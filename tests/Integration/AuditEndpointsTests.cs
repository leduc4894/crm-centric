using System.Net;
using System.Text.Json;
using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed class AuditEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture db = new();
    private readonly Guid tenant = Guid.NewGuid(), other = Guid.NewGuid();
    private readonly Guid actorMembership = Guid.NewGuid();
    private FakeOidcIssuer issuer = null!;
    private ApiFactory factory = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        await db.InitializeAsync();
        issuer = await FakeOidcIssuer.StartAsync();
        await using var connection = new NpgsqlConnection(db.MigrationConnectionString);
        await connection.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connection, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(connection, db.ApplicationRole, default);
        await using var seed = new NpgsqlCommand("""
            INSERT INTO platform.tenants VALUES (@t,'A','active',now()),(@o,'B','active',now());
            INSERT INTO platform.memberships
              SELECT @t,gen_random_uuid(),@iss,s,r,'active',1 FROM (VALUES
                ('owner','owner'),('admin','admin'),('agent','agent'),('operator','operator'),('unbound','admin')) m(s,r);
            INSERT INTO platform.memberships VALUES (@t,@actor,@iss,'staff@example.invalid','admin','inactive',1);
            INSERT INTO platform.memberships VALUES (@o,gen_random_uuid(),@iss,'owner','admin','active',1);
            INSERT INTO platform.staff_bindings SELECT @iss,s,'active','test' FROM unnest(ARRAY['owner','admin','agent','operator']) s;
            INSERT INTO audit.entries(id,tenant_id,actor_issuer,actor_subject,action,target_id,summary,correlation_id,created_at)
              SELECT ('00000000-0000-0000-0000-00000000000'||i)::uuid,@t,@iss,'staff@example.invalid',action,
                @t,'{}','example-correlation','2026-01-01T00:00:00.123456Z'
              FROM unnest(ARRAY['tenant.created','membership.updated','invite.created','invite.revoked','invite.accepted']) WITH ORDINALITY a(action,i);
            INSERT INTO audit.entries VALUES(gen_random_uuid(),@o,@iss,'owner','tenant.created',@o,'{}','other-correlation',now());
            """, connection);
        seed.Parameters.AddWithValue("actor", actorMembership);
        seed.Parameters.AddWithValue("t", tenant);
        seed.Parameters.AddWithValue("o", other);
        seed.Parameters.AddWithValue("iss", issuer.Issuer.AbsoluteUri);
        await seed.ExecuteNonQueryAsync();
        factory = new ApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Platform"] = db.ApplicationConnectionString,
            ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri,
            ["Oidc:Audience"] = "crm-api",
            ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer, ".well-known/openid-configuration").AbsoluteUri,
            ["Oidc:AllowHttpLoopback"] = "true"
        });
        client = factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        client.Dispose(); await factory.DisposeAsync(); await issuer.DisposeAsync(); await db.DisposeAsync();
    }

    private async Task<HttpResponseMessage> Get(string query = "", string subject = "owner", Guid? selected = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/audit" + query);
        request.Headers.Authorization = new("Bearer", issuer.IssueToken(new() { Subject = subject }));
        request.Headers.Add("X-Tenant-Id", (selected ?? tenant).ToString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Authorized_read_projects_only_tenant_records_and_pages_tied_timestamps_in_descending_id_order()
    {
        var actions = new List<string>();
        var ids = new List<Guid>();
        string? cursor = null, actor = null;
        do
        {
            using var response = await Get("?limit=2" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), "admin");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.False(body.Contains("staff@example.invalid", StringComparison.Ordinal));
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            Assert.True(root.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(["items", "nextCursor"]));
            foreach (var entry in root.GetProperty("items").EnumerateArray())
            {
                Assert.True(entry.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(["id", "action", "target", "actor", "correlationId", "summary", "timestamp"]));
                var id = entry.GetProperty("id").GetGuid();
                Assert.DoesNotContain(id, ids);
                ids.Add(id);
                actions.Add(entry.GetProperty("action").GetString()!);
                Assert.Equal(tenant.ToString(), entry.GetProperty("target").GetString());
                Assert.Equal("example-correlation", entry.GetProperty("correlationId").GetString());
                Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00.123456Z"), entry.GetProperty("timestamp").GetDateTimeOffset());
                var current = entry.GetProperty("actor").GetString()!;
                Assert.Equal(actorMembership.ToString(), current);
                actor ??= current;
                Assert.Equal(actor, current);
                Assert.InRange(entry.GetProperty("summary").GetString()!.Length, 0, 1000);
            }
            cursor = root.GetProperty("nextCursor").GetString();
            if (cursor is not null)
            {
                using var cross = await Get("?cursor=" + Uri.EscapeDataString(cursor), selected: other);
                await AssertProblem(cross, HttpStatusCode.BadRequest, "CURSOR_INVALID");
            }
        } while (cursor is not null);
        Assert.Equal(new[] { "invite.accepted", "invite.revoked", "invite.created", "membership.updated", "tenant.created" }, actions);
        using var own = await Get(selected: other);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var page = JsonDocument.Parse(await own.Content.ReadAsStringAsync());
        Assert.Equal(other.ToString(), Assert.Single(page.RootElement.GetProperty("items").EnumerateArray()).GetProperty("target").GetString());
    }

    [Fact]
    public async Task Runtime_role_cannot_read_or_mutate_the_table_or_select_another_tenant_through_the_function()
    {
        await using var connection = new NpgsqlConnection(db.ApplicationConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[] {
            "SELECT id FROM audit.entries",
            "UPDATE audit.entries SET summary='changed'",
            "DELETE FROM audit.entries",
            "SELECT * FROM platform.list_audit_entries(@other,@issuer,'owner',NULL,NULL,10)"
        })
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await using var scope = new NpgsqlCommand("SELECT set_config('crm.tenant_id',@tenant,true)", connection, transaction);
            scope.Parameters.AddWithValue("tenant", tenant.ToString());
            await scope.ExecuteScalarAsync();
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("other", other);
            command.Parameters.AddWithValue("issuer", issuer.Issuer.AbsoluteUri);
            var denied = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
    }

    [Fact]
    public async Task Invitation_audit_projection_excludes_email_raw_token_and_digest()
    {
        const string email = "invited@example.invalid";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/invites");
        request.Headers.Authorization = new("Bearer", issuer.IssueToken(new() { Subject = "owner" }));
        request.Headers.Add("X-Tenant-Id", tenant.ToString());
        request.Content = System.Net.Http.Json.JsonContent.Create(new { email, role = "agent" });
        using var created = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var invite = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var token = invite.RootElement.GetProperty("token").GetString()!;
        var target = invite.RootElement.GetProperty("id").GetGuid();
        Assert.True(Platform.Api.Identity.InviteToken.TryHash(token, out var digest));

        using var response = await Get();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { token, email, Convert.ToHexString(digest), Convert.ToBase64String(digest) })
            Assert.False(body.Contains(secret, StringComparison.OrdinalIgnoreCase));
        using var page = JsonDocument.Parse(body);
        var entry = page.RootElement.GetProperty("items")[0];
        Assert.Equal(target, entry.GetProperty("target").GetGuid());
        Assert.Equal("invite.created", entry.GetProperty("action").GetString());
        Assert.Equal("Invitation created", entry.GetProperty("summary").GetString());
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode code, string reason)
    {
        Assert.Equal(code, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(reason, result.RootElement.GetProperty("reasonToken").GetString());
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), result.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Access_requires_verified_staff_and_active_manager_membership_and_valid_pagination()
    {
        foreach (var subject in new[] { "agent", "operator", "unbound" })
        {
            using var denied = await Get(subject: subject);
            await AssertProblem(denied, HttpStatusCode.Forbidden, subject == "unbound" ? "TENANT_ACCESS_DENIED" : "INSUFFICIENT_SCOPE");
        }
        using var cross = await Get(subject: "admin", selected: other);
        await AssertProblem(cross, HttpStatusCode.Forbidden, "TENANT_ACCESS_DENIED");
        foreach (var query in new[] { "?limit=0", "?limit=101", "?limit=2&limit=2", "?cursor=x&cursor=y", "?cursor=!!", "?cursor=" + new string('a', 2049) })
        {
            using var bad = await Get(query);
            await AssertProblem(bad, HttpStatusCode.BadRequest, query.StartsWith("?cursor=") && !query.Contains('&') ? "CURSOR_INVALID" : "REQUEST_INVALID");
        }
    }
}
