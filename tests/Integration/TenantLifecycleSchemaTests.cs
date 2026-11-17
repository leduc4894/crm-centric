using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed class TenantLifecycleSchemaTests : IAsyncLifetime
{
    private readonly PostgresFixture db = new();
    public Task InitializeAsync() => db.InitializeAsync();
    public Task DisposeAsync() => db.DisposeAsync();

    [Fact]
    public async Task Create_tenant_persists_owner_and_audit_atomically()
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(migration, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(migration, db.ApplicationRole, CancellationToken.None);
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        var tenant = Guid.NewGuid();
        await using (var create = new NpgsqlCommand("SELECT platform.create_tenant_with_owner(@id,'Example','issuer','subject','correlation')", app))
        {
            create.Parameters.AddWithValue("id", tenant);
            await create.ExecuteNonQueryAsync();
        }
        await using var verify = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM platform.tenants WHERE id=@id),
                   (SELECT count(*) FROM platform.memberships WHERE tenant_id=@id AND issuer='issuer' AND subject='subject' AND role='owner' AND status='active' AND version=1),
                   (SELECT count(*) FROM audit.entries WHERE tenant_id=@id)
            """, migration);
        verify.Parameters.AddWithValue("id", tenant);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
    }
    [Fact]
    public async Task Audit_failure_rolls_back_tenant_and_owner_and_blocks_unsafe_access()
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(migration, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(migration, db.ApplicationRole, CancellationToken.None);
        await PostgresFixture.ExecuteAsync(migration, """
            CREATE FUNCTION audit.reject_entry() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected failure'; END $$;
            CREATE TRIGGER reject_entry BEFORE INSERT ON audit.entries FOR EACH ROW EXECUTE FUNCTION audit.reject_entry();
            """);
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        var tenant = Guid.NewGuid();
        await using (var create = new NpgsqlCommand("SELECT platform.create_tenant_with_owner(@id,'Example','issuer','subject','correlation')", app))
        {
            create.Parameters.AddWithValue("id", tenant);
            await Assert.ThrowsAsync<PostgresException>(() => create.ExecuteNonQueryAsync());
        }
        await using (var verify = new NpgsqlCommand("SELECT (SELECT count(*) FROM platform.tenants WHERE id=@id) + (SELECT count(*) FROM platform.memberships WHERE tenant_id=@id) + (SELECT count(*) FROM audit.entries WHERE tenant_id=@id)", migration))
        {
            verify.Parameters.AddWithValue("id", tenant);
            Assert.Equal(0L, await verify.ExecuteScalarAsync());
        }
        await using (var check = new NpgsqlCommand("SELECT NOT has_function_privilege('public','platform.create_tenant_with_owner(uuid,text,text,text,text)','EXECUTE') AND NOT has_function_privilege('public','platform.update_membership(uuid,uuid,bigint,text,text,text,text,text)','EXECUTE')", migration))
            Assert.Equal(true, await check.ExecuteScalarAsync());
        foreach (var sql in new[] { "SELECT * FROM audit.entries", "UPDATE audit.entries SET summary='x'", "DELETE FROM audit.entries", "TRUNCATE audit.entries" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.ExecuteAsync(app, sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
    }

    [Fact]
    public async Task Populated_audit_refuses_rollback()
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(migration, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await PostgresFixture.ExecuteAsync(migration, "SELECT platform.create_tenant_with_owner(gen_random_uuid(),'Example','issuer','subject','correlation')");
        var migrator = context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261116000000_TenantFoundation"));
        await using var count = new NpgsqlCommand("SELECT count(*) FROM audit.entries", migration);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Cross_tenant_update_is_hidden_and_audit_failure_rolls_back_update()
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(migration, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(migration, db.ApplicationRole, CancellationToken.None);
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var member = Guid.NewGuid();
        await PostgresFixture.ExecuteAsync(migration, $"""
            SELECT platform.create_tenant_with_owner('{a}','A','issuer','subject','correlation');
            SELECT platform.create_tenant_with_owner('{b}','B','issuer','subject','correlation');
            INSERT INTO platform.memberships VALUES ('{b}','{member}','other','subject','agent','active',1);
            """);
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        await using (var transaction = await app.BeginTransactionAsync())
        {
            await PostgresFixture.ExecuteAsync(app, $"SELECT set_config('crm.tenant_id','{a}',true)");
            await using var attempt = new NpgsqlCommand("SELECT outcome FROM platform.update_membership(@tenant,@target,1,'admin',NULL,'issuer','subject','correlation')", app, transaction);
            attempt.Parameters.AddWithValue("tenant", b); attempt.Parameters.AddWithValue("target", member);
            Assert.Equal("not_found", await attempt.ExecuteScalarAsync());
            await transaction.RollbackAsync();
        }
        await PostgresFixture.ExecuteAsync(migration, """
            CREATE FUNCTION audit.reject_entry() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected failure'; END $$;
            CREATE TRIGGER reject_entry BEFORE INSERT ON audit.entries FOR EACH ROW EXECUTE FUNCTION audit.reject_entry();
            """);
        await using (var transaction = await app.BeginTransactionAsync())
        {
            await PostgresFixture.ExecuteAsync(app, $"SELECT set_config('crm.tenant_id','{b}',true)");
            await using var attempt = new NpgsqlCommand("SELECT outcome FROM platform.update_membership(@tenant,@target,1,'admin',NULL,'issuer','subject','correlation')", app, transaction);
            attempt.Parameters.AddWithValue("tenant", b); attempt.Parameters.AddWithValue("target", member);
            await Assert.ThrowsAsync<PostgresException>(() => attempt.ExecuteScalarAsync());
            await transaction.RollbackAsync();
        }
        await using var verify = new NpgsqlCommand("SELECT role,version FROM platform.memberships WHERE tenant_id=@tenant AND id=@id", migration);
        verify.Parameters.AddWithValue("tenant", b); verify.Parameters.AddWithValue("id", member);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal("agent", reader.GetString(0)); Assert.Equal(1L, reader.GetInt64(1));
    }

    [Fact]
    public async Task Upgrade_preserves_inactive_membership_and_lookup_denies_it()
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(migration, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        var migrator = context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync("20261116000000_TenantFoundation");
        var tenant = Guid.NewGuid();
        await PostgresFixture.ExecuteAsync(migration, $"""
            INSERT INTO platform.tenants VALUES ('{tenant}','Existing','active',now());
            INSERT INTO platform.memberships VALUES ('{tenant}',gen_random_uuid(),'issuer','subject','agent','inactive',1);
            """);
        await migrator.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(migration, db.ApplicationRole, CancellationToken.None);
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        Assert.Empty(await Platform.Infrastructure.Identity.MembershipLookup.ListAsync(app, "issuer", "subject", null, 20, CancellationToken.None));
        await using var status = new NpgsqlCommand("SELECT status FROM platform.memberships WHERE tenant_id=@tenant", migration);
        status.Parameters.AddWithValue("tenant", tenant);
        Assert.Equal("inactive", await status.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Update_records_bounded_fields_and_noop_does_not_increment()
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(migration, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(migration, db.ApplicationRole, CancellationToken.None);
        var tenant = Guid.NewGuid(); var member = Guid.NewGuid();
        await PostgresFixture.ExecuteAsync(migration, $"""
            SELECT platform.create_tenant_with_owner('{tenant}','A','issuer','subject','correlation');
            INSERT INTO platform.memberships VALUES ('{tenant}','{member}','other','subject','agent','active',1);
            """);
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        await using (var transaction = await app.BeginTransactionAsync())
        {
            await PostgresFixture.ExecuteAsync(app, $"SELECT set_config('crm.tenant_id','{tenant}',true)");
            var result = await Platform.Infrastructure.Identity.LifecycleStore.UpdateMembershipAsync(app, transaction, tenant, member, 1, "admin", null, "issuer", "subject", "correlation", CancellationToken.None);
            Assert.Equal(Platform.Infrastructure.Identity.MembershipUpdateOutcome.Updated, result.Outcome); Assert.Equal(2, result.Membership!.Version);
            var noop = await Platform.Infrastructure.Identity.LifecycleStore.UpdateMembershipAsync(app, transaction, tenant, member, 2, "admin", null, "issuer", "subject", "correlation", CancellationToken.None);
            Assert.Equal(Platform.Infrastructure.Identity.MembershipUpdateOutcome.Unchanged, noop.Outcome); Assert.Equal(2, noop.Membership!.Version);
            await transaction.CommitAsync();
        }
        await using var audit = new NpgsqlCommand("SELECT summary FROM audit.entries WHERE tenant_id=@tenant AND action='membership.updated'", migration);
        audit.Parameters.AddWithValue("tenant", tenant);
        await using var reader = await audit.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var summary = reader.GetString(0);
        Assert.Contains("agent", summary); Assert.Contains("admin", summary); Assert.DoesNotContain("subject", summary);
        Assert.False(await reader.ReadAsync());
    }

    [Theory]
    [InlineData("GRANT SELECT (summary) ON audit.entries TO PUBLIC")]
    [InlineData("GRANT EXECUTE ON FUNCTION platform.create_tenant_with_owner(uuid,text,text,text,text) TO PUBLIC")]
    [InlineData("GRANT EXECUTE ON FUNCTION platform.update_membership(uuid,uuid,bigint,text,text,text,text,text) TO PUBLIC")]
    public async Task Unsafe_grants_fail_role_validation(string grant)
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(migration, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await PostgresFixture.ExecuteAsync(migration, grant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGrants.ValidateAsync(migration, db.ApplicationRole, CancellationToken.None));
    }

    [Fact]
    public async Task Duplicate_tenant_id_leaves_one_tenant_owner_and_audit_entry()
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(migration, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(migration, db.ApplicationRole, CancellationToken.None);
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        var tenant = Guid.NewGuid();
        await using var create = new NpgsqlCommand("SELECT platform.create_tenant_with_owner(@id,'Example','issuer','subject','correlation')", app);
        create.Parameters.AddWithValue("id", tenant);
        await create.ExecuteNonQueryAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => create.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        await AssertCountsAsync(migration, tenant, 1, 1, 1);
    }

    [Fact]
    public async Task Terminating_uncommitted_create_rolls_back_tenant_owner_and_audit()
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(migration, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(migration, db.ApplicationRole, CancellationToken.None);
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        var tenant = Guid.NewGuid();
        await using var transaction = await app.BeginTransactionAsync();
        await using (var create = new NpgsqlCommand("SELECT platform.create_tenant_with_owner(@id,'Example','issuer','subject','correlation')", app, transaction))
        {
            create.Parameters.AddWithValue("id", tenant);
            await create.ExecuteNonQueryAsync();
        }
        await using var admin = new NpgsqlConnection(db.AdminConnectionString);
        await admin.OpenAsync();
        await using (var terminate = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", admin))
        {
            terminate.Parameters.AddWithValue("pid", app.ProcessID);
            Assert.Equal(true, await terminate.ExecuteScalarAsync());
        }
        await Assert.ThrowsAnyAsync<NpgsqlException>(async () =>
        {
            await using var probe = new NpgsqlCommand("SELECT 1", app, transaction);
            await probe.ExecuteNonQueryAsync();
        });
        await AssertCountsAsync(migration, tenant, 0, 0, 0);
    }

    private static async Task AssertCountsAsync(NpgsqlConnection connection, Guid tenant, long tenants, long owners, long entries)
    {
        await using var verify = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM platform.tenants WHERE id=@tenant),
                   (SELECT count(*) FROM platform.memberships WHERE tenant_id=@tenant AND role='owner'),
                   (SELECT count(*) FROM audit.entries WHERE tenant_id=@tenant)
            """, connection);
        verify.Parameters.AddWithValue("tenant", tenant);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(tenants, reader.GetInt64(0));
        Assert.Equal(owners, reader.GetInt64(1));
        Assert.Equal(entries, reader.GetInt64(2));
    }

}
