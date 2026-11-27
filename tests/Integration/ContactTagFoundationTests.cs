using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed class ContactTagFoundationTests : IAsyncLifetime
{
    private const string Previous = "20261127010000_ContactEmailUpsert";
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
        await context.Database.MigrateAsync();
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

    private static async Task SetTenant(NpgsqlConnection conn, Guid id) =>
        await Sql(conn, "SELECT set_config('crm.tenant_id',$1,true)", id.ToString());

    [Fact]
    public async Task Upgrade_enforces_tenant_keys_names_rls_and_read_only_role()
    {
        foreach (var table in new[] { "tags", "contact_tags" })
        {
            Assert.Equal(true, await Sql(owner, "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid=$1::regclass", $"crm.{table}"));
            Assert.Equal(false, await Sql(owner, "SELECT has_table_privilege($1,$2,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')", db.ApplicationRole, $"crm.{table}"));
            Assert.Equal(true, await Sql(owner, "SELECT has_table_privilege($1,$2,'SELECT')", db.ApplicationRole, $"crm.{table}"));
        }
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        Assert.Equal(true, await Sql(app, "SELECT NOT rolbypassrls AND NOT rolsuper FROM pg_roles WHERE rolname=current_user"));
        Assert.Equal(false, await Sql(app, "SELECT EXISTS(SELECT FROM pg_class WHERE oid IN ('crm.tags'::regclass,'crm.contact_tags'::regclass) AND pg_has_role(current_user,relowner,'MEMBER'))"));
        var tag = Guid.NewGuid(); var contact = Guid.NewGuid();
        foreach (var t in new[] { tenant, other })
        {
            await using var tx = await owner.BeginTransactionAsync();
            await SetTenant(owner, t);
            await Sql(owner, "INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES($1,$2,'Contact')", t, contact);
            await Sql(owner, "INSERT INTO crm.tags(tenant_id,id,name,normalized_name) VALUES($1,$2,'Name','name')", t, tag);
            await Sql(owner, "INSERT INTO crm.contact_tags(tenant_id,contact_id,tag_id) VALUES($1,$2,$3)", t, contact, tag);
            await tx.CommitAsync();
        }
        Assert.Equal(0L, await Sql(app, "SELECT count(*) FROM crm.tags"));
        Assert.Equal(0L, await Sql(app, "SELECT count(*) FROM crm.contact_tags"));
        await using (var tx = await app.BeginTransactionAsync())
        {
            await SetTenant(app, tenant);
            Assert.Equal(1L, await Sql(app, "SELECT count(*) FROM crm.tags"));
            Assert.Equal(1L, await Sql(app, "SELECT count(*) FROM crm.contact_tags ct JOIN crm.tags t ON t.tenant_id=ct.tenant_id AND t.id=ct.tag_id JOIN crm.contacts c ON c.tenant_id=ct.tenant_id AND c.id=ct.contact_id"));
            var ex = await Assert.ThrowsAsync<PostgresException>(() => Sql(app, "INSERT INTO crm.tags(tenant_id,id,name,normalized_name) VALUES($1,gen_random_uuid(),'x','x')", tenant));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
            await tx.RollbackAsync();
        }
        foreach (var sql in new[] { "UPDATE crm.tags SET name='x'", "DELETE FROM crm.tags", "INSERT INTO crm.contact_tags(tenant_id,contact_id,tag_id) VALUES(gen_random_uuid(),gen_random_uuid(),gen_random_uuid())", "UPDATE crm.contact_tags SET tag_id=gen_random_uuid()", "DELETE FROM crm.contact_tags" })
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => Sql(app, sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        }
    }

    [Fact]
    public async Task Duplicate_names_and_cross_tenant_references_are_rejected()
    {
        var tag = Guid.NewGuid(); var contact = Guid.NewGuid();
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await SetTenant(owner, tenant);
            await Sql(owner, "INSERT INTO crm.tags(tenant_id,id,name,normalized_name) VALUES($1,$2,'Name','name')", tenant, tag);
            await Sql(owner, "INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES($1,$2,'Contact')", tenant, contact);
            await tx.CommitAsync();
        }
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await SetTenant(owner, tenant);
            var duplicate = await Assert.ThrowsAsync<PostgresException>(() => Sql(owner, "INSERT INTO crm.tags(tenant_id,id,name,normalized_name) VALUES($1,gen_random_uuid(),'Other','name')", tenant));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
            await tx.RollbackAsync();
        }
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await SetTenant(owner, other);
            var foreign = await Assert.ThrowsAsync<PostgresException>(() => Sql(owner, "INSERT INTO crm.contact_tags(tenant_id,contact_id,tag_id) VALUES($1,$2,$3)", other, contact, tag));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreign.SqlState);
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Link_requires_each_reference_in_same_tenant_and_is_unique()
    {
        var tag = Guid.NewGuid(); var contact = Guid.NewGuid();
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await SetTenant(owner, tenant);
            await Sql(owner, "INSERT INTO crm.tags(tenant_id,id,name,normalized_name) VALUES($1,$2,'Name','name')", tenant, tag);
            await Sql(owner, "INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES($1,$2,'Contact')", tenant, contact);
            await Sql(owner, "INSERT INTO crm.contact_tags(tenant_id,contact_id,tag_id) VALUES($1,$2,$3)", tenant, contact, tag);
            await tx.CommitAsync();
        }
        foreach (var pair in new[] { (contact, tag, PostgresErrorCodes.UniqueViolation), (contact, Guid.NewGuid(), PostgresErrorCodes.ForeignKeyViolation), (Guid.NewGuid(), tag, PostgresErrorCodes.ForeignKeyViolation) })
        {
            await using var tx = await owner.BeginTransactionAsync();
            await SetTenant(owner, tenant);
            var error = await Assert.ThrowsAsync<PostgresException>(() => Sql(owner, "INSERT INTO crm.contact_tags(tenant_id,contact_id,tag_id) VALUES($1,$2,$3)", tenant, pair.Item1, pair.Item2));
            Assert.Equal(pair.Item3, error.SqlState);
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Empty_rollback_succeeds_and_populated_rollback_requires_backup_and_forward_fix()
    {
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        Assert.Equal(DBNull.Value, await Sql(owner, "SELECT to_regclass('crm.tags')::text"));
        await migrator.MigrateAsync();
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await SetTenant(owner, tenant);
            await Sql(owner, "INSERT INTO crm.tags(tenant_id,id,name,normalized_name) VALUES($1,gen_random_uuid(),'Name','name')", tenant);
            await tx.CommitAsync();
        }
        var ex = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Previous));
        Assert.Equal(PostgresErrorCodes.RaiseException, ex.SqlState);
        Assert.Contains("back up", ex.MessageText);
        Assert.Contains("forward fix", ex.MessageText);
    }

    [Theory]
    [InlineData("crm.tags")]
    [InlineData("crm.contact_tags")]
    public async Task Validation_rejects_disabled_force_rls_or_write_grants(string table)
    {
        await Sql(owner, $"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY");
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGrants.ValidateAsync(owner, db.ApplicationRole, default));
        await Sql(owner, $"ALTER TABLE {table} FORCE ROW LEVEL SECURITY");
        await Sql(owner, $"GRANT INSERT ON {table} TO {PostgresFixture.Quote(db.ApplicationRole)}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGrants.ValidateAsync(owner, db.ApplicationRole, default));
    }
}
