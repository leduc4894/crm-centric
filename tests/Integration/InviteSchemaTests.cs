using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed class InviteSchemaTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261118010000_VerifiedStaffBindings";
    private readonly PostgresFixture db = new();
    private readonly Guid tenant = Guid.NewGuid();
    private readonly Guid otherTenant = Guid.NewGuid();
    private NpgsqlConnection owner = null!;
    private PlatformDbContext context = null!;

    public async Task InitializeAsync()
    {
        await db.InitializeAsync();
        owner = new NpgsqlConnection(db.MigrationConnectionString);
        await owner.OpenAsync();
        context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(owner, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        foreach (var id in new[] { tenant, otherTenant })
            await Scalar(owner, "SELECT platform.create_tenant_with_owner($1,'Example','issuer','subject','correlation')", id);
        await Audit("membership.updated");
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(owner, db.ApplicationRole, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await context.DisposeAsync();
        await owner.DisposeAsync();
        await db.DisposeAsync();
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        return await command.ExecuteScalarAsync();
    }

    private Task<object?> Insert(Guid tenantId, byte[] hash) => Scalar(owner, """
        INSERT INTO platform.invites(tenant_id,id,token_hash,email,normalized_email,role,created_at,expires_at)
        VALUES($1,gen_random_uuid(),$2,'a@Example.com','a@example.com','agent',now(),now()+interval '24 hours')
        """, tenantId, hash);

    private Task<object?> Audit(string action) => Scalar(owner, """
        INSERT INTO audit.entries(id,tenant_id,actor_issuer,actor_subject,action,target_id,summary,correlation_id)
        VALUES(gen_random_uuid(),$1,'issuer','subject',$2,gen_random_uuid(),'summary','correlation')
        """, tenant, action);

    [Fact]
    public async Task Lookup_is_scalar_uuid_and_role_cannot_access_any_invite_column_or_mutate()
    {
        var digest = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        await Insert(tenant, digest);
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        Assert.Equal(tenant, await Scalar(app, "SELECT platform.invite_tenant_for_hash($1)", digest));
        Assert.Equal(DBNull.Value, await Scalar(app, "SELECT platform.invite_tenant_for_hash($1)", new byte[31]));
        Assert.Equal(true, await Scalar(owner, """
            SELECT NOT proretset AND prorettype='uuid'::regtype AND prosecdef
              AND proconfig=ARRAY['search_path=pg_catalog']
              AND NOT has_function_privilege('public',oid,'EXECUTE')
              FROM pg_proc WHERE oid='platform.invite_tenant_for_hash(bytea)'::regprocedure
            """));
        Assert.Equal(true, await Scalar(app, """
            SELECT NOT has_any_column_privilege(current_user,'platform.invites','SELECT,INSERT,UPDATE,REFERENCES')
              AND NOT has_table_privilege(current_user,'platform.invites','SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
            """));
    }

    [Fact]
    public async Task Rls_scopes_nonowner_even_with_column_read_and_grant_validation_rejects_drift()
    {
        await Insert(tenant, new byte[32]);
        await Insert(otherTenant, Enumerable.Repeat((byte)1,32).ToArray());
        await Scalar(owner, $"GRANT SELECT (tenant_id) ON platform.invites TO {PostgresFixture.Quote(db.ApplicationRole)}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGrants.ValidateAsync(owner, db.ApplicationRole, default));
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        Assert.Equal(0L, await Scalar(app, "SELECT count(tenant_id) FROM platform.invites"));
        await using (var tx = await app.BeginTransactionAsync())
        {
            await Scalar(app, "SELECT set_config('crm.tenant_id',$1,true)", tenant.ToString());
            Assert.Equal(new[] { tenant }, await Scalar(app, "SELECT array_agg(tenant_id) FROM platform.invites"));
            await tx.CommitAsync();
        }
        Assert.Equal(0L, await Scalar(app, "SELECT count(tenant_id) FROM platform.invites"));
    }

    [Fact]
    public async Task Constraints_enforce_digest_length_global_uniqueness_role_expiry_and_terminal_state()
    {
        await Insert(tenant, new byte[32]);
        Assert.Equal(PostgresErrorCodes.UniqueViolation,
            (await Assert.ThrowsAsync<PostgresException>(() => Insert(otherTenant, new byte[32]))).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation,
            (await Assert.ThrowsAsync<PostgresException>(() => Insert(tenant, new byte[31]))).SqlState);
        foreach (var assignment in new[] { "role='owner'", "expires_at=created_at", "accepted_at=now(),revoked_at=now()" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => Scalar(owner, "UPDATE platform.invites SET " + assignment));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
    }

    [Fact]
    public async Task Rollback_guards_invites_and_audit_independently_and_preserves_prior_actions()
    {
        Assert.Equal(2L, await Scalar(owner, "SELECT count(*) FROM audit.entries WHERE action='tenant.created'"));
        Assert.Equal(1L, await Scalar(owner, "SELECT count(*) FROM audit.entries WHERE action='membership.updated'"));
        await Insert(tenant, new byte[32]);
        var migrator = context.GetService<IMigrator>();
        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(PreviousMigration));
        Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        Assert.Equal(1L, await Scalar(owner, "SELECT count(*) FROM platform.invites"));
        await Scalar(owner, "DELETE FROM platform.invites");
        foreach (var action in new[] { "invite.created", "invite.accepted", "invite.revoked" })
            await Audit(action);
        error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(PreviousMigration));
        Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        Assert.Equal(6L, await Scalar(owner, "SELECT count(*) FROM audit.entries"));
        await Scalar(owner, "DELETE FROM audit.entries WHERE action LIKE 'invite.%'");
        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(3L, await Scalar(owner, "SELECT count(*) FROM audit.entries"));
        Assert.Equal(PostgresErrorCodes.CheckViolation,
            (await Assert.ThrowsAsync<PostgresException>(() => Audit("invite.created"))).SqlState);
    }
}
