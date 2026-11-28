using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Platform.Api.Crm;
using Microsoft.Extensions.Configuration;
using System.Text;
using Xunit;

namespace Integration;

public sealed class TimelineSchemaTests : IAsyncLifetime
{
    private const string Previous = "20261127036000_ExactNumberWhitespace";
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
    public void Cursor_round_trips_ties_and_rejects_wrong_scope_key_and_malformed_tokens()
    {
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var cursor = new TimelineCursor(key);
        var contact = Guid.NewGuid();
        var stamp = DateTime.UnixEpoch.AddTicks(123450);
        foreach (var id in new[] { Guid.NewGuid(), Guid.NewGuid() })
        {
            var token = cursor.Encode(tenant, contact, stamp, id);
            Assert.True(cursor.TryDecode(token, tenant, contact, out var time, out var anchor));
            Assert.Equal((stamp, id), (time, anchor));
            Assert.False(cursor.TryDecode(token, other, contact, out _, out _));
            Assert.False(cursor.TryDecode(token, tenant, Guid.NewGuid(), out _, out _));
            Assert.False(new TimelineCursor(new byte[32]).TryDecode(token, tenant, contact, out _, out _));
            var collection = new CrmCollectionCursor(key);
            foreach (var invalid in new[] { "", "!", (token[0] == 'A' ? "B" : "A") + token[1..], collection.Encode(tenant,"tags",null,id) })
                Assert.False(cursor.TryDecode(invalid, tenant, contact, out _, out _));
        }
        Assert.False(TimelineCursor.TryCreate(new ConfigurationBuilder().Build(), out _));
    }

    [Fact]
    public async Task Timeline_has_scoped_reads_restricted_writes_and_guarded_downgrade()
    {
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await migrator.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(owner, db.ApplicationRole, default);
        var contact = Guid.NewGuid();
        var inbox = Guid.NewGuid();
        const string insert = """
            INSERT INTO crm.timeline_entries(tenant_id,id,contact_id,inbox_id,entry_kind,source,type,occurred_at,summary)
            VALUES($1,gen_random_uuid(),$2,$3,'note','crm','crm.note.created.v1',now(),$4)
            """;
        var bytes = Encoding.UTF8.GetBytes("A\0猫😀");
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(
            () => Sql(owner,insert,tenant,contact,DBNull.Value,bytes))).SqlState);
        await Sql(owner,"SELECT set_config('crm.tenant_id',$1,false)",tenant.ToString());
        await Sql(owner,"INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES($1,$2,'Contact')",tenant,contact);
        foreach (var item in new object[] { DBNull.Value, DBNull.Value, inbox })
            await Sql(owner, insert, tenant,contact,item,bytes);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, (await Assert.ThrowsAsync<PostgresException>(
            () => Sql(owner,insert,tenant,contact,inbox,bytes))).SqlState);
        await Sql(owner,"SELECT set_config('crm.tenant_id',$1,false)",other.ToString());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(
            () => Sql(owner,insert,other,contact,inbox,bytes))).SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(
            () => Sql(owner,insert,tenant,contact,DBNull.Value,bytes))).SqlState);
        Assert.Equal(true,await Sql(owner,"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='crm.timeline_entries'::regclass"));
        Assert.Equal(true,await Sql(owner,"SELECT EXISTS(SELECT FROM pg_indexes WHERE schemaname='crm' AND tablename='timeline_entries' AND indexdef LIKE '%(tenant_id, contact_id, occurred_at DESC, id DESC)%')"));
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        Assert.Equal(false,await Sql(app,"SELECT rolsuper OR rolbypassrls OR pg_has_role(current_user,c.relowner,'MEMBER') FROM pg_roles,pg_class c WHERE rolname=current_user AND c.oid='crm.timeline_entries'::regclass"));
        foreach (var scope in new[] { "",other.ToString(),tenant.ToString() })
        {
            await Sql(app,"SELECT set_config('crm.tenant_id',$1,false)",scope);
            Assert.Equal(scope == tenant.ToString() ? 3L : 0L,await Sql(app,"SELECT count(*) FROM crm.timeline_entries"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege,(await Assert.ThrowsAsync<PostgresException>(
                () => Sql(app,insert,tenant,contact,DBNull.Value,bytes))).SqlState);
        }
        Assert.Equal(bytes,Assert.IsType<byte[]>(await Sql(app,"SELECT summary FROM crm.timeline_entries LIMIT 1")));
        var blocked = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Previous));
        Assert.Contains("forward fix",blocked.MessageText);
    }
}
