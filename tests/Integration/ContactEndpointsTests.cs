using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed partial class ContactEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture db = new();
    private readonly Guid tenant = Guid.NewGuid(), other = Guid.NewGuid();
    private FakeOidcIssuer issuer = null!;
    private ApiFactory factory = null!;
    private HttpClient client = null!;
    public async Task InitializeAsync()
    {
        await db.InitializeAsync(); issuer = await FakeOidcIssuer.StartAsync();
        await using var c = new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync();
        await using var ctx = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(c, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await ctx.Database.MigrateAsync(); await DatabaseRoleGrants.ApplyAsync(c, db.ApplicationRole, default);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO platform.tenants VALUES (@t,'A','active',now()),(@o,'B','active',now());
            INSERT INTO platform.memberships VALUES
                (@t,gen_random_uuid(),@iss,'owner','owner','active',1),(@t,gen_random_uuid(),@iss,'admin','admin','active',1),
                (@t,gen_random_uuid(),@iss,'agent','agent','active',1),(@t,gen_random_uuid(),@iss,'operator','operator','active',1),
                (@t,gen_random_uuid(),@iss,'revoked','agent','inactive',1),(@o,gen_random_uuid(),@iss,'other','owner','active',1);
            INSERT INTO platform.staff_bindings SELECT @iss,s,'active','ticket' FROM unnest(ARRAY['owner','admin','agent','operator','revoked','other','outsider']) s;
            """,c);
        cmd.Parameters.AddWithValue("t",tenant); cmd.Parameters.AddWithValue("o",other); cmd.Parameters.AddWithValue("iss",issuer.Issuer.AbsoluteUri); await cmd.ExecuteNonQueryAsync();
        factory = new ApiFactory(new Dictionary<string,string?> { ["ConnectionStrings:Platform"] = db.ApplicationConnectionString, ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri, ["Oidc:Audience"] = "crm-api", ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer,".well-known/openid-configuration").AbsoluteUri, ["Oidc:AllowHttpLoopback"] = "true" });
        client = factory.CreateClient();
    }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); await issuer.DisposeAsync(); await db.DisposeAsync(); }
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? body = null, string subject = "owner", string? selected = null, bool anonymous = false, string? match = null)
    {
        using var req = new HttpRequestMessage(method,path);
        if (!anonymous) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer",issuer.IssueToken(new() { Subject=subject, Role="owner" }));
        req.Headers.Add("X-Tenant-Id", selected ?? tenant.ToString());
        if (body is not null) req.Content = new StringContent(body,Encoding.UTF8,"application/json");
        if (match is not null) req.Headers.TryAddWithoutValidation("If-Match",match);
        return await client.SendAsync(req);
    }
    private static async Task Problem(HttpResponseMessage r,HttpStatusCode status,string reason)
    {
        Assert.Equal(status,r.StatusCode); Assert.Equal("application/problem+json",r.Content.Headers.ContentType!.MediaType);
        using var doc=JsonDocument.Parse(await r.Content.ReadAsStringAsync()); Assert.Equal(reason,doc.RootElement.GetProperty("reasonToken").GetString());
        Assert.Equal(r.Headers.GetValues("X-Correlation-Id").Single(),doc.RootElement.GetProperty("correlationId").GetString());
    }
    private async Task<long> Count(string table, Guid t)
    {
        await using var c = new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync(); await using var tx = await c.BeginTransactionAsync();
        await using var setting = new NpgsqlCommand("SELECT set_config('crm.tenant_id',@t,true)",c,tx); setting.Parameters.AddWithValue("t",t.ToString()); await setting.ExecuteNonQueryAsync();
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table}",c,tx); return (long)(await cmd.ExecuteScalarAsync())!;
    }
    [Fact]
    public async Task Create_and_get_allow_roles_and_null_and_journal_once()
    {
        foreach (var subject in new[]{"owner","admin","agent"})
        {
            using var created=await Send(HttpMethod.Post,"/api/v1/contacts",$"{{\"displayName\":\"Example\",\"email\":\"{subject}@example.test\"}}",subject);
            Assert.Equal(HttpStatusCode.Created,created.StatusCode); Assert.Equal("\"v1\"",created.Headers.ETag!.ToString());
            using var body=JsonDocument.Parse(await created.Content.ReadAsStringAsync()); var root=body.RootElement; var id=root.GetProperty("id").GetGuid();
            Assert.Equal(new[]{"createdAt","displayName","email","id","phone","updatedAt","version"},root.EnumerateObject().Select(x=>x.Name).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal("Example",root.GetProperty("displayName").GetString()); Assert.Equal(1,root.GetProperty("version").GetInt32());
            foreach(var reader in new[]{"owner","admin","agent","operator"})
            { using var get=await Send(HttpMethod.Get,$"/api/v1/contacts/{id}",subject:reader); Assert.Equal(HttpStatusCode.OK,get.StatusCode); Assert.Equal("\"v1\"",get.Headers.ETag!.ToString()); }
        }
        using(var empty=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"No email\",\"email\":null}")) Assert.Equal(HttpStatusCode.Created,empty.StatusCode);
        Assert.Equal(4,await Count("crm.contacts",tenant)); Assert.Equal(4,await Count("outbox.events",tenant));
        await using var c=new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync(); await using var cmd=new NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE action='contact.created' AND tenant_id=@t",c); cmd.Parameters.AddWithValue("t",tenant); Assert.Equal(4L,await cmd.ExecuteScalarAsync());
    }
    [Fact]
    public async Task Existing_email_requires_matching_strong_etag_and_updates_only_supplied_fields()
    {
        using var first=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"First\",\"email\":\"Person@example.test\",\"phone\":\"123\"}");
        Assert.Equal(HttpStatusCode.Created,first.StatusCode); using var json=JsonDocument.Parse(await first.Content.ReadAsStringAsync()); var id=json.RootElement.GetProperty("id").GetGuid();
        const string update="{\"displayName\":\"Second\",\"email\":\" person@EXAMPLE.test \"}";
        using(var missing=await Send(HttpMethod.Post,"/api/v1/contacts",update)) await Problem(missing,(HttpStatusCode)428,"PRECONDITION_REQUIRED");
        foreach(var value in new[]{"*","W/\"v1\"","\"v1\",\"v2\"","nonsense"})
        { using var bad=await Send(HttpMethod.Post,"/api/v1/contacts",update,match:value); await Problem(bad,HttpStatusCode.BadRequest,"PRECONDITION_INVALID"); }
        using(var stale=await Send(HttpMethod.Post,"/api/v1/contacts",update,match:"\"v2\"")) await Problem(stale,HttpStatusCode.PreconditionFailed,"VERSION_CONFLICT");
        Assert.Equal(1,await Count("outbox.events",tenant));
        using(var updated=await Send(HttpMethod.Post,"/api/v1/contacts",update,match:"\"v1\""))
        {
            Assert.Equal(HttpStatusCode.OK,updated.StatusCode); Assert.Equal("\"v2\"",updated.Headers.ETag!.ToString());
            using var data=JsonDocument.Parse(await updated.Content.ReadAsStringAsync()); Assert.Equal(id,data.RootElement.GetProperty("id").GetGuid());
            Assert.Equal("Second",data.RootElement.GetProperty("displayName").GetString()); Assert.Equal("123",data.RootElement.GetProperty("phone").GetString());
        }
        using(var cleared=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"Second\",\"email\":\"person@example.test\",\"phone\":null}",match:"\"v2\""))
        { Assert.Equal(HttpStatusCode.OK,cleared.StatusCode); Assert.Equal("\"v3\"",cleared.Headers.ETag!.ToString()); using var data=JsonDocument.Parse(await cleared.Content.ReadAsStringAsync()); Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("phone").ValueKind); }
        using(var noop=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"Second\",\"email\":\"person@example.test\",\"phone\":null}",match:"\"v3\""))
        { Assert.Equal(HttpStatusCode.OK,noop.StatusCode); Assert.Equal("\"v3\"",noop.Headers.ETag!.ToString()); }
        Assert.Equal(1,await Count("crm.contacts",tenant)); Assert.Equal(3,await Count("outbox.events",tenant));
        await using var c=new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync(); await using var cmd=new NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE action IN ('contact.created','contact.updated') AND tenant_id=@t",c); cmd.Parameters.AddWithValue("t",tenant); Assert.Equal(3L,await cmd.ExecuteScalarAsync());
    }
    [Fact]
    public async Task New_or_null_email_with_if_match_never_creates_a_contact()
    {
        foreach (var body in new[] { "{\"displayName\":\"New\",\"email\":\"new@example.test\"}", "{\"displayName\":\"New\",\"email\":null}", "{\"displayName\":\"New\"}" })
        {
            foreach (var malformed in new[] { "*", "W/\"v1\"", "\"v1\",\"v2\"", "invalid" })
            {
                using var invalid = await Send(HttpMethod.Post,"/api/v1/contacts",body,match:malformed);
                await Problem(invalid,HttpStatusCode.BadRequest,"PRECONDITION_INVALID");
            }
            using var unmatched = await Send(HttpMethod.Post,"/api/v1/contacts",body,match:"\"v1\"");
            await Problem(unmatched,HttpStatusCode.PreconditionFailed,"VERSION_CONFLICT");
        }
        Assert.Equal(0,await Count("crm.contacts",tenant)); Assert.Equal(0,await Count("outbox.events",tenant));
        await using var c=new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync(); await using var cmd=new NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE action IN ('contact.created','contact.updated') AND tenant_id=@t",c); cmd.Parameters.AddWithValue("t",tenant); Assert.Equal(0L,await cmd.ExecuteScalarAsync());
    }
    [Fact]
    public async Task Concurrent_matching_etag_has_at_most_one_mutation()
    {
        using var first=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"First\",\"email\":\"a@example.test\"}"); Assert.Equal(HttpStatusCode.Created,first.StatusCode);
        var results=await Task.WhenAll(Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"A\",\"email\":\"a@example.test\"}",match:"\"v1\""),Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"B\",\"email\":\"a@example.test\"}",match:"\"v1\""));
        try { Assert.Single(results,x=>x.StatusCode==HttpStatusCode.OK); Assert.Single(results,x=>x.StatusCode==HttpStatusCode.PreconditionFailed); } finally { foreach(var x in results) x.Dispose(); }
        Assert.Equal(1,await Count("crm.contacts",tenant)); Assert.Equal(2,await Count("outbox.events",tenant));
    }
    [Fact]
    public async Task Concurrent_creation_same_email_requires_precondition_for_second_request()
    {
        var body="{\"displayName\":\"First\",\"email\":\"a@example.test\"}";
        var results=await Task.WhenAll(Send(HttpMethod.Post,"/api/v1/contacts",body),Send(HttpMethod.Post,"/api/v1/contacts",body));
        try { Assert.Single(results,x=>x.StatusCode==HttpStatusCode.Created); Assert.Single(results,x=>x.StatusCode==(HttpStatusCode)428); } finally { foreach(var x in results) x.Dispose(); }
        Assert.Equal(1,await Count("crm.contacts",tenant)); Assert.Equal(1,await Count("outbox.events",tenant));
    }
    [Fact]
    public async Task Same_email_in_another_tenant_is_independent()
    {
        const string body="{\"displayName\":\"First\",\"email\":\"same@example.test\"}";
        using var a=await Send(HttpMethod.Post,"/api/v1/contacts",body); using var b=await Send(HttpMethod.Post,"/api/v1/contacts",body,"other",other.ToString());
        Assert.Equal(HttpStatusCode.Created,a.StatusCode); Assert.Equal(HttpStatusCode.Created,b.StatusCode);
    }
    [Fact]
    public async Task Invalid_requests_and_permissions_fail_closed()
    {
        foreach(var body in new[]{"{}","[]","{", "{\"displayName\":\"A\",\"tenantId\":\"00000000-0000-0000-0000-000000000000\"}","{\"displayName\":\"A\",\"unknown\":1}","{\"displayName\":\"A\",\"displayName\":\"B\"}","{\"displayName\":\"A\",\"email\":\"invalid\"}","{\"displayName\":\"\"}","{\"displayName\":\""+new string('a',201)+"\"}","{\"displayName\":\"A\",\"phone\":\""+new string('1',41)+"\"}","{\"displayName\":\"A\",\"email\":\""+new string('a',310)+"@example.test\"}"})
        { using var r=await Send(HttpMethod.Post,"/api/v1/contacts",body); await Problem(r,HttpStatusCode.BadRequest,"REQUEST_INVALID"); }
        using(var r=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"A\"}","operator")) await Problem(r,HttpStatusCode.Forbidden,"INSUFFICIENT_SCOPE");
        foreach(var subject in new[]{"revoked","outsider"}) { using var r=await Send(HttpMethod.Get,$"/api/v1/contacts/{Guid.NewGuid()}",subject:subject); await Problem(r,HttpStatusCode.Forbidden,"TENANT_ACCESS_DENIED"); }
        using(var r=await Send(HttpMethod.Get,$"/api/v1/contacts/{Guid.NewGuid()}",selected:"invalid")) await Problem(r,HttpStatusCode.BadRequest,"TENANT_CONTEXT_INVALID");
        using(var r=await Send(HttpMethod.Get,$"/api/v1/contacts/{Guid.NewGuid()}",anonymous:true)) await Problem(r,HttpStatusCode.Unauthorized,"AUTHENTICATION_REQUIRED");
        Assert.Equal(0,await Count("crm.contacts",tenant)); Assert.Equal(0,await Count("outbox.events",tenant));
    }
    [Fact]
    public async Task Journal_failure_rolls_back_create_and_update()
    {
        using var first=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"Original\",\"email\":\"a@example.test\"}");
        Assert.Equal(HttpStatusCode.Created,first.StatusCode);
        using var original=JsonDocument.Parse(await first.Content.ReadAsStringAsync()); var id=original.RootElement.GetProperty("id").GetGuid();
        await using (var c=new NpgsqlConnection(db.MigrationConnectionString))
        {
            await c.OpenAsync(); await using var cmd=new NpgsqlCommand("CREATE FUNCTION audit.fail_contact_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test'; END $$; CREATE TRIGGER fail_contact_test BEFORE INSERT ON audit.entries FOR EACH ROW EXECUTE FUNCTION audit.fail_contact_test()",c); await cmd.ExecuteNonQueryAsync();
        }
        using(var failed=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"Change\",\"email\":\"a@example.test\"}",match:"\"v1\"")) await Problem(failed,HttpStatusCode.ServiceUnavailable,"CRM_STORE_UNAVAILABLE");
        using(var failed=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"New\",\"email\":\"b@example.test\"}")) await Problem(failed,HttpStatusCode.ServiceUnavailable,"CRM_STORE_UNAVAILABLE");
        using(var get=await Send(HttpMethod.Get,$"/api/v1/contacts/{id}"))
        { Assert.Equal(HttpStatusCode.OK,get.StatusCode); Assert.Equal("\"v1\"",get.Headers.ETag!.ToString()); using var body=JsonDocument.Parse(await get.Content.ReadAsStringAsync()); Assert.Equal("Original",body.RootElement.GetProperty("displayName").GetString()); }
        Assert.Equal(1,await Count("crm.contacts",tenant)); Assert.Equal(1,await Count("outbox.events",tenant));
    }
    [Fact]
    public async Task Unicode_name_and_multiple_null_emails_are_supported()
    {
        foreach(var name in new[]{"\uFFFD","\U0001F600"})
        {
            var body=JsonSerializer.Serialize(new { displayName=name,email=(string?)null });
            using var created=await Send(HttpMethod.Post,"/api/v1/contacts",body); Assert.Equal(HttpStatusCode.Created,created.StatusCode);
            using var data=JsonDocument.Parse(await created.Content.ReadAsStringAsync()); Assert.Equal(name,data.RootElement.GetProperty("displayName").GetString());
        }
        Assert.Equal(2,await Count("crm.contacts",tenant)); Assert.Equal(2,await Count("outbox.events",tenant));
    }
    [Fact]
    public async Task Unauthorized_token_or_unverified_staff_cannot_access_contacts()
    {
        await using (var c=new NpgsqlConnection(db.MigrationConnectionString))
        {
            await c.OpenAsync(); await using var cmd=new NpgsqlCommand("INSERT INTO platform.memberships VALUES (@tenant,gen_random_uuid(),@issuer,'unverified','owner','active',1)",c);
            cmd.Parameters.AddWithValue("tenant",tenant); cmd.Parameters.AddWithValue("issuer",issuer.Issuer.AbsoluteUri); await cmd.ExecuteNonQueryAsync();
        }
        using(var denied=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"Example\"}","unverified")) await Problem(denied,HttpStatusCode.Forbidden,"TENANT_ACCESS_DENIED");
        using var request=new HttpRequestMessage(HttpMethod.Get,$"/api/v1/contacts/{Guid.NewGuid()}");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",issuer.IssueToken(new() { Subject="owner", PayloadType="ID" })); request.Headers.Add("X-Tenant-Id",tenant.ToString());
        using var response=await client.SendAsync(request); await Problem(response,HttpStatusCode.Unauthorized,"AUTHENTICATION_REQUIRED");
        Assert.Equal(0,await Count("crm.contacts",tenant)); Assert.Equal(0,await Count("outbox.events",tenant));
    }
    [Fact]
    public async Task Unique_violation_returns_safe_problem_without_postgres_detail()
    {
        await using (var c=new NpgsqlConnection(db.MigrationConnectionString))
        {
            await c.OpenAsync(); await using var cmd=new NpgsqlCommand("CREATE FUNCTION crm.reject_unique_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION USING ERRCODE='23505', MESSAGE='unique error', DETAIL='sensitive-synthetic-email@example.test'; END $$; CREATE TRIGGER reject_unique_test BEFORE INSERT ON crm.contacts FOR EACH ROW EXECUTE FUNCTION crm.reject_unique_test()",c); await cmd.ExecuteNonQueryAsync();
        }
        using var failed=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"Name\",\"email\":\"other@example.test\"}");
        await Problem(failed,HttpStatusCode.Conflict,"CONTACT_EMAIL_CONFLICT"); Assert.DoesNotContain("sensitive-synthetic-email",await failed.Content.ReadAsStringAsync());
        Assert.Equal(0,await Count("crm.contacts",tenant)); Assert.Equal(0,await Count("outbox.events",tenant));
    }
    [Fact]
    public async Task Another_tenant_contact_is_not_visible()
    {
        using var created=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"B\"}","other",other.ToString()); Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        using var json=JsonDocument.Parse(await created.Content.ReadAsStringAsync()); var id=json.RootElement.GetProperty("id").GetGuid();
        using var get=await Send(HttpMethod.Get,$"/api/v1/contacts/{id}"); await Problem(get,HttpStatusCode.NotFound,"NOT_FOUND");
    }
}
