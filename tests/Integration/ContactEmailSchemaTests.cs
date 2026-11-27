using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed class ContactEmailSchemaTests : IAsyncLifetime
{
    private const string Previous = "20261127000000_ContactFoundation";
    private readonly PostgresFixture db = new();
    private readonly Guid tenant = Guid.NewGuid(), other = Guid.NewGuid();
    private NpgsqlConnection owner = null!;
    private PlatformDbContext context = null!;

    public async Task InitializeAsync()
    {
        await db.InitializeAsync();
        owner = new NpgsqlConnection(db.MigrationConnectionString);
        await owner.OpenAsync();
        context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(owner,
            p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.GetService<IMigrator>().MigrateAsync(Previous);
        foreach (var id in new[] { tenant, other })
            await Sql(owner, "SELECT platform.create_tenant_with_owner($1,'Example','issuer','subject','correlation')", id);
        await DatabaseRoleGrants.ApplyAsync(owner, db.ApplicationRole, default);
    }

    public async Task DisposeAsync()
    {
        await context.DisposeAsync();
        await owner.DisposeAsync();
        await db.DisposeAsync();
    }

    private static async Task<object?> Sql(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.Add(new NpgsqlParameter { Value = value });
        return await command.ExecuteScalarAsync();
    }

    private async Task<NpgsqlConnection> AppAsync()
    {
        var connection = new NpgsqlConnection(db.ApplicationConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static Task<object?> SetTenant(NpgsqlConnection app, Guid tenant) =>
        Sql(app, "SELECT set_config('crm.tenant_id',$1,true)", tenant.ToString());

    private static Task<object?> Insert(NpgsqlConnection app, Guid tenant, string? email) =>
        Sql(app, "INSERT INTO crm.contacts(tenant_id,id,display_name,email) VALUES($1,gen_random_uuid(),'Name',$2) RETURNING id", tenant, (object?)email ?? DBNull.Value);

    [Fact]
    public async Task Normalized_active_email_is_unique_per_tenant_null_allowed_and_deleted_reusable()
    {
        await context.Database.MigrateAsync();
        await using var app = await AppAsync();
        await using (var tx = await app.BeginTransactionAsync())
        {
            await SetTenant(app, tenant);
            var id = (Guid)(await Insert(app, tenant, "  Alice+tag@EXAMPLE.com "))!;
            Assert.Equal("alice+tag@example.com", await Sql(app, "SELECT email_normalized FROM crm.contacts WHERE id=$1", id));
            await Insert(app, tenant, null);
            await Insert(app, tenant, null);
            await tx.CommitAsync();
        }
        await using (var tx = await app.BeginTransactionAsync())
        {
            await SetTenant(app, tenant);
            Assert.Equal(PostgresErrorCodes.UniqueViolation,
                (await Assert.ThrowsAsync<PostgresException>(() => Insert(app, tenant, "alice+tag@example.COM"))).SqlState);
            await tx.RollbackAsync();
        }
        await using (var tx = await app.BeginTransactionAsync())
        {
            await SetTenant(app, other);
            await Insert(app, other, "alice+tag@example.com");
            await tx.CommitAsync();
        }
        await using (var tx = await app.BeginTransactionAsync())
        {
            await SetTenant(app, tenant);
            await Sql(app, "UPDATE crm.contacts SET deleted_at=now() WHERE email IS NOT NULL");
            await Insert(app, tenant, "ALICE+TAG@example.com");
            await Insert(app, tenant, "another@example.com");
            Assert.Equal(PostgresErrorCodes.UniqueViolation,
                (await Assert.ThrowsAsync<PostgresException>(() => Sql(app, "UPDATE crm.contacts SET email=' alice+tag@example.com ' WHERE email='another@example.com'"))).SqlState);
            await tx.RollbackAsync();
        }
        await app.CloseAsync();
        await app.OpenAsync();
        Assert.Equal(0L, await Sql(app, "SELECT count(*) FROM crm.contacts"));
        Assert.Equal(false, await Sql(app, "SELECT has_schema_privilege(current_user,'crm','CREATE')"));
        Assert.Equal(true, await Sql(owner, "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='crm.contacts'::regclass"));
    }

    [Fact]
    public async Task Upgrade_retains_existing_data_and_backfills_normalized_values()
    {
        await using var app = await AppAsync();
        foreach (var t in new[] { tenant, other })
        {
            await using var tx = await app.BeginTransactionAsync();
            await SetTenant(app, t);
            await Insert(app, t, "  Existing@Example.COM ");
            await Insert(app, t, null);
            await tx.CommitAsync();
        }
        await context.Database.MigrateAsync();
        await using var check = await app.BeginTransactionAsync();
        await SetTenant(app, other);
        Assert.Equal(1L, await Sql(app, "SELECT count(*) FROM crm.contacts WHERE email_normalized='existing@example.com'"));
        Assert.Equal(2L, await Sql(app, "SELECT count(*) FROM crm.contacts"));
        await check.CommitAsync();
    }

    [Fact]
    public async Task Existing_duplicates_fail_upgrade_atomically_without_disclosing_email_or_changing_rows()
    {
        await using var app = await AppAsync();
        await using (var tx = await app.BeginTransactionAsync())
        {
            await SetTenant(app, other);
            await Insert(app, other, " Private@example.com ");
            await Insert(app, other, "private@EXAMPLE.com");
            await tx.CommitAsync();
        }
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.MigrateAsync());
        Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        Assert.Contains("CONTACT_EMAIL_DUPLICATES", error.MessageText);
        Assert.DoesNotContain("private", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DBNull.Value, await Sql(owner, "SELECT to_regclass('crm.contacts_active_email_unique')::text"));
        await using (var tx = await app.BeginTransactionAsync())
        {
            await SetTenant(app, other);
            Assert.Equal(2L, await Sql(app, "SELECT count(*) FROM crm.contacts"));
            await tx.CommitAsync();
        }
    }

    [Fact]
    public async Task Concurrent_creates_with_same_normalized_email_have_one_winner()
    {
        await context.Database.MigrateAsync();
        await using var first = await AppAsync();
        await using var second = await AppAsync();
        await using var tx1 = await first.BeginTransactionAsync();
        await using var tx2 = await second.BeginTransactionAsync();
        await SetTenant(first, tenant);
        await SetTenant(second, tenant);
        var secondPid = (int)(await Sql(second, "SELECT pg_backend_pid()"))!;
        await Insert(first, tenant, " race@example.com ");
        var competing = Insert(second, tenant, "RACE@example.com");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !(bool)(await Sql(owner,
                   "SELECT cardinality(pg_blocking_pids($1)) > 0", secondPid))!)
            await Task.Delay(25);
        Assert.Equal(true, await Sql(owner, "SELECT cardinality(pg_blocking_pids($1)) > 0", secondPid));
        await tx1.CommitAsync();
        Assert.Equal(PostgresErrorCodes.UniqueViolation,
            (await Assert.ThrowsAsync<PostgresException>(async () => await competing)).SqlState);
        await tx2.RollbackAsync();
    }

    [Fact]
    public async Task Rollback_succeeds_empty_but_requires_forward_fix_with_contact_data()
    {
        await context.Database.MigrateAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await migrator.MigrateAsync();
        await using var app = await AppAsync();
        await using (var tx = await app.BeginTransactionAsync())
        {
            await SetTenant(app, other);
            await Insert(app, other, "test@example.com");
            await tx.CommitAsync();
        }
        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Previous));
        Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        Assert.Contains("forward fix", error.MessageText);
    }
}
