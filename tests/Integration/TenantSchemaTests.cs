using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Platform.Infrastructure.Identity;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed class TenantSchemaTests : IAsyncLifetime
{
    private readonly PostgresFixture db = new();
    public Task InitializeAsync() => db.InitializeAsync();
    public Task DisposeAsync() => db.DisposeAsync();

    private PlatformDbContext Context(NpgsqlConnection c) => new(new DbContextOptionsBuilder<PlatformDbContext>()
        .UseNpgsql(c, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);

    [Fact]
    public async Task Application_role_cannot_be_member_of_bypass_role()
    {
        await using var admin = new NpgsqlConnection(db.AdminConnectionString);
        await admin.OpenAsync();
        var bypass = "bypass_" + Guid.NewGuid().ToString("N");
        await PostgresFixture.ExecuteAsync(admin, $"CREATE ROLE {PostgresFixture.Quote(bypass)} NOLOGIN BYPASSRLS; GRANT {PostgresFixture.Quote(bypass)} TO {PostgresFixture.Quote(db.ApplicationRole)}");
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGrants.ValidateAsync(migration, db.ApplicationRole, CancellationToken.None));
    }

    [Theory]
    [InlineData("memberships", "status", "UPDATE", "direct")]
    [InlineData("memberships", "status", "UPDATE", "public")]
    [InlineData("memberships", "status", "UPDATE", "inherited")]
    [InlineData("tenants", "name", "SELECT", "direct")]
    [InlineData("tenants", "name", "SELECT", "public")]
    [InlineData("tenants", "name", "SELECT", "inherited")]
    public async Task Application_role_with_forbidden_column_privilege_is_rejected(string table, string column, string privilege, string source)
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await using var context = Context(migration);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(migration, db.ApplicationRole, CancellationToken.None);
        var grantee = source switch
        {
            "public" => "PUBLIC",
            "inherited" => PostgresFixture.Quote("column_grant_" + Guid.NewGuid().ToString("N")),
            _ => PostgresFixture.Quote(db.ApplicationRole)
        };
        if (source == "inherited")
        {
            await using var admin = new NpgsqlConnection(db.AdminConnectionString);
            await admin.OpenAsync();
            await PostgresFixture.ExecuteAsync(admin, $"CREATE ROLE {grantee} NOLOGIN; GRANT {grantee} TO {PostgresFixture.Quote(db.ApplicationRole)}");
        }
        await PostgresFixture.ExecuteAsync(migration, $"GRANT {privilege} ({column}) ON platform.{table} TO {grantee}");
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        await using var effective = new NpgsqlCommand("SELECT has_column_privilege(current_user, @table, @column, @privilege)", app);
        effective.Parameters.AddWithValue("table", "platform." + table);
        effective.Parameters.AddWithValue("column", column);
        effective.Parameters.AddWithValue("privilege", privilege);
        Assert.Equal(true, await effective.ExecuteScalarAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGrants.ApplyAsync(migration, db.ApplicationRole, CancellationToken.None));
    }

    [Fact]
    public async Task Existing_application_role_with_excess_membership_privilege_is_rejected()
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await using var context = Context(migration);
        await context.Database.MigrateAsync();
        await PostgresFixture.ExecuteAsync(migration, $"GRANT INSERT ON platform.memberships TO {PostgresFixture.Quote(db.ApplicationRole)}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGrants.ApplyAsync(migration, db.ApplicationRole, CancellationToken.None));
    }

    [Fact]
    public async Task Upgrade_constraints_lookup_privileges_and_rollback()
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString);
        await migration.OpenAsync();
        await using var context = Context(migration);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261108144703_InitialPlatformInfrastructure");
        await migrator.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(migration, db.ApplicationRole, CancellationToken.None);
        await DatabaseRoleGrants.ApplyAsync(migration, db.ApplicationRole, CancellationToken.None);

        var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await PostgresFixture.ExecuteAsync(migration, $"""
            INSERT INTO platform.tenants(id,name,status,created_at) VALUES ('{a}','A','active',now()), ('{b}','B','suspended',now());
            INSERT INTO platform.memberships(tenant_id,id,issuer,subject,role,status,version) VALUES
            ('{a}',gen_random_uuid(),'issuer','subject','owner','active',1),
            ('{b}',gen_random_uuid(),'issuer','subject','agent','active',1),
            ('{a}',gen_random_uuid(),'issuer','other','operator','inactive',1),
            ('{b}',gen_random_uuid(),'other-issuer','other-subject','admin','active',1);
            """);
        foreach (var sql in new[] {
            $"INSERT INTO platform.tenants VALUES (gen_random_uuid(),'', 'active', now())",
            $"INSERT INTO platform.tenants VALUES (gen_random_uuid(),repeat('a',201), 'active', now())",
            $"INSERT INTO platform.tenants VALUES (gen_random_uuid(),'x','invalid',now())",
            $"INSERT INTO platform.memberships VALUES ('{a}',gen_random_uuid(),'issuer','subject','owner','active',1)",
            "INSERT INTO platform.memberships SELECT tenant_id,id,'other-issuer','new-subject','agent','active',1 FROM platform.memberships LIMIT 1",
            $"INSERT INTO platform.memberships VALUES (gen_random_uuid(),gen_random_uuid(),'i','s','owner','active',1)",
            $"INSERT INTO platform.memberships VALUES ('{a}',gen_random_uuid(),'i','s','invalid','active',1)",
            $"INSERT INTO platform.memberships VALUES ('{a}',gen_random_uuid(),'i','s','owner','invalid',1)",
            $"INSERT INTO platform.memberships VALUES ('{a}',gen_random_uuid(),'i','s','owner','active',0)" })
            await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.ExecuteAsync(migration, sql));

        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        var rows = await MembershipLookup.ListAsync(app, "issuer", "subject", null, 101, CancellationToken.None);
        Assert.Equal(new[] { a, b }, rows.Select(x => x.Id));
        Assert.Equal("suspended", rows[1].Status);
        Assert.Empty(await MembershipLookup.ListAsync(app, "issuer", "other", null, 101, CancellationToken.None));
        Assert.Empty(await MembershipLookup.ListAsync(app, "", "subject", null, 101, CancellationToken.None));
        Assert.Equal(b, Assert.Single(await MembershipLookup.ListAsync(app, "issuer", "subject", a, 1, CancellationToken.None)).Id);
        await using (var read = new NpgsqlCommand("SELECT count(*) FROM platform.memberships", app))
            Assert.Equal(0L, await read.ExecuteScalarAsync());
        foreach (var sql in new[] { "SELECT * FROM platform.tenants", "INSERT INTO platform.memberships DEFAULT VALUES", "UPDATE platform.memberships SET status='inactive'", "DELETE FROM platform.memberships", "CREATE TABLE platform.bad(id int)", "CREATE OR REPLACE FUNCTION platform.list_principal_tenants(text,text,uuid,integer) RETURNS int LANGUAGE SQL AS 'SELECT 1'" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.ExecuteAsync(app, sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
        await using (var check = new NpgsqlCommand("""
            SELECT NOT r.rolsuper AND NOT r.rolbypassrls AND c.relowner <> r.oid
              AND NOT has_function_privilege('public','platform.list_principal_tenants(text,text,uuid,integer)','EXECUTE')
              AND NOT has_function_privilege('public','platform.lock_active_membership(text,text,uuid)','EXECUTE')
            FROM pg_roles r, pg_class c WHERE r.rolname=current_user AND c.oid='platform.memberships'::regclass
            """, app)) Assert.Equal(true, await check.ExecuteScalarAsync());
        // The definer is deliberately not an authentication boundary: an app credential
        // can request any principal. Only the authenticated API may hold that credential.
        Assert.Equal(b, Assert.Single(await MembershipLookup.ListAsync(app, "other-issuer", "other-subject", null, 101, CancellationToken.None)).Id);
        await using (var transaction = await app.BeginTransactionAsync())
        {
            await PostgresFixture.ExecuteAsync(app, $"SELECT set_config('crm.tenant_id','{a}',true)");
            await using var readA = new NpgsqlCommand("SELECT count(*) FROM platform.memberships", app, transaction);
            Assert.Equal(2L, await readA.ExecuteScalarAsync());
            await transaction.RollbackAsync();
        }
        await using (var transaction = await app.BeginTransactionAsync())
        {
            await PostgresFixture.ExecuteAsync(app, "SELECT set_config('crm.tenant_id','not-a-uuid',true)");
            var failure = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.ExecuteAsync(app, "SELECT * FROM platform.memberships"));
            Assert.Equal(PostgresErrorCodes.InvalidTextRepresentation, failure.SqlState);
            await transaction.RollbackAsync();
        }
        // Grant TEMP solely for a hostile search_path probe; production role does not require it.
        await using (var admin = new NpgsqlConnection(db.AdminConnectionString))
        {
            await admin.OpenAsync();
            await PostgresFixture.ExecuteAsync(admin, $"GRANT TEMPORARY ON DATABASE {PostgresFixture.Quote(db.DatabaseName)} TO {PostgresFixture.Quote(db.ApplicationRole)}");
        }
        await PostgresFixture.ExecuteAsync(app, "CREATE TEMP TABLE tenants(id uuid, name text, status text); CREATE TEMP TABLE memberships(tenant_id uuid, issuer text, subject text, status text); SET search_path=pg_temp,platform");
        Assert.Equal(2, (await MembershipLookup.ListAsync(app, "issuer", "subject", null, 101, CancellationToken.None)).Count);
        await using (var cmd = new NpgsqlCommand("SELECT platform.lock_active_membership('issuer','subject',@id)", app))
        {
            cmd.Parameters.AddWithValue("id", a);
            Assert.Equal(true, await cmd.ExecuteScalarAsync());
            cmd.Parameters["id"].Value = b;
            Assert.Equal(false, await cmd.ExecuteScalarAsync());
            cmd.Parameters["id"].Value = a;
            cmd.CommandText = "SELECT platform.lock_active_membership('issuer','other',@id)";
            Assert.Equal(false, await cmd.ExecuteScalarAsync());
        }
        await migrator.MigrateAsync("20261108144703_InitialPlatformInfrastructure");
        await using var checkDown = new NpgsqlCommand("SELECT to_regclass('platform.memberships') IS NULL AND to_regprocedure('platform.list_principal_tenants(text,text,uuid,integer)') IS NULL AND to_regclass('platform.\"__EFMigrationsHistory\"') IS NOT NULL", migration);
        Assert.Equal(true, await checkDown.ExecuteScalarAsync());
    }
}
