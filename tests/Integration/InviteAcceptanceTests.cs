using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Platform.Api.Identity;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed class InviteAcceptanceTests : IAsyncLifetime
{
    private readonly PostgresFixture db = new();
    private readonly Guid tenant = Guid.NewGuid();
    private readonly (string Token, byte[] Hash) invite = InviteToken.Create();
    private readonly OperationalSecurityTests.Capture logs = new();
    private FakeOidcIssuer issuer = null!;
    private ApiFactory app = null!;
    private HttpClient client = null!;
    private static FakeTokenOptions Verified => new() { Subject = "recipient", PayloadType = "Bearer", IncludeVerifiedEmail = true };

    public async Task InitializeAsync()
    {
        await db.InitializeAsync();
        issuer = await FakeOidcIssuer.StartAsync();
        await using var c = new NpgsqlConnection(db.MigrationConnectionString);
        await c.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(c,
            p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(c, db.ApplicationRole, default);
        await Sql("""
            INSERT INTO platform.tenants VALUES (@t,'Fixture','active',now());
            INSERT INTO platform.staff_bindings VALUES (@iss,'recipient','active','ticket'),(@iss,'owner','active','ticket');
            INSERT INTO platform.memberships VALUES (@t,gen_random_uuid(),@iss,'owner','owner','active',1);
            INSERT INTO platform.invites(tenant_id,id,token_hash,email,normalized_email,role,created_at,expires_at)
            VALUES (@t,gen_random_uuid(),@hash,'Alice@Example.COM','Alice@example.com','operator',now(),now()+interval '24 hours')
            """);
        app = new ApiFactory(new Dictionary<string,string?> {
            ["ConnectionStrings:Platform"] = db.ApplicationConnectionString, ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri,
            ["Oidc:Audience"] = "crm-api", ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer,".well-known/openid-configuration").AbsoluteUri,
            ["Oidc:AllowHttpLoopback"] = "true" });
        client = app.CreateClient();
        app.Services.GetRequiredService<ILoggerFactory>().AddProvider(logs);
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
        await issuer.DisposeAsync();
        await db.DisposeAsync();
    }

    private async Task<object?> Sql(string sql)
    {
        await using var c = new NpgsqlConnection(db.MigrationConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql,c);
        cmd.Parameters.AddWithValue("t",tenant);
        cmd.Parameters.AddWithValue("iss",issuer.Issuer.AbsoluteUri);
        cmd.Parameters.AddWithValue("hash",invite.Hash);
        return await cmd.ExecuteScalarAsync();
    }

    private async Task<HttpResponseMessage> Accept(FakeTokenOptions? identity, string? body = null, bool tenantHeader = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,"/api/v1/invites/accept");
        if (identity != null) req.Headers.Authorization = new("Bearer",issuer.IssueToken(identity));
        if (tenantHeader) req.Headers.Add("X-Tenant-Id",tenant.ToString());
        req.Content = body is null ? JsonContent.Create(new { token = invite.Token }) : new StringContent(body,Encoding.UTF8,"application/json");
        return await client.SendAsync(req);
    }

    private async Task Problem(HttpResponseMessage response, HttpStatusCode code, string reason)
    {
        using (response)
        {
            Assert.Equal(code,response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Redacted(text);
            Assert.DoesNotContain(tenant.ToString(),text);
            using var json = JsonDocument.Parse(text);
            Assert.Equal(reason,json.RootElement.GetProperty("reasonToken").GetString());
            Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(),json.RootElement.GetProperty("correlationId").GetString());
        }
    }

    private void Redacted(string text)
    {
        foreach (var value in new[] { invite.Token, Convert.ToHexString(invite.Hash), Convert.ToBase64String(invite.Hash), "Alice@example.com" })
            Assert.False(text.Contains(value,StringComparison.OrdinalIgnoreCase));
    }

    private async Task State(int accepted)
    {
        var counts = await Sql("""
            SELECT ARRAY[(SELECT count(*) FROM platform.invites WHERE accepted_at IS NOT NULL),
                         (SELECT count(*) FROM platform.memberships WHERE subject='recipient'),
                         (SELECT count(*) FROM audit.entries WHERE action='invite.accepted')]
            """);
        Assert.Equal(new long[] { accepted, accepted, accepted }, (long[])counts!);
    }

    [Fact]
    public async Task Concurrent_requests_have_one_winner_and_one_audit()
    {
        var results = await Task.WhenAll(Enumerable.Range(0,10).Select(_ => Accept(Verified)));
        using var winner = Assert.Single(results,r => r.StatusCode == HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await winner.Content.ReadAsStringAsync());
        var member = json.RootElement;
        Assert.True(member.EnumerateObject().Select(p=>p.Name).ToHashSet().SetEquals(["id","issuer","subject","role","status","version"]));
        Assert.Equal("recipient",member.GetProperty("subject").GetString());
        Assert.Equal(issuer.Issuer.AbsoluteUri,member.GetProperty("issuer").GetString());
        Assert.Equal("operator",member.GetProperty("role").GetString());
        Assert.Equal("active",member.GetProperty("status").GetString());
        Assert.Equal(1,member.GetProperty("version").GetInt64());
        Assert.Equal(await Sql("SELECT id FROM platform.memberships WHERE tenant_id=@t AND subject='recipient'"),member.GetProperty("id").GetGuid());
        foreach (var other in results.Where(r=>r!=winner)) await Problem(other,HttpStatusCode.Conflict,"INVITE_STATE_CONFLICT");
        await State(1);
        Redacted((string)(await Sql("SELECT to_jsonb(e)::text FROM audit.entries e WHERE action='invite.accepted'"))!);
        Redacted(logs.Text);
    }

    [Fact]
    public async Task Rejects_invalid_request_identity_and_token()
    {
        using var anonymous = await Accept(null);
        Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        foreach (var body in new[] { "{", "{}", """{"token":12}""", """{"token":"invalid"}""",
            JsonSerializer.Serialize(new { token=invite.Token, tenant_id=tenant }), $$"""{"token":"{{invite.Token}}","token":"{{invite.Token}}"}""" })
            await Problem(await Accept(Verified,body),HttpStatusCode.BadRequest,"REQUEST_INVALID");
        await Problem(await Accept(Verified,tenantHeader:true),HttpStatusCode.BadRequest,"REQUEST_INVALID");
        await Problem(await Accept(Verified,JsonSerializer.Serialize(new { token=InviteToken.Create().Token })),HttpStatusCode.NotFound,"INVITE_NOT_FOUND");
        foreach (var identity in new[] { Verified with { IncludeVerifiedEmail=false }, Verified with { Subject="unbound" },
            Verified with { DuplicateEmail=true }, Verified with { DuplicateVerification=true },
            Verified with { PayloadType="ID" }, Verified with { Email="alice@example.com" }, Verified with { Verification=false },
            Verified with { Verification="true" }, Verified with { Verification=new[]{true,true} }, Verified with { Email=new[]{"Alice@example.com","Alice@example.com"} } })
            await Problem(await Accept(identity),HttpStatusCode.Forbidden,"INVITE_IDENTITY_INVALID");
        await Sql("UPDATE platform.staff_bindings SET status='revoked' WHERE subject='recipient'");
        await Problem(await Accept(Verified),HttpStatusCode.Forbidden,"INVITE_IDENTITY_INVALID");
        Assert.Equal(0L,await Sql("SELECT count(*) FROM platform.staff_bindings WHERE subject='unbound'"));
        await State(0);
    }

    [Fact]
    public async Task Existing_membership_is_immutable_and_does_not_consume()
    {
        foreach (var role in new[] {"owner","agent"})
        foreach (var status in new[] {"active","inactive"})
        {
            await Sql($"UPDATE platform.memberships SET role='{role}',status='{status}',version=4 WHERE subject='owner'");
            await Problem(await Accept(Verified with { Subject="owner" }),HttpStatusCode.Conflict,"MEMBERSHIP_EXISTS");
            Assert.Equal(role+":"+status+":4",await Sql("SELECT role||':'||status||':'||version FROM platform.memberships WHERE subject='owner'"));
        }
        await State(0);
    }

    [Fact]
    public async Task Suspended_tenant_and_revoked_invite_are_closed()
    {
        await Sql("UPDATE platform.tenants SET status='suspended' WHERE id=@t");
        await Problem(await Accept(Verified),HttpStatusCode.Forbidden,"INVITE_IDENTITY_INVALID");
        await Sql("UPDATE platform.tenants SET status='active' WHERE id=@t; UPDATE platform.invites SET revoked_at=clock_timestamp() WHERE tenant_id=@t");
        await Problem(await Accept(Verified),HttpStatusCode.Conflict,"INVITE_STATE_CONFLICT");
        await State(0);
    }
    private async Task WaitForBlocked(NpgsqlConnection observer, int expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            await PostgresFixture.ExecuteAsync(observer,"SELECT pg_stat_clear_snapshot()");
            await using var cmd = new NpgsqlCommand("""
                SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND usename=@role
                  AND wait_event_type='Lock'
                """, observer);
            cmd.Parameters.AddWithValue("role",db.ApplicationRole);
            if ((long)(await cmd.ExecuteScalarAsync(timeout.Token))! >= expected) return;
            await Task.Delay(25,timeout.Token);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Accept_and_revoke_follow_tenant_lock_queue(bool acceptFirst)
    {
        var id = (Guid)(await Sql("SELECT id FROM platform.invites WHERE tenant_id=@t"))!;
        async Task<HttpResponseMessage> Revoke()
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete,"/api/v1/invites/"+id);
            req.Headers.Authorization = new("Bearer",issuer.IssueToken(new() { Subject="owner" }));
            req.Headers.Add("X-Tenant-Id",tenant.ToString());
            return await client.SendAsync(req);
        }
        await using var blocker = new NpgsqlConnection(db.AdminConnectionString);
        await blocker.OpenAsync();
        await using var tx = await blocker.BeginTransactionAsync();
        await using var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('crm.membership:'||@t::uuid::text,0))",blocker,tx);
        gate.Parameters.AddWithValue("t",tenant);
        await gate.ExecuteNonQueryAsync();
        var first = acceptFirst ? Accept(Verified) : Revoke();
        await WaitForBlocked(blocker,1);
        var second = acceptFirst ? Revoke() : Accept(Verified);
        await WaitForBlocked(blocker,2);
        await tx.CommitAsync();
        using var winner = await first;
        Assert.Equal(acceptFirst ? HttpStatusCode.OK : HttpStatusCode.NoContent,winner.StatusCode);
        await Problem(await second,HttpStatusCode.Conflict,"INVITE_STATE_CONFLICT");
        await State(acceptFirst ? 1 : 0);
        Assert.Equal(acceptFirst ? 0L : 1L,await Sql("SELECT count(*) FROM audit.entries WHERE action='invite.revoked'"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Revalidates_after_staff_row_lock_wait(bool expire)
    {
        await using var blocker = new NpgsqlConnection(db.AdminConnectionString);
        await blocker.OpenAsync();
        await using var tx = await blocker.BeginTransactionAsync();
        await using var gate = new NpgsqlCommand(expire
            ? "SELECT 1 FROM platform.staff_bindings WHERE subject='recipient' FOR UPDATE"
            : "UPDATE platform.staff_bindings SET status='revoked' WHERE subject='recipient'",blocker,tx);
        await gate.ExecuteNonQueryAsync();
        if (expire)
            await Sql("UPDATE platform.invites SET created_at=statement_timestamp()-interval '24 hours'+interval '2 seconds', expires_at=statement_timestamp()+interval '2 seconds'");
        var pending = Accept(Verified);
        await WaitForBlocked(blocker,1);
        if (expire)
            await Sql("SELECT pg_sleep(greatest(0,extract(epoch FROM expires_at-clock_timestamp()))) FROM platform.invites");
        await tx.CommitAsync();
        await Problem(await pending,expire ? HttpStatusCode.Conflict : HttpStatusCode.Forbidden,
            expire ? "INVITE_STATE_CONFLICT" : "INVITE_IDENTITY_INVALID");
        await State(0);
    }

    [Fact]
    public async Task Staff_revocation_waits_for_already_verified_acceptance()
    {
        // Hold acceptance just before its audit insert, after it has locked the verified binding.
        await Sql("""
            CREATE FUNCTION audit.wait_test() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN PERFORM pg_advisory_xact_lock(1234567); RETURN NEW; END $$;
            CREATE TRIGGER wait_test BEFORE INSERT ON audit.entries FOR EACH ROW EXECUTE FUNCTION audit.wait_test();
            """);
        await using var gate = new NpgsqlConnection(db.AdminConnectionString);
        await gate.OpenAsync();
        await PostgresFixture.ExecuteAsync(gate,"SELECT pg_advisory_lock(1234567)");
        var accept = Accept(Verified);
        await WaitForBlocked(gate,1);
        await using var staff = new NpgsqlConnection(db.MigrationConnectionString);
        await staff.OpenAsync();
        var revokeStaff = PostgresFixture.ExecuteAsync(staff,"UPDATE platform.staff_bindings SET status='revoked' WHERE subject='recipient'");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            await using var blocked = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid))>0",gate);
            blocked.Parameters.AddWithValue("pid",staff.ProcessID);
            if (await blocked.ExecuteScalarAsync(timeout.Token) is true) break;
            await Task.Delay(25,timeout.Token);
        }
        await PostgresFixture.ExecuteAsync(gate,"SELECT pg_advisory_unlock(1234567)");
        using var response = await accept;
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        await revokeStaff;
        Assert.Equal("revoked",await Sql("SELECT status FROM platform.staff_bindings WHERE subject='recipient'"));
        await State(1);
    }

    [Theory]
    [InlineData("40001",false,3)]
    [InlineData("40P01",false,3)]
    [InlineData("23514",false,1)]
    [InlineData("40001",true,1)]
    [InlineData("terminate",false,1)]
    [InlineData("terminate",true,1)]
    public async Task Failure_rolls_back_and_retry_depends_on_commit_boundary(string fault,bool atCommit,long attempts)
    {
        await using var admin = new NpgsqlConnection(db.AdminConnectionString);
        await admin.OpenAsync();
        string fail = fault=="terminate" ? "PERFORM pg_terminate_backend(pg_backend_pid());" : $"RAISE EXCEPTION USING ERRCODE='{fault}';";
        var trigger = atCommit ? "CONSTRAINT TRIGGER fault AFTER INSERT ON audit.entries DEFERRABLE INITIALLY DEFERRED" : "TRIGGER fault BEFORE INSERT ON audit.entries";
        await using var setup = new NpgsqlCommand($"""
            CREATE SEQUENCE audit.attempts;
            CREATE FUNCTION audit.fail_test() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
            BEGIN
              PERFORM nextval('audit.attempts'); {fail} RETURN NEW;
            END $$;
            CREATE {trigger} FOR EACH ROW EXECUTE FUNCTION audit.fail_test();
            """,admin);
        await setup.ExecuteNonQueryAsync();
        await Problem(await Accept(Verified),HttpStatusCode.ServiceUnavailable,"IDENTITY_STORE_UNAVAILABLE");
        await using var count = new NpgsqlCommand("SELECT last_value FROM audit.attempts WHERE is_called",admin);
        Assert.Equal(attempts,await count.ExecuteScalarAsync());
        await State(0);
    }
}
