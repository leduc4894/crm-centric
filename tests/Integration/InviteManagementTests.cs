using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Integration;

public sealed class InviteManagementTests : IAsyncLifetime
{
    private readonly OperationalSecurityTests.Capture logs = new();
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
        cmd.Parameters.AddWithValue("tenant", tenant); cmd.Parameters.AddWithValue("otherTenant", otherTenant); cmd.Parameters.AddWithValue("owner", owner); cmd.Parameters.AddWithValue("agent", agent); cmd.Parameters.AddWithValue("other", other); cmd.Parameters.AddWithValue("issuer", issuer.Issuer.AbsoluteUri); await cmd.ExecuteNonQueryAsync();
        factory = new ApiFactory(new Dictionary<string, string?> { ["ConnectionStrings:Platform"] = db.ApplicationConnectionString, ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri, ["Oidc:Audience"] = "crm-api", ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer, ".well-known/openid-configuration").AbsoluteUri, ["Oidc:AllowHttpLoopback"] = "true" });
        client = factory.CreateClient();
        factory.Services.GetRequiredService<ILoggerFactory>().AddProvider(logs);
    }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); await issuer.DisposeAsync(); await db.DisposeAsync(); }
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string subject = "owner", string? body = null, Guid? selected = null)
    {
        using var req = new HttpRequestMessage(method, path); req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.IssueToken(new() { Subject = subject, Role = "owner" })); req.Headers.Add("X-Tenant-Id", (selected ?? tenant).ToString());
        if (body is not null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(req);
    }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode code, string reason)
    {
        Assert.Equal(code, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal(reason, json.RootElement.GetProperty("reasonToken").GetString()); Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), json.RootElement.GetProperty("correlationId").GetString());
    }
    [Fact]
    public async Task Owner_creates_lists_and_revokes_once()
    {
        using var create = await Send(HttpMethod.Post, "/api/v1/invites", body: """{"email":"Person@EXAMPLE.com","role":"agent"}""");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.True(create.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var item = json.RootElement;
        Assert.True(item.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(["id", "email", "role", "expiresAt", "status", "token"]));
        Assert.Equal("Person@EXAMPLE.com", item.GetProperty("email").GetString());
        Assert.Equal("agent", item.GetProperty("role").GetString());
        Assert.Equal("pending", item.GetProperty("status").GetString());
        var expires = item.GetProperty("expiresAt").GetDateTimeOffset();
        var id = item.GetProperty("id").GetGuid();
        var token = item.GetProperty("token").GetString()!;
        Assert.True(Platform.Api.Identity.InviteToken.TryHash(token, out var hash));
        await using var c = new NpgsqlConnection(db.MigrationConnectionString);
        await c.OpenAsync();
        await using var check = new NpgsqlCommand("SELECT token_hash=@hash AND expires_at=@expires AND expires_at-created_at=interval '24 hours' AND email='Person@EXAMPLE.com' AND normalized_email='Person@example.com' FROM platform.invites i WHERE id=@id", c);
        check.Parameters.AddWithValue("hash", hash);
        check.Parameters.AddWithValue("id", id);
        check.Parameters.AddWithValue("expires", expires);

        Assert.True((bool?)await check.ExecuteScalarAsync());
        await using var record = new NpgsqlCommand("SELECT to_jsonb(i)::text FROM platform.invites i WHERE id=@id", c);
        record.Parameters.AddWithValue("id", id);
        Assert.False(((string)(await record.ExecuteScalarAsync())!).Contains(token, StringComparison.Ordinal));
        using var list = await Send(HttpMethod.Get, "/api/v1/invites");
        using var page = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var listed = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(listed.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(["id", "email", "role", "expiresAt", "status"]));
        Assert.Equal(id, listed.GetProperty("id").GetGuid());
        Assert.Equal("pending", listed.GetProperty("status").GetString());
        using var revoke = await Send(HttpMethod.Delete, $"/api/v1/invites/{id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using var again = await Send(HttpMethod.Delete, $"/api/v1/invites/{id}");
        await Problem(again, HttpStatusCode.Conflict, "INVITE_STATE_CONFLICT");
        await using var audit = new NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE target_id=@id AND action IN ('invite.created','invite.revoked')", c);
        audit.Parameters.AddWithValue("id", id);
        Assert.Equal(2L, await audit.ExecuteScalarAsync());
        await using var summaries = new NpgsqlCommand("SELECT string_agg(to_jsonb(e)::text, ' ') FROM audit.entries e", c);
        Assert.True(logs.Text.Contains("INVITE_STATE_CONFLICT", StringComparison.Ordinal));
        var persistedAudit = (string)(await summaries.ExecuteScalarAsync())!;
        foreach (var secret in new[] { token, Convert.ToHexString(hash), Convert.ToBase64String(hash), "Person@example.com" })
        {
            Assert.False(persistedAudit.Contains(secret, StringComparison.OrdinalIgnoreCase));
            Assert.False(logs.Text.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }

    private const string ValidBody = """{"email":"a@example.com","role":"agent"}""";
    private const string Path = "/api/v1/invites";

    private async Task Execute(string sql)
    {
        await using var c = new NpgsqlConnection(db.MigrationConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("tenant", tenant);
        cmd.Parameters.AddWithValue("issuer", issuer.Issuer.AbsoluteUri);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Management_requires_staff_and_authorized_tenant_role()
    {
        await Execute("""
            UPDATE platform.memberships SET status='active' WHERE tenant_id=@tenant;
            INSERT INTO platform.memberships VALUES
              (@tenant,gen_random_uuid(),@issuer,'operator','operator','active',1),
              (@tenant,gen_random_uuid(),@issuer,'service','admin','active',1),
              (@tenant,gen_random_uuid(),@issuer,'admin','admin','active',1);
            INSERT INTO platform.staff_bindings VALUES (@issuer,'operator','active','ticket'),(@issuer,'admin','active','ticket')
            """);
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Delete })
        {
            var path = method == HttpMethod.Delete ? Path + "/" + Guid.NewGuid() : Path;
            using var unauth = await client.SendAsync(new HttpRequestMessage(method, path));
            Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);
            foreach (var actor in new[] { "agent", "operator", "service" })
            {
                using var denied = await Send(method, path, actor, ValidBody);
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            using var cross = await Send(method, path, body: ValidBody, selected: otherTenant);
            await Problem(cross, HttpStatusCode.Forbidden, "TENANT_ACCESS_DENIED");
            using var req = new HttpRequestMessage(method, path);
            req.Headers.Authorization = new("Bearer", issuer.IssueToken(new() { Subject = "owner" }));
            using var missing = await client.SendAsync(req);
            await Problem(missing, HttpStatusCode.BadRequest, "TENANT_CONTEXT_INVALID");
            using var duplicate = new HttpRequestMessage(method,path);
            duplicate.Headers.Authorization = req.Headers.Authorization;
            duplicate.Headers.Add("X-Tenant-Id",new[] { tenant.ToString(), otherTenant.ToString() });
            using var ambiguous = await client.SendAsync(duplicate);
            await Problem(ambiguous,HttpStatusCode.BadRequest,"TENANT_CONTEXT_INVALID");
        }
        using var admin = await Send(HttpMethod.Post, Path, "admin", ValidBody);
        Assert.Equal(HttpStatusCode.Created, admin.StatusCode);
        using var list = await Send(HttpMethod.Get, Path, "admin");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var json = JsonDocument.Parse(await admin.Content.ReadAsStringAsync());
        using var revoke = await Send(HttpMethod.Delete, Path + "/" + json.RootElement.GetProperty("id").GetGuid(), "admin");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
    }

    [Fact]
    public async Task Rejects_invalid_body_and_pagination()
    {
        foreach (var body in new[] { "{", "{}", """{"email":"bad","role":"admin"}""",
            """{"email":"a@example.org","role":"owner"}""", """{"email":"a@example.org","role":"admin","role":"agent"}""",
            """{"email":"a@example.org","role":"admin","tenant_id":"x"}""" })
        {
            using var response = await Send(HttpMethod.Post, Path, body: body);
            await Problem(response, HttpStatusCode.BadRequest, "REQUEST_INVALID");
        }
        foreach (var query in new[] { "limit=0", "limit=101", "limit=x", "limit=2&limit=2", "cursor=x&cursor=y" })
        {
            using var response = await Send(HttpMethod.Get, Path + "?" + query);
            await Problem(response, HttpStatusCode.BadRequest, "REQUEST_INVALID");
        }
        using var invalid = await Send(HttpMethod.Get, Path + "?cursor=bad");
        await Problem(invalid, HttpStatusCode.BadRequest, "CURSOR_INVALID");
    }

    [Fact]
    public async Task Pagination_and_revoke_are_tenant_bound_and_expiry_uses_database_time()
    {
        await Execute($"INSERT INTO platform.memberships VALUES ('{otherTenant}',gen_random_uuid(),@issuer,'owner','admin','active',1)");
        for (var i = 0; i < 2; i++)
        {
            using var created = await Send(HttpMethod.Post, Path, body: ValidBody);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        await Execute("UPDATE platform.invites SET created_at=created_at-interval '2 days',expires_at=expires_at-interval '2 days' WHERE tenant_id=@tenant");
        using var first = await Send(HttpMethod.Get, Path + "?limit=1");
        using var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var row = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("expired", row.GetProperty("status").GetString());
        var id = row.GetProperty("id").GetGuid();
        var cursor = json.RootElement.GetProperty("nextCursor").GetString();
        var next = Path + "?limit=1&cursor=" + cursor;
        using var second = await Send(HttpMethod.Get, next);
        using var page = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.NotEqual(id, Assert.Single(page.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("nextCursor").ValueKind);
        using var wrongCursor = await Send(HttpMethod.Get, next, selected: otherTenant);
        await Problem(wrongCursor, HttpStatusCode.BadRequest, "CURSOR_INVALID");
        using var wrongId = await Send(HttpMethod.Delete, Path + "/" + id, selected: otherTenant);
        await Problem(wrongId, HttpStatusCode.NotFound, "INVITE_NOT_FOUND");
        using var expired = await Send(HttpMethod.Delete, Path + "/" + id);
        await Problem(expired, HttpStatusCode.Conflict, "INVITE_STATE_CONFLICT");
        await Execute("UPDATE platform.invites SET created_at=created_at+interval '2 days', expires_at=expires_at+interval '2 days', accepted_at=clock_timestamp() WHERE tenant_id=@tenant");
        using var accepted = await Send(HttpMethod.Delete,Path+"/"+id);
        await Problem(accepted,HttpStatusCode.Conflict,"INVITE_STATE_CONFLICT");
    }

    [Fact]
    public async Task Production_accepts_forwarded_https_only_from_configured_terminator()
    {
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Issuer"] = "https://issuer.invalid/",
                ["Oidc:MetadataAddress"] = "https://issuer.invalid/.well-known/openid-configuration",
                ["Invites:TrustedTlsProxy"] = "192.0.2.10"
            }));
        });
        foreach (var (peer, scheme, forwarded, status) in new[]
        {
            ("192.0.2.11","http","https",400), ("192.0.2.10","http","https,http",400),
            ("192.0.2.10","http","https",401), ("192.0.2.11","https","",401)
        })
        {
            var result = await production.Server.SendAsync(context =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                context.Request.Scheme = scheme;
                context.Request.Method = "GET";
                context.Request.Path = Path;
                context.Request.Headers["X-Forwarded-Proto"] = forwarded;
            });
            Assert.Equal(status, result.Response.StatusCode);
            Assert.Equal("no-store", result.Response.Headers.CacheControl.ToString());
        }
    }

    [Fact]
    public async Task Production_rejects_unrestricted_forwarded_headers_configuration()
    {
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Issuer"] = "https://issuer.invalid/",
                ["Oidc:MetadataAddress"] = "https://issuer.invalid/.well-known/openid-configuration",
                ["FORWARDEDHEADERS_ENABLED"] = "true"
            }));
        });
        var response = await production.Server.SendAsync(context =>
        {
            context.Request.Scheme = "https";
            context.Request.Method = "GET";
            context.Request.Path = Path;
        });
        Assert.Equal(503, response.Response.StatusCode);
    }

    [Fact]
    public async Task Failed_commit_returns_problem_and_does_not_regenerate_invitation()
    {
        await Execute("""
            CREATE SEQUENCE audit.invite_commit_calls;
            CREATE FUNCTION audit.reject_invite_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM nextval('audit.invite_commit_calls');
                RAISE EXCEPTION 'test commit failure' USING ERRCODE='40001';
            END $$;
            CREATE CONSTRAINT TRIGGER reject_invite_commit AFTER INSERT ON audit.entries
              DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION audit.reject_invite_commit();
            """);
        using var failed = await Send(HttpMethod.Post, Path, body: ValidBody);
        await Problem(failed, HttpStatusCode.ServiceUnavailable, "IDENTITY_STORE_UNAVAILABLE");
        using var error = JsonDocument.Parse(await failed.Content.ReadAsStringAsync());
        Assert.False(error.RootElement.TryGetProperty("token", out _));
        await using var c = new NpgsqlConnection(db.MigrationConnectionString);
        await c.OpenAsync();
        await using var state = new NpgsqlCommand("""
            SELECT is_called AND last_value=1 AND NOT EXISTS (SELECT FROM platform.invites)
                 AND NOT EXISTS (SELECT FROM audit.entries) FROM audit.invite_commit_calls
            """, c);
        Assert.True((bool?)await state.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(true,503)]
    [InlineData(false,401)]
    public async Task Development_checks_forwarding_configuration_before_loopback_exception(bool automatic, int expected)
    {
        using var development = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["FORWARDEDHEADERS_ENABLED"] = automatic.ToString()
            }));
        });
        var response = await development.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Scheme = "http";
            context.Request.Method = "GET";
            context.Request.Path = Path;
        });
        Assert.Equal(expected,response.Response.StatusCode);
    }

    [Fact]
    public async Task Invitation_input_is_read_before_tenant_mutation_locks()
    {
        await using var observer = new NpgsqlConnection(db.AdminConnectionString);
        await observer.OpenAsync();
        using var prior = await Send(HttpMethod.Post, Path, body: ValidBody);
        Assert.Equal(HttpStatusCode.Created, prior.StatusCode);
        using var created = JsonDocument.Parse(await prior.Content.ReadAsStringAsync());
        var revokePath = Path + "/" + created.RootElement.GetProperty("id").GetGuid();
        var revoked = false;
        using var input = new ObservedInput(Encoding.UTF8.GetBytes(ValidBody), async () =>
        {
            await using var check = new NpgsqlCommand("""
                SELECT EXISTS (SELECT FROM pg_locks l JOIN pg_stat_activity a ON a.pid=l.pid
                  WHERE a.datname=current_database() AND a.usename=@role AND l.locktype='advisory' AND l.granted)
                """, observer);
            check.Parameters.AddWithValue("role",db.ApplicationRole);
            Assert.False((bool)(await check.ExecuteScalarAsync())!);
            if (!revoked)
            {
                // This valid upload resumes after an independent request has committed its revocation.
                using var response = await Send(HttpMethod.Delete, revokePath).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                revoked = true;
            }
        });
        var result = await factory.Server.SendAsync(context =>
        {
            context.Request.Method = "POST";
            context.Request.Path = Path;
            context.Request.ContentType = "application/json";
            context.Request.Body = input;
            context.Request.Headers.Authorization = "Bearer " + issuer.IssueToken(new() { Subject="owner" });
            context.Request.Headers["X-Tenant-Id"] = tenant.ToString();
        });
        Assert.Equal(201,result.Response.StatusCode);
        Assert.True(input.ReadObserved);
        Assert.True(revoked);
        foreach (var invalidBody in new[] { "{", new string(' ', 8193) })
        {
            using var denied = await Send(HttpMethod.Post, Path, "agent", invalidBody);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using var tooLarge = await Send(HttpMethod.Post, Path, body: new string(' ', 8193));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
    }

    private sealed class ObservedInput(byte[] data, Func<Task> observe) : MemoryStream(data)
    {
        public bool ReadObserved { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await observe();
            ReadObserved = true;
            return await base.ReadAsync(buffer,ct);
        }
    }
}
