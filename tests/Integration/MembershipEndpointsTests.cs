using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace Integration;

public sealed class MembershipEndpointsTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly PostgresFixture db = new();
    private FakeOidcIssuer issuer = null!;
    private ApiFactory factory = null!;
    private HttpClient client = null!;
    private readonly Guid tenant = Guid.NewGuid(), otherTenant = Guid.NewGuid(), owner = Guid.NewGuid(), agent = Guid.NewGuid(), other = Guid.NewGuid();
    public async Task InitializeAsync()
    {
        await db.InitializeAsync(); issuer = await FakeOidcIssuer.StartAsync();
        await using var c = new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(c, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync(); await DatabaseRoleGrants.ApplyAsync(c, db.ApplicationRole, CancellationToken.None);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO platform.tenants VALUES (@tenant,'A','active',now()),(@otherTenant,'B','active',now());
            INSERT INTO platform.memberships VALUES (@tenant,@owner,@issuer,'owner','owner','active',1),(@tenant,@agent,@issuer,'agent','agent','inactive',1),(@otherTenant,@other,@issuer,'other','owner','active',1);
            INSERT INTO platform.staff_bindings VALUES (@issuer,'owner','active','ticket'),(@issuer,'agent','active','ticket');
            """, c);
        cmd.Parameters.AddWithValue("tenant",tenant); cmd.Parameters.AddWithValue("otherTenant",otherTenant); cmd.Parameters.AddWithValue("owner",owner); cmd.Parameters.AddWithValue("agent",agent); cmd.Parameters.AddWithValue("other",other); cmd.Parameters.AddWithValue("issuer",issuer.Issuer.AbsoluteUri); await cmd.ExecuteNonQueryAsync();
        factory = new ApiFactory(new Dictionary<string,string?> { ["ConnectionStrings:Platform"] = db.ApplicationConnectionString, ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri, ["Oidc:Audience"] = "crm-api", ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer,".well-known/openid-configuration").AbsoluteUri, ["Oidc:AllowHttpLoopback"] = "true" });
        client = factory.CreateClient();
    }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); await issuer.DisposeAsync(); await db.DisposeAsync(); }
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string subject = "owner", string? body = null, string? match = null, Guid? selected = null)
    {
        using var req = new HttpRequestMessage(method,path); req.Headers.Authorization = new AuthenticationHeaderValue("Bearer",issuer.IssueToken(new() { Subject=subject, Role="owner" })); req.Headers.Add("X-Tenant-Id",(selected ?? tenant).ToString());
        if (body is not null) req.Content = new StringContent(body,Encoding.UTF8,"application/json");
        if (match is not null) req.Headers.TryAddWithoutValidation("If-Match",match);
        return await client.SendAsync(req);
    }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode code, string reason)
    {
        Assert.Equal(code,response.StatusCode); Assert.Equal("application/problem+json",response.Content.Headers.ContentType!.MediaType);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal(reason,json.RootElement.GetProperty("reasonToken").GetString()); Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(),json.RootElement.GetProperty("correlationId").GetString());
    }
    [Theory]
    [InlineData("tenant-list")]
    [InlineData("membership-list")]
    [InlineData("membership-get")]
    [InlineData("membership-patch")]
    public async Task Same_audience_id_token_cannot_authorize_identity_routes(string route)
    {
        var path = route switch {
            "tenant-list" => "/api/v1/tenants",
            "membership-list" => "/api/v1/memberships",
            _ => $"/api/v1/memberships/{agent}"
        };
        using var request = new HttpRequestMessage(route == "membership-patch" ? HttpMethod.Patch : HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.IssueToken(new() { Subject = "owner", PayloadType = "ID" }));
        request.Headers.Add("X-Tenant-Id", tenant.ToString());
        if (route == "membership-patch") { request.Headers.TryAddWithoutValidation("If-Match", "\"v1\""); request.Content = new StringContent("{\"status\":\"active\"}", Encoding.UTF8, "application/json"); }
        using var response = await client.SendAsync(request);
        await Problem(response, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
    }
    [Fact]
    public async Task Owner_reads_tenant_membership_with_revoked_mapping_and_etag()
    {
        using var response=await Send(HttpMethod.Get,$"/api/v1/memberships/{agent}"); Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Equal("\"v1\"",response.Headers.ETag!.ToString());
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal(agent,json.RootElement.GetProperty("id").GetGuid()); Assert.Equal("revoked",json.RootElement.GetProperty("status").GetString());
    }
    [Fact]
    public async Task Patch_changes_role_and_status_with_version_and_audit()
    {
        using var response=await Send(HttpMethod.Patch,$"/api/v1/memberships/{agent}",body:"{\"role\":\"admin\",\"status\":\"active\"}",match:"\"v1\"");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Equal("\"v2\"",response.Headers.ETag!.ToString());
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal("admin",json.RootElement.GetProperty("role").GetString()); Assert.Equal("active",json.RootElement.GetProperty("status").GetString());
        await using var c=new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync(); await using var cmd=new NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE tenant_id=@tenant AND target_id=@id",c); cmd.Parameters.AddWithValue("tenant",tenant); cmd.Parameters.AddWithValue("id",agent); Assert.Equal(1L,await cmd.ExecuteScalarAsync());
    }

    private async Task Execute(string sql)
    {
        await using var c=new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync(); await using var cmd=new NpgsqlCommand(sql,c); await cmd.ExecuteNonQueryAsync();
    }
    private async Task<long> Scalar(string sql)
    {
        await using var c=new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync(); await using var cmd=new NpgsqlCommand(sql,c); return (long)(await cmd.ExecuteScalarAsync())!;
    }
    [Fact]
    public async Task List_keyset_and_cross_tenant_access_fail_closed()
    {
        using var first=await Send(HttpMethod.Get,"/api/v1/memberships?limit=1"); Assert.Equal(HttpStatusCode.OK,first.StatusCode);
        using var page=JsonDocument.Parse(await first.Content.ReadAsStringAsync()); var firstId=Assert.Single(page.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid(); var cursor=page.RootElement.GetProperty("nextCursor").GetString(); Assert.NotNull(cursor);
        using var second=await Send(HttpMethod.Get,"/api/v1/memberships?limit=1&cursor="+cursor); Assert.Equal(HttpStatusCode.OK,second.StatusCode);
        using var page2=JsonDocument.Parse(await second.Content.ReadAsStringAsync()); Assert.NotEqual(firstId,Assert.Single(page2.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid()); Assert.Equal(JsonValueKind.Null,page2.RootElement.GetProperty("nextCursor").ValueKind);
        using var invalid=await Send(HttpMethod.Get,"/api/v1/memberships?cursor=bad"); await Problem(invalid,HttpStatusCode.BadRequest,"CURSOR_INVALID");
        using var cross=await Send(HttpMethod.Get,$"/api/v1/memberships/{other}"); await Problem(cross,HttpStatusCode.NotFound,"NOT_FOUND");
        using var badLimit=await Send(HttpMethod.Get,"/api/v1/memberships?limit=0"); await Problem(badLimit,HttpStatusCode.BadRequest,"REQUEST_INVALID");
        using var duplicate=await Send(HttpMethod.Get,"/api/v1/memberships?limit=1&limit=2"); await Problem(duplicate,HttpStatusCode.BadRequest,"REQUEST_INVALID");
        await Execute($"UPDATE platform.memberships SET status='active' WHERE tenant_id='{tenant}' AND id='{agent}'");
        using var denied=await Send(HttpMethod.Get,"/api/v1/memberships",subject:"agent"); await Problem(denied,HttpStatusCode.Forbidden,"INSUFFICIENT_SCOPE");
    }
    [Fact]
    public async Task Preconditions_bad_body_owner_rules_and_noop_are_fail_closed()
    {
        var path=$"/api/v1/memberships/{agent}";
        using (var response=await Send(HttpMethod.Patch,path,body:"{\"role\":\"admin\"}")) await Problem(response,(HttpStatusCode)428,"PRECONDITION_REQUIRED");
        foreach (var match in new[]{"*","W/\"v1\"","\"v1\",\"v2\"","\"v0\"","\"v9223372036854775808\""})
        { using var response=await Send(HttpMethod.Patch,path,body:"{\"role\":\"admin\"}",match:match); await Problem(response,HttpStatusCode.BadRequest,"PRECONDITION_INVALID"); }
        using (var response=await Send(HttpMethod.Patch,path,body:"{\"role\":\"admin\"}",match:"\"v2\"")) await Problem(response,HttpStatusCode.PreconditionFailed,"VERSION_CONFLICT");
        foreach (var body in new[]{"{}","{","[]","{\"role\":null}","{\"unknown\":\"x\"}","{\"status\":\"inactive\"}","{\"role\":\"admin\",\"role\":\"agent\"}","{\"\\uD800\":\"x\"}","{\"role\":\"\\uD800\"}"})
        { using var response=await Send(HttpMethod.Patch,path,body:body,match:"\"v1\""); await Problem(response,HttpStatusCode.BadRequest,"REQUEST_INVALID"); }
        using (var response=await Send(HttpMethod.Patch,path,body:"{\"role\":\"owner\"}",match:"\"v1\"")) await Problem(response,HttpStatusCode.Conflict,"OWNER_INVARIANT");
        using (var response=await Send(HttpMethod.Patch,$"/api/v1/memberships/{owner}",body:"{\"status\":\"revoked\"}",match:"\"v1\"")) await Problem(response,HttpStatusCode.Conflict,"OWNER_INVARIANT");
        using (var response=await Send(HttpMethod.Patch,path,body:"{\"status\":\"revoked\"}",match:"\"v1\"")) { Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Equal("\"v1\"",response.Headers.ETag!.ToString()); }
        Assert.Equal(0L,await Scalar("SELECT count(*) FROM audit.entries"));
        using (var response=await Send(HttpMethod.Patch,$"/api/v1/memberships/{other}",body:"{\"role\":\"admin\"}",match:"\"v1\"")) await Problem(response,HttpStatusCode.NotFound,"NOT_FOUND");
    }
    [Fact]
    public async Task Concurrent_same_version_one_wins_and_audits_once()
    {
        var path=$"/api/v1/memberships/{agent}";
        var calls=await Task.WhenAll(Send(HttpMethod.Patch,path,body:"{\"role\":\"admin\"}",match:"\"v1\""),Send(HttpMethod.Patch,path,body:"{\"status\":\"active\"}",match:"\"v1\""));
        try { Assert.Equal(new[]{HttpStatusCode.OK,HttpStatusCode.PreconditionFailed},calls.Select(x=>x.StatusCode).Order().ToArray()); }
        finally { foreach(var r in calls) r.Dispose(); }
        Assert.Equal(1L,await Scalar("SELECT count(*) FROM audit.entries"));
    }
    [Fact]
    public async Task Audit_failure_rolls_back_patch()
    {
        await Execute("CREATE FUNCTION audit.reject_membership_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'forced audit failure'; END $$; CREATE TRIGGER reject_membership_test BEFORE INSERT ON audit.entries FOR EACH ROW EXECUTE FUNCTION audit.reject_membership_test()");
        using var response=await Send(HttpMethod.Patch,$"/api/v1/memberships/{agent}",body:"{\"status\":\"active\"}",match:"\"v1\""); await Problem(response,HttpStatusCode.ServiceUnavailable,"IDENTITY_STORE_UNAVAILABLE");
        Assert.Equal(1L,await Scalar($"SELECT version FROM platform.memberships WHERE id='{agent}'")); Assert.Equal(0L,await Scalar("SELECT count(*) FROM audit.entries"));
    }
    [Fact]
    public async Task Self_revocation_requires_another_owner_and_then_denies_requests()
    {
        var secondOwner=Guid.NewGuid(); await Execute($"INSERT INTO platform.memberships VALUES ('{tenant}','{secondOwner}','{issuer.Issuer.AbsoluteUri}','otherowner','owner','active',1)");
        using (var response=await Send(HttpMethod.Patch,$"/api/v1/memberships/{owner}",body:"{\"status\":\"revoked\"}",match:"\"v1\"")) Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var denied=await Send(HttpMethod.Get,"/api/v1/memberships"); await Problem(denied,HttpStatusCode.Forbidden,"TENANT_ACCESS_DENIED");
    }

    [Fact]
    public async Task Concurrent_cross_revocations_keep_one_active_owner()
    {
        var second=Guid.NewGuid(); await Execute($"INSERT INTO platform.memberships VALUES ('{tenant}','{second}','{issuer.Issuer.AbsoluteUri}','owner2','owner','active',1); INSERT INTO platform.staff_bindings VALUES ('{issuer.Issuer.AbsoluteUri}','owner2','active','ticket')");
        var responses=await Task.WhenAll(Send(HttpMethod.Patch,$"/api/v1/memberships/{second}",body:"{\"status\":\"revoked\"}",match:"\"v1\""),Send(HttpMethod.Patch,$"/api/v1/memberships/{owner}",subject:"owner2",body:"{\"status\":\"revoked\"}",match:"\"v1\""));
        try { Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK); Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Forbidden || x.StatusCode==HttpStatusCode.Conflict); }
        finally { foreach(var r in responses) r.Dispose(); }
        Assert.Equal(1L,await Scalar($"SELECT count(*) FROM platform.memberships WHERE tenant_id='{tenant}' AND role='owner' AND status='active'"));
        Assert.Equal(1L,await Scalar("SELECT count(*) FROM audit.entries"));
    }
    [Theory]
    [InlineData(2,HttpStatusCode.OK,3,"40P01")]
    [InlineData(3,HttpStatusCode.ServiceUnavailable,3,"40P01")]
    [InlineData(1,HttpStatusCode.OK,2,"40001")]
    public async Task Deadlock_retries_reauthorize_and_rollback_whole_attempt(int failures,HttpStatusCode expected,int calls,string sqlState)
    {
        await Execute($"CREATE SEQUENCE audit.retry_test; CREATE FUNCTION audit.fail_retry_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF nextval('audit.retry_test') <= {failures} THEN RAISE EXCEPTION USING ERRCODE='{sqlState}', MESSAGE='forced retry'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_retry_test BEFORE UPDATE ON platform.memberships FOR EACH ROW EXECUTE FUNCTION audit.fail_retry_test()");
        using var response=await Send(HttpMethod.Patch,$"/api/v1/memberships/{agent}",body:"{{\"status\":\"active\"}}".Replace("{{","{").Replace("}}","}"),match:"\"v1\"");
        Assert.Equal(expected,response.StatusCode); Assert.Equal((long)calls,await Scalar("SELECT last_value FROM audit.retry_test"));
        Assert.Equal(expected==HttpStatusCode.OK ? 1L : 0L,await Scalar("SELECT count(*) FROM audit.entries"));
        Assert.Equal(expected==HttpStatusCode.OK ? 2L : 1L,await Scalar($"SELECT version FROM platform.memberships WHERE id='{agent}'"));
    }
    [Fact]
    public async Task Legacy_subject_outside_contract_is_not_truncated_or_mutated()
    {
        await Execute($"UPDATE platform.memberships SET subject=repeat('s',257) WHERE id='{agent}'");
        using (var response=await Send(HttpMethod.Get,$"/api/v1/memberships/{agent}")) await Problem(response,HttpStatusCode.ServiceUnavailable,"IDENTITY_STORE_UNAVAILABLE");
        using (var response=await Send(HttpMethod.Patch,$"/api/v1/memberships/{agent}",body:"{\"status\":\"active\"}",match:"\"v1\"")) await Problem(response,HttpStatusCode.ServiceUnavailable,"IDENTITY_STORE_UNAVAILABLE");
        Assert.Equal(1L,await Scalar($"SELECT version FROM platform.memberships WHERE id='{agent}'"));
    }

    [Fact]
    public async Task Contract_responses_and_etags_are_captured_from_real_http()
    {
        await Execute($"UPDATE platform.memberships SET status='active' WHERE id='{agent}'");
        async Task Capture(HttpResponseMessage response, int expected, string kind)
        {
            using(response) { Assert.Equal(expected,(int)response.StatusCode); var body=await response.Content.ReadAsStringAsync(); output.WriteLine("MEMBERSHIP_CONTRACT:"+JsonSerializer.Serialize(new { status=expected, kind, body=JsonDocument.Parse(body).RootElement, etag=response.Headers.ETag?.ToString() })); }
        }
        await Capture(await Send(HttpMethod.Get,$"/api/v1/memberships/{agent}"),200,"membership");
        await Capture(await Send(HttpMethod.Get,"/api/v1/memberships"),200,"page");
        await Capture(await Send(HttpMethod.Patch,$"/api/v1/memberships/{agent}",body:"{}",match:"\"v1\""),400,"problem");
        await Capture(await Send(HttpMethod.Get,"/api/v1/memberships",subject:"agent"),403,"problem");
        await Capture(await Send(HttpMethod.Get,$"/api/v1/memberships/{other}"),404,"problem");
        await Capture(await Send(HttpMethod.Patch,$"/api/v1/memberships/{owner}",body:"{\"status\":\"revoked\"}",match:"\"v1\""),409,"problem");
        await Capture(await Send(HttpMethod.Patch,$"/api/v1/memberships/{agent}",body:"{\"status\":\"active\"}",match:"\"v2\""),412,"problem");
        await Capture(await Send(HttpMethod.Patch,$"/api/v1/memberships/{agent}",body:"{\"status\":\"active\"}"),428,"problem");
        await Capture(await Send(HttpMethod.Patch,$"/api/v1/memberships/{agent}",body:"{\"role\":\"admin\"}",match:"\"v1\""),200,"membership");
    }

    [Fact]
    public async Task Header_validation_and_cursor_tenant_binding()
    {
        using var missing=new HttpRequestMessage(HttpMethod.Get,"/api/v1/memberships"); missing.Headers.Authorization=new AuthenticationHeaderValue("Bearer",issuer.IssueToken(new(){Subject="owner"}));
        using (var response=await client.SendAsync(missing)) await Problem(response,HttpStatusCode.BadRequest,"TENANT_CONTEXT_INVALID");
        using var duplicate=new HttpRequestMessage(HttpMethod.Get,"/api/v1/memberships"); duplicate.Headers.Authorization=new AuthenticationHeaderValue("Bearer",issuer.IssueToken(new(){Subject="owner"})); duplicate.Headers.TryAddWithoutValidation("X-Tenant-Id",new[]{tenant.ToString(),otherTenant.ToString()});
        using (var response=await client.SendAsync(duplicate)) await Problem(response,HttpStatusCode.BadRequest,"TENANT_CONTEXT_INVALID");
        var cursor=Platform.Api.Identity.MembershipCursor.Encode(otherTenant,other);
        using (var response=await Send(HttpMethod.Get,"/api/v1/memberships?cursor="+cursor)) await Problem(response,HttpStatusCode.BadRequest,"CURSOR_INVALID");
        using var request=new HttpRequestMessage(HttpMethod.Patch,$"/api/v1/memberships/{agent}"); request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",issuer.IssueToken(new(){Subject="owner"})); request.Headers.Add("X-Tenant-Id",tenant.ToString()); request.Headers.TryAddWithoutValidation("If-Match",new[]{"\"v1\"","\"v2\""}); request.Content=new StringContent("{\"role\":\"admin\"}",Encoding.UTF8,"application/json");
        using var bad=await client.SendAsync(request); await Problem(bad,HttpStatusCode.BadRequest,"PRECONDITION_INVALID");
    }
    [Fact]
    public async Task Admin_can_read_and_update_but_cannot_change_owner_role()
    {
        await Execute($"UPDATE platform.memberships SET status='active',role='admin' WHERE id='{agent}'");
        using (var response=await Send(HttpMethod.Get,"/api/v1/memberships",subject:"agent")) Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using (var response=await Send(HttpMethod.Patch,$"/api/v1/memberships/{owner}",subject:"agent",body:"{\"role\":\"agent\"}",match:"\"v1\"")) await Problem(response,HttpStatusCode.Conflict,"OWNER_INVARIANT");
        using (var response=await Send(HttpMethod.Patch,$"/api/v1/memberships/{agent}",subject:"agent",body:"{\"role\":\"operator\"}",match:"\"v1\"")) Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var denied=await Send(HttpMethod.Get,"/api/v1/memberships",subject:"agent"); await Problem(denied,HttpStatusCode.Forbidden,"INSUFFICIENT_SCOPE");
    }
    [Fact]
    public async Task Previously_revoked_owner_can_be_reactivated_without_changing_role()
    {
        var target=Guid.NewGuid(); await Execute($"INSERT INTO platform.memberships VALUES ('{tenant}','{target}','{issuer.Issuer.AbsoluteUri}','exowner','owner','inactive',1)");
        using var response=await Send(HttpMethod.Patch,$"/api/v1/memberships/{target}",body:"{\"status\":\"active\"}",match:"\"v1\""); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal("owner",json.RootElement.GetProperty("role").GetString()); Assert.Equal("active",json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Signed_service_with_owner_membership_but_no_verified_staff_binding_is_denied()
    {
        var service=Guid.NewGuid(); await Execute($"INSERT INTO platform.memberships VALUES ('{tenant}','{service}','{issuer.Issuer.AbsoluteUri}','service','owner','active',1)");
        using var response=await Send(HttpMethod.Get,"/api/v1/memberships",subject:"service"); await Problem(response,HttpStatusCode.Forbidden,"TENANT_ACCESS_DENIED");
    }

    [Fact]
    public async Task Owner_role_field_is_invalid_even_when_request_also_revokes_existing_owner()
    {
        var second=Guid.NewGuid(); await Execute($"INSERT INTO platform.memberships VALUES ('{tenant}','{second}','{issuer.Issuer.AbsoluteUri}','second','owner','active',1)");
        using var response=await Send(HttpMethod.Patch,$"/api/v1/memberships/{owner}",body:"{\"role\":\"owner\",\"status\":\"revoked\"}",match:"\"v1\"");
        await Problem(response,HttpStatusCode.BadRequest,"REQUEST_INVALID");
        Assert.Equal(1L,await Scalar($"SELECT version FROM platform.memberships WHERE id='{owner}'"));
        Assert.Equal(1L,await Scalar($"SELECT count(*) FROM platform.memberships WHERE id='{owner}' AND role='owner' AND status='active'"));
        Assert.Equal(0L,await Scalar("SELECT count(*) FROM audit.entries"));
    }

    [Fact]
    public async Task Actual_reader_writer_deadlock_rolls_back_one_transaction_and_patch_remains_atomic()
    {
        await using var admin = new NpgsqlConnection(db.AdminConnectionString); await admin.OpenAsync();
        await using var tx = await admin.BeginTransactionAsync();
        await using (var setup = new NpgsqlCommand("SET LOCAL deadlock_timeout='50ms'; SELECT id FROM platform.memberships WHERE id=@id FOR SHARE",admin,tx))
        { setup.Parameters.AddWithValue("id",agent); await setup.ExecuteNonQueryAsync(); }
        var patch = Send(HttpMethod.Patch,$"/api/v1/memberships/{agent}",body:"{\"status\":\"active\"}",match:"\"v1\"");
        await using var observer = new NpgsqlConnection(db.AdminConnectionString); await observer.OpenAsync();
        var waiting = false;
        for(var i=0; i<100 && !waiting; i++)
        {
            await using var probe=new NpgsqlCommand("SELECT EXISTS (SELECT FROM pg_stat_activity WHERE wait_event_type='Lock' AND query LIKE '%platform.update_membership%')",observer);
            waiting = (bool)(await probe.ExecuteScalarAsync())!;
            if (!waiting) await Task.Delay(50);
        }
        Assert.True(waiting,"PATCH did not wait for the reader lock");
        var victimWasReader=false;
        try
        {
            await using var cross=new NpgsqlCommand("UPDATE platform.memberships SET role=role WHERE id=@id",admin,tx); cross.Parameters.AddWithValue("id",owner);
            await cross.ExecuteNonQueryAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "40P01") { victimWasReader=true; await tx.RollbackAsync(); }
        using var response=await patch.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(2L,await Scalar($"SELECT version FROM platform.memberships WHERE id='{agent}'"));
        Assert.Equal(1L,await Scalar("SELECT count(*) FROM audit.entries"));
        Assert.Equal(1L,await Scalar($"SELECT count(*) FROM platform.memberships WHERE id='{agent}' AND status='active'"));
        _ = victimWasReader; // Either PostgreSQL victim is safe: API retries or reader rolls back.
    }

    [Fact]
    public async Task Connection_lost_during_commit_is_not_retried()
    {
        await Execute("CREATE SEQUENCE audit.commit_attempts");
        await using (var admin=new NpgsqlConnection(db.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var setup=new NpgsqlCommand("""
                CREATE FUNCTION audit.terminate_commit_test() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
                BEGIN
                    PERFORM pg_catalog.nextval('audit.commit_attempts');
                    PERFORM pg_catalog.pg_terminate_backend(pg_catalog.pg_backend_pid());
                    RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER terminate_commit_test AFTER INSERT ON audit.entries DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION audit.terminate_commit_test();
                """,admin);
            await setup.ExecuteNonQueryAsync();
        }
        using var response=await Send(HttpMethod.Patch,$"/api/v1/memberships/{agent}",body:"{\"status\":\"active\"}",match:"\"v1\"");
        await Problem(response,HttpStatusCode.ServiceUnavailable,"IDENTITY_STORE_UNAVAILABLE");
        Assert.Equal(1L,await Scalar("SELECT count(*) FROM audit.commit_attempts WHERE is_called AND last_value=1"));
        Assert.Equal(1L,await Scalar($"SELECT version FROM platform.memberships WHERE id='{agent}'"));
        Assert.Equal(0L,await Scalar("SELECT count(*) FROM audit.entries"));
    }

}
