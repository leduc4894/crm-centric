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

public sealed class TenantEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture db = new();
    private FakeOidcIssuer issuer = null!;
    private ApiFactory factory = null!;
    private HttpClient client = null!;
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}");

    public async Task InitializeAsync()
    {
        await db.InitializeAsync();
        issuer = await FakeOidcIssuer.StartAsync();
        await using var c = new NpgsqlConnection(db.MigrationConnectionString);
        await c.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(c, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(c, db.ApplicationRole, CancellationToken.None);
        for (var i = 1; i <= 105; i++)
        {
            await using var cmd = new NpgsqlCommand("INSERT INTO platform.tenants VALUES (@id,@name,@status,now()); INSERT INTO platform.memberships VALUES (@id,gen_random_uuid(),@issuer,@subject,'agent',@membership,1)", c);
            cmd.Parameters.AddWithValue("id", Id(i)); cmd.Parameters.AddWithValue("name", $"Tenant {i}");
            cmd.Parameters.AddWithValue("status", i == 2 ? "suspended" : "active");
            cmd.Parameters.AddWithValue("issuer", issuer.Issuer.AbsoluteUri); cmd.Parameters.AddWithValue("subject", i == 105 ? "bob" : "alice");
            cmd.Parameters.AddWithValue("membership", i == 104 ? "inactive" : "active");
            await cmd.ExecuteNonQueryAsync();
        }
        factory = new ApiFactory(new Dictionary<string, string?> {
            ["ConnectionStrings:Platform"] = db.ApplicationConnectionString,
            ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri, ["Oidc:Audience"] = "crm-api",
            ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer, ".well-known/openid-configuration").AbsoluteUri,
            ["Oidc:AllowHttpLoopback"] = "true" });
        client = factory.CreateClient();
    }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); await issuer.DisposeAsync(); await db.DisposeAsync(); }
    private async Task<HttpResponseMessage> Get(string query = "", string? subject = "alice")
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenants" + query);
        if (subject is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.IssueToken(new() { Subject = subject }));
        req.Headers.Add("X-Tenant-Id", Id(105).ToString());
        return await client.SendAsync(req);
    }
    private static async Task<JsonDocument> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    private static string Cursor(Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(id.ToString("D"))).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string token)
    {
        Assert.Equal(status, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var body = await Json(response); var root = body.RootElement;
        Assert.Equal(new[] { "correlationId", "reasonToken", "status", "title", "type" }, root.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal((int)status, root.GetProperty("status").GetInt32()); Assert.Equal(token, root.GetProperty("reasonToken").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.Equal(root.GetProperty("correlationId").GetString(), response.Headers.GetValues("X-Correlation-Id").Single());
    }
    [Fact]
    public async Task Authentication_and_principal_scoped_pagination_follow_contract()
    {
        using (var anonymous = await Get(subject: null)) await AssertProblem(anonymous, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
        using (var first = await Get("?limit=1"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode); using var json = await Json(first); var root = json.RootElement;
            Assert.Equal(new[] { "items", "nextCursor" }, root.EnumerateObject().Select(p => p.Name).Order().ToArray());
            var item = Assert.Single(root.GetProperty("items").EnumerateArray());
            Assert.Equal(new[] { "id", "name", "status" }, item.EnumerateObject().Select(p => p.Name).Order().ToArray());
            Assert.Equal(Id(1), item.GetProperty("id").GetGuid()); Assert.Equal(Cursor(Id(1)), root.GetProperty("nextCursor").GetString());
        }
        using (var second = await Get("?limit=1&cursor=" + Cursor(Id(1))))
        { using var json = await Json(second); var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray()); Assert.Equal(Id(2), item.GetProperty("id").GetGuid()); Assert.Equal("suspended", item.GetProperty("status").GetString()); }
        using (var defaultPage = await Get()) { using var json = await Json(defaultPage); Assert.Equal(20, json.RootElement.GetProperty("items").GetArrayLength()); }
        using (var hundred = await Get("?limit=100")) { using var json = await Json(hundred); Assert.Equal(100, json.RootElement.GetProperty("items").GetArrayLength()); Assert.Equal(Cursor(Id(100)), json.RootElement.GetProperty("nextCursor").GetString()); }
        using (var last = await Get("?limit=100&cursor=" + Cursor(Id(100)))) { using var json = await Json(last); Assert.Equal(new[] { Id(101), Id(102), Id(103) }, json.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid())); Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextCursor").ValueKind); }
        using (var bob = await Get(subject: "bob")) { using var json = await Json(bob); Assert.Equal(Id(105), Assert.Single(json.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid()); }
    }
    [Fact]
    public async Task Invalid_queries_and_stale_or_other_principal_cursor_fail_closed()
    {
        foreach (var query in new[] { "?limit=0", "?limit=101", "?limit=abc", "?limit=1&limit=2", "?cursor=x&cursor=y" })
        { using var response = await Get(query); await AssertProblem(response, HttpStatusCode.BadRequest, "REQUEST_INVALID"); }
        foreach (var cursor in new[] { "", "abc", "!", new string('a', 2049), Cursor(Id(105)), Cursor(Id(104)), Cursor(Guid.NewGuid()), Convert.ToBase64String(Encoding.UTF8.GetBytes(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa").ToString("D").ToUpperInvariant())).TrimEnd('='), Cursor(Id(1)) + "=" })
        { using var response = await Get("?cursor=" + cursor); await AssertProblem(response, HttpStatusCode.BadRequest, "CURSOR_INVALID"); }
        using (var response = await Get("?cursor=" + Cursor(Id(1)), "bob")) await AssertProblem(response, HttpStatusCode.BadRequest, "CURSOR_INVALID");
        await using var c = new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync();
        await using var revoke = new NpgsqlCommand("UPDATE platform.memberships SET status='inactive' WHERE tenant_id=@id", c); revoke.Parameters.AddWithValue("id", Id(1)); await revoke.ExecuteNonQueryAsync();
        using var revoked = await Get("?cursor=" + Cursor(Id(1))); await AssertProblem(revoked, HttpStatusCode.BadRequest, "CURSOR_INVALID");
    }
    [Fact]
    public async Task Cursor_at_minimum_uuid_works_and_issuer_is_part_of_identity()
    {
        await using var c = new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync();
        await using var seed = new NpgsqlCommand("INSERT INTO platform.tenants VALUES (@id,'Zero','active',now()); INSERT INTO platform.memberships VALUES (@id,gen_random_uuid(),@issuer,'alice','agent','active',1); INSERT INTO platform.memberships VALUES (@other,gen_random_uuid(),'https://other.example/','alice','agent','active',1)", c);
        seed.Parameters.AddWithValue("id", Guid.Empty); seed.Parameters.AddWithValue("issuer", issuer.Issuer.AbsoluteUri); seed.Parameters.AddWithValue("other", Id(105)); await seed.ExecuteNonQueryAsync();
        using var first = await Get("?limit=1"); using var firstJson = await Json(first);
        Assert.Equal(Guid.Empty, Assert.Single(firstJson.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(Cursor(Guid.Empty), firstJson.RootElement.GetProperty("nextCursor").GetString());
        using var next = await Get("?limit=1&cursor=" + Cursor(Guid.Empty)); using var nextJson = await Json(next);
        Assert.Equal(Id(1), Assert.Single(nextJson.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        using var otherIssuer = await Get("?cursor=" + Cursor(Id(105))); await AssertProblem(otherIssuer, HttpStatusCode.BadRequest, "CURSOR_INVALID");
    }
    [Fact]
    public async Task Unavailable_database_returns_problem_instead_of_empty_page()
    {
        await db.Container.StopAsync();
        using var response = await Get(); await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "IDENTITY_STORE_UNAVAILABLE");
    }
}
