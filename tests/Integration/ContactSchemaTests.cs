using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Crm;
using Xunit;

namespace Integration;

public sealed class ContactSchemaTests : IAsyncLifetime
{
    private const string Previous = "20261126000000_Invitations";
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
        await using var cmd = new NpgsqlCommand(sql, connection);
        foreach (var value in values) cmd.Parameters.Add(new NpgsqlParameter { Value = value });
        return await cmd.ExecuteScalarAsync();
    }

    [Fact]
    public async Task Schema_enforces_tenant_keys_default_version_nullable_duplicate_email_and_rls()
    {
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        Assert.Equal(true, await Sql(owner, "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='crm.contacts'::regclass"));
        Assert.Equal(true, await Sql(owner, "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='outbox.events'::regclass"));
        Assert.Equal(true, await Sql(app, "SELECT NOT rolbypassrls AND NOT rolsuper FROM pg_roles WHERE rolname=current_user"));
        Assert.Equal(false, await Sql(app, "SELECT has_schema_privilege(current_user,'crm','CREATE') OR has_schema_privilege(current_user,'outbox','CREATE')"));
        Assert.Equal(false, await Sql(owner, "SELECT has_table_privilege($1,'audit.entries','SELECT,INSERT,UPDATE,DELETE')", db.ApplicationRole));
        var same = Guid.NewGuid();
        foreach (var t in new[] { tenant, other })
        {
            await using var tx = await app.BeginTransactionAsync();
            await Sql(app, "SELECT set_config('crm.tenant_id',$1,true)", t.ToString());
            Assert.Equal(1L, await Sql(app, "INSERT INTO crm.contacts(tenant_id,id,display_name,email) VALUES($1,$2,'Name','same@example.test') RETURNING version", t, same));
            await Sql(app, "INSERT INTO crm.contacts(tenant_id,id,display_name,email) VALUES($1,gen_random_uuid(),'Name',NULL)", t);
            Assert.Equal(2L, await Sql(app, "SELECT count(*) FROM crm.contacts"));
            await tx.CommitAsync();
        }
        Assert.Equal(0L, await Sql(app, "SELECT count(*) FROM crm.contacts"));
        await using (var tx = await app.BeginTransactionAsync())
        {
            await Sql(app, "SELECT set_config('crm.tenant_id',$1,true)", tenant.ToString());
            Assert.Equal(2L, await Sql(app, "SELECT count(*) FROM crm.contacts"));
            await Sql(app, "UPDATE crm.contacts SET deleted_at=now() WHERE id=$1", same);
            await tx.CommitAsync();
        }
        await app.CloseAsync();
        await app.OpenAsync();
        Assert.Equal(0L, await Sql(app, "SELECT count(*) FROM crm.contacts"));
        Assert.Equal(true, await Sql(owner, "SELECT EXISTS (SELECT FROM pg_indexes WHERE schemaname='crm' AND tablename='contacts' AND indexdef LIKE '%updated_at%')"));
    }

    [Fact]
    public async Task Rollback_is_allowed_when_empty_and_rejected_when_contact_data_exists()
    {
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        Assert.Equal(DBNull.Value, await Sql(owner, "SELECT to_regclass('crm.contacts')::text"));
        await migrator.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(owner, db.ApplicationRole, default);
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        await using (var tx = await app.BeginTransactionAsync())
        {
            await Sql(app, "SELECT set_config('crm.tenant_id',$1,true)", tenant.ToString());
            await Sql(app, "INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES($1,gen_random_uuid(),'Name')", tenant);
            await tx.CommitAsync();
        }
        var ex = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Previous));
        Assert.Equal(PostgresErrorCodes.RaiseException, ex.SqlState);
        Assert.Contains("forward fix", ex.MessageText);
    }
    private static Task<Guid> Append(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid contactId) =>
        ContactMutationJournal.AppendAsync(connection, transaction, tenantId, contactId,
            "issuer", "subject", "contact.created", "correlation", 1L, CancellationToken.None);

    [Fact]
    public async Task Journal_commits_a_stable_event_and_rolls_back_with_contact_and_audit()
    {
        await Sql(owner, "INSERT INTO platform.staff_bindings VALUES('issuer','subject','active','verified')");
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        var contact = Guid.NewGuid();
        Guid eventId;
        await using (var tx = await app.BeginTransactionAsync())
        {
            await Sql(app, "SELECT set_config('crm.tenant_id',$1,true)", tenant.ToString());
            await Sql(app, "INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES($1,$2,'Name')", tenant, contact);
            await Append(app, tx, tenant, contact);
            await tx.RollbackAsync();
        }
        Assert.Equal(0L, await Sql(owner, "SELECT count(*) FROM audit.entries WHERE action='contact.created'"));
        await using (var tx = await app.BeginTransactionAsync())
        {
            await Sql(app, "SELECT set_config('crm.tenant_id',$1,true)", tenant.ToString());
            Assert.Equal(0L, await Sql(app, "SELECT count(*) FROM crm.contacts"));
            await Sql(app, "INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES($1,$2,'Name')", tenant, contact);
            eventId = await Append(app, tx, tenant, contact);
            await tx.CommitAsync();
        }
        Assert.Equal(1L, await Sql(owner, "SELECT count(*) FROM audit.entries WHERE action='contact.created'"));
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await Sql(owner, "SELECT set_config('crm.tenant_id',$1,true)", tenant.ToString());
            Assert.Equal(true, await Sql(owner, """
                SELECT id=$1 AND payload->>'eventId'=id::text AND payload->>'tenantId'=tenant_id::text
                   AND payload->>'contactId'=contact_id::text AND payload->>'eventType'=type
                   AND payload->>'occurredAt' IS NOT NULL AND (payload->'payload'->>'version')::bigint=1
                   AND (SELECT count(*) FROM jsonb_object_keys(payload->'payload'))=1
                FROM outbox.events
                """, eventId));
            await tx.CommitAsync();
        }
    }

    [Theory]
    [InlineData("audit.entries")]
    [InlineData("outbox.events")]
    public async Task Append_failure_aborts_contact_audit_and_outbox(string target)
    {
        await Sql(owner, "INSERT INTO platform.staff_bindings VALUES('issuer','subject','active','verified')");
        await Sql(owner, "CREATE FUNCTION platform.test_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected'; END $$");
        await Sql(owner, $"CREATE TRIGGER test_failure BEFORE INSERT ON {target} FOR EACH ROW EXECUTE FUNCTION platform.test_failure()");
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        await using (var tx = await app.BeginTransactionAsync())
        {
            await Sql(app, "SELECT set_config('crm.tenant_id',$1,true)", tenant.ToString());
            var contact = Guid.NewGuid();
            await Sql(app, "INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES($1,$2,'Name')", tenant, contact);
            await Assert.ThrowsAsync<PostgresException>(() => Append(app, tx, tenant, contact));
            await tx.RollbackAsync();
        }
        Assert.Equal(0L, await Sql(owner, "SELECT count(*) FROM audit.entries WHERE action='contact.created'"));
        await using var check = await owner.BeginTransactionAsync();
        await Sql(owner, "SELECT set_config('crm.tenant_id',$1,true)", tenant.ToString());
        Assert.Equal(0L, await Sql(owner, "SELECT count(*) FROM crm.contacts"));
        Assert.Equal(0L, await Sql(owner, "SELECT count(*) FROM outbox.events"));
        await check.CommitAsync();
    }

    [Fact]
    public async Task Grant_validation_rejects_switchable_role_ownership_and_outbox_read()
    {
        await using var admin = new NpgsqlConnection(db.AdminConnectionString);
        await admin.OpenAsync();
        await Sql(admin, $"CREATE ROLE contact_reader NOLOGIN; GRANT contact_reader TO {PostgresFixture.Quote(db.ApplicationRole)} WITH INHERIT FALSE, SET TRUE");
        await Sql(owner, "GRANT USAGE ON SCHEMA outbox TO contact_reader; GRANT SELECT ON outbox.events TO contact_reader");
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGrants.ValidateAsync(owner, db.ApplicationRole, default));
        await Sql(owner, "REVOKE SELECT ON outbox.events FROM contact_reader; GRANT CREATE ON SCHEMA crm TO contact_reader");
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGrants.ValidateAsync(owner, db.ApplicationRole, default));
    }

    [Fact]
    public async Task Tenant_context_prevents_cross_tenant_insert_and_journal_forgery()
    {
        await Sql(owner, "INSERT INTO platform.staff_bindings VALUES('issuer','subject','active','verified')");
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        await using (var tx = await app.BeginTransactionAsync())
        {
            await Sql(app, "SELECT set_config('crm.tenant_id',$1,true)", tenant.ToString());
            var error = await Assert.ThrowsAsync<PostgresException>(() => Sql(app,
                "INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES($1,gen_random_uuid(),'Name')", other));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            await tx.RollbackAsync();
        }
        await using (var tx = await app.BeginTransactionAsync())
        {
            await Sql(app, "SELECT set_config('crm.tenant_id',$1,true)", tenant.ToString());
            var error = await Assert.ThrowsAsync<PostgresException>(() => Append(app, tx, other, Guid.NewGuid()));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            await tx.RollbackAsync();
        }
    }

    [Theory]
    [InlineData("crm.contacts")]
    [InlineData("outbox.events")]
    public async Task Grant_validation_rejects_disabled_or_unforced_tenant_rls(string table)
    {
        await Sql(owner, $"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY");
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGrants.ValidateAsync(owner, db.ApplicationRole, default));
    }

    [Fact]
    public async Task Premigration_validation_allows_previous_schema_without_weakening_postmigration_checks()
    {
        await context.GetService<IMigrator>().MigrateAsync(Previous);
        await DatabaseRoleGrants.ValidateAsync(owner, db.ApplicationRole, default);
    }

}
