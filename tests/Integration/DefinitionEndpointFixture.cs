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

public abstract class DefinitionEndpointFixture : IAsyncLifetime
{
    protected readonly PostgresFixture db = new();
    protected readonly Guid tenant = Guid.NewGuid(), other = Guid.NewGuid();
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
            """, c);
        cmd.Parameters.AddWithValue("t", tenant); cmd.Parameters.AddWithValue("o", other); cmd.Parameters.AddWithValue("iss", issuer.Issuer.AbsoluteUri); await cmd.ExecuteNonQueryAsync();
        factory = new ApiFactory(new Dictionary<string, string?> { ["ConnectionStrings:Platform"] = db.ApplicationConnectionString, ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri, ["Oidc:Audience"] = "crm-api", ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer, ".well-known/openid-configuration").AbsoluteUri, ["ContactCursor:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), ["Oidc:AllowHttpLoopback"] = "true" });
        client = factory.CreateClient();
    }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); await issuer.DisposeAsync(); await db.DisposeAsync(); }
    protected async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? body = null, string subject = "owner", string? selected = null, bool anonymous = false, string[]? match = null)
    {
        using var req = new HttpRequestMessage(method, path);
        if (!anonymous) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.IssueToken(new() { Subject = subject, Role = "owner" }));
        req.Headers.Add("X-Tenant-Id", selected ?? tenant.ToString());
        if (match is not null) req.Headers.TryAddWithoutValidation("If-Match", match);
        if (body is not null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(req);
    }
    protected async Task<Guid> CreateId(string path, string json, bool foreign = false)
    {
        using var response = await Send(HttpMethod.Post, path, json, foreign ? "other" : "owner", foreign ? other.ToString() : null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    protected static async Task Problem(HttpResponseMessage r, HttpStatusCode status, string reason)
    {
        Assert.Equal(status, r.StatusCode); Assert.Equal("application/problem+json", r.Content.Headers.ContentType!.MediaType);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); Assert.Equal(reason, doc.RootElement.GetProperty("reasonToken").GetString());
        Assert.Equal(r.Headers.GetValues("X-Correlation-Id").Single(), doc.RootElement.GetProperty("correlationId").GetString());
    }
    protected async Task<long> Count(string table, Guid t)
    {
        await using var c = new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync(); await using var tx = await c.BeginTransactionAsync();
        await using var setting = new NpgsqlCommand("SELECT set_config('crm.tenant_id',@t,true)", c, tx); setting.Parameters.AddWithValue("t", t.ToString()); await setting.ExecuteNonQueryAsync();
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table}", c, tx); return (long)(await cmd.ExecuteScalarAsync())!;
    }
    protected async Task VerifyCreation(string path, string body, string duplicate, string actor, string action)
    {
        var responses = await Task.WhenAll(Send(HttpMethod.Post, path, body, actor), Send(HttpMethod.Post, path, duplicate, actor));
        var created = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await Problem(conflict, HttpStatusCode.Conflict, action == "tag.created" ? "TAG_NAME_CONFLICT" : "FIELD_KEY_CONFLICT");
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();
        await using var c = new NpgsqlConnection(db.MigrationConnectionString);
        await c.OpenAsync();
        await using var audit = new NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE tenant_id=$1 AND target_id=$2 AND actor_subject=$3 AND action=$4 AND correlation_id=$5 AND actor_issuer=$6", c);
        foreach (object value in new object[] { tenant, id, actor, action, created.Headers.GetValues("X-Correlation-Id").Single(), issuer.Issuer.AbsoluteUri })
            audit.Parameters.Add(new NpgsqlParameter { Value = value });
        Assert.Equal(1L, await audit.ExecuteScalarAsync());
        Assert.Equal(0, await Count("outbox.events", tenant));
        foreach (var response in responses) response.Dispose();
    }
    protected async Task VerifyAuditRollback(string path, string body, string table, string action)
    {
        await using var connection = new NpgsqlConnection(db.MigrationConnectionString);
        await connection.OpenAsync();
        // Reject only this audit action to exercise the same transaction as the definition insert.
        await using var command = new NpgsqlCommand(
            $"ALTER TABLE audit.entries ADD CONSTRAINT simulate_audit_failure CHECK(action <> '{action}')", connection);
        await command.ExecuteNonQueryAsync();
        using var response = await Send(HttpMethod.Post, path, body);
        await Problem(response, HttpStatusCode.ServiceUnavailable, "CRM_STORE_UNAVAILABLE");
        Assert.Equal(0, await Count(table, tenant));
    }
}
