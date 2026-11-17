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

public sealed class TenantProvisioningTests : IAsyncLifetime
{
    private readonly PostgresFixture db = new();
    private FakeOidcIssuer issuer = null!;
    private ApiFactory factory = null!;
    private HttpClient client = null!;
    public async Task InitializeAsync()
    {
        await db.InitializeAsync();
        issuer = await FakeOidcIssuer.StartAsync();
        await using var c = new NpgsqlConnection(db.MigrationConnectionString);
        await c.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(c, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(c, db.ApplicationRole, CancellationToken.None);
        factory = new ApiFactory(new Dictionary<string, string?> {
            ["ConnectionStrings:Platform"] = db.ApplicationConnectionString,
            ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri, ["Oidc:Audience"] = "crm-api",
            ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer, ".well-known/openid-configuration").AbsoluteUri,
            ["Oidc:AllowHttpLoopback"] = "true" });
        client = factory.CreateClient();
    }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); await issuer.DisposeAsync(); await db.DisposeAsync(); }
    private async Task<HttpResponseMessage> Post(string body, string? scope = "platform.tenants.create", string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenants");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? issuer.IssueToken(new() { Subject = "actor", Scope = scope }));
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(req);
    }
    [Fact]
    public async Task Valid_provisioner_creates_tenant_owner_and_audit_and_can_list_it()
    {
        using var response = await Post("{\"name\":\"Acme\"}");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(new[] { "id", "name", "status" }, root.EnumerateObject().Select(x => x.Name).Order().ToArray());
        var id = root.GetProperty("id").GetGuid();
        Assert.Equal("Acme", root.GetProperty("name").GetString());
        Assert.Equal("active", root.GetProperty("status").GetString());
        await using var c = new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT m.issuer,m.subject,m.role,m.status,m.version,a.action,a.actor_issuer,a.actor_subject,a.correlation_id FROM platform.memberships m JOIN audit.entries a ON a.tenant_id=m.tenant_id WHERE m.tenant_id=@id", c);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.Equal(issuer.Issuer.AbsoluteUri, reader.GetString(0)); Assert.Equal("actor", reader.GetString(1));
        Assert.Equal("owner", reader.GetString(2)); Assert.Equal("active", reader.GetString(3)); Assert.Equal(1, reader.GetInt64(4));
        Assert.Equal("tenant.created", reader.GetString(5)); Assert.Equal(issuer.Issuer.AbsoluteUri, reader.GetString(6)); Assert.Equal("actor", reader.GetString(7));
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), reader.GetString(8)); Assert.False(await reader.ReadAsync());
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenants");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.IssueToken(new() { Subject = "actor" }));
        using var list = await client.SendAsync(req); Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(id, Assert.Single(listJson.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }
    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string reason)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(reason, json.RootElement.GetProperty("reasonToken").GetString());
        Assert.Equal((int)status, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), json.RootElement.GetProperty("correlationId").GetString());
    }
    [Fact]
    public async Task Invalid_token_and_missing_or_inexact_scope_fail_closed()
    {
        using (var response = await Post("{\"name\":\"Acme\"}", token: "not.a.valid.jwt"))
            await AssertProblem(response, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
        static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var unsigned = Encode("{\"alg\":\"none\"}") + "." + Encode(JsonSerializer.Serialize(new { iss = issuer.Issuer.AbsoluteUri, sub = "actor", aud = "crm-api", scope = "platform.tenants.create", exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() })) + ".";
        using (var response = await Post("{\"name\":\"Acme\"}", token: unsigned))
            await AssertProblem(response, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
        foreach (var scope in new string?[] { null, "other", "platform.tenants.create.extra", "Platform.tenants.create", "platform.tenants.create  other" })
        {
            using var response = await Post("{\"name\":\"Acme\"}", scope);
            await AssertProblem(response, HttpStatusCode.Forbidden, "INSUFFICIENT_SCOPE");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.IssueToken(new() { Subject = "actor" }));
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        request.Content = new StringContent("{\"name\":\"Acme\",\"role\":\"owner\"}", Encoding.UTF8, "application/json");
        using var forged = await client.SendAsync(request);
        await AssertProblem(forged, HttpStatusCode.Forbidden, "INSUFFICIENT_SCOPE");
    }
    [Fact]
    public async Task Invalid_json_and_unknown_or_invalid_fields_are_rejected_without_mutation()
    {
        foreach (var body in new[] { "", "{}", "[]", "null", "{", "{\"name\":null}", "{\"name\":1}", "{\"name\":\"\"}", "{\"name\":\"  \"}", "{\"name\":\"ok\",\"subject\":\"forged\"}", "{\"name\":\"a\",\"name\":\"b\"}", JsonSerializer.Serialize(new { name = new string('x', 201) }) })
        {
            using var response = await Post(body);
            await AssertProblem(response, HttpStatusCode.BadRequest, "REQUEST_INVALID");
        }
        await AssertCounts(0, 0, 0);
    }
    private async Task AssertCounts(long tenants, long memberships, long audits)
    {
        await using var c = new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT (SELECT count(*) FROM platform.tenants),(SELECT count(*) FROM platform.memberships),(SELECT count(*) FROM audit.entries)", c);
        await using var reader = await cmd.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.Equal(tenants, reader.GetInt64(0)); Assert.Equal(memberships, reader.GetInt64(1)); Assert.Equal(audits, reader.GetInt64(2));
    }
    [Fact]
    public async Task Audit_insert_failure_rolls_back_all_provisioning_data()
    {
        await using (var c = new NpgsqlConnection(db.MigrationConnectionString))
        {
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand("CREATE FUNCTION audit.reject_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'forced audit failure'; END $$; CREATE TRIGGER reject_test BEFORE INSERT ON audit.entries FOR EACH ROW EXECUTE FUNCTION audit.reject_test()", c);
            await cmd.ExecuteNonQueryAsync();
        }
        using var response = await Post("{\"name\":\"Acme\"}");
        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "IDENTITY_STORE_UNAVAILABLE");
        await AssertCounts(0, 0, 0);
    }
    [Fact]
    public async Task Database_failure_returns_problem()
    {
        await db.Container.StopAsync();
        using var response = await Post("{\"name\":\"Acme\"}");
        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "IDENTITY_STORE_UNAVAILABLE");
    }

}
