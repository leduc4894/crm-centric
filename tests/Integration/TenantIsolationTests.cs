using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Infrastructure.Identity;
using Platform.Infrastructure.Persistence;
using Xunit;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Integration;

public sealed class TenantIsolationTests : IAsyncLifetime
{
    private readonly PostgresFixture db = new();
    private readonly Guid a = Guid.NewGuid(), b = Guid.NewGuid(), suspended = Guid.NewGuid();
    public async Task InitializeAsync()
    {
        await db.InitializeAsync();
        await using var c = new NpgsqlConnection(db.MigrationConnectionString);
        await c.OpenAsync();
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(c, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(c, db.ApplicationRole, CancellationToken.None);
        await PostgresFixture.ExecuteAsync(c, $"""
            INSERT INTO platform.tenants VALUES ('{a}','A','active',now()), ('{b}','B','active',now()), ('{suspended}','C','suspended',now());
            INSERT INTO platform.memberships VALUES
            ('{a}',gen_random_uuid(),'issuer','alice','owner','active',1),
            ('{b}',gen_random_uuid(),'issuer','bob','owner','active',1),
            ('{b}',gen_random_uuid(),'issuer','inactive','agent','inactive',1),
            ('{suspended}',gen_random_uuid(),'issuer','alice','agent','active',1);
            """);
    }
    public Task DisposeAsync() => db.DisposeAsync();
    private NpgsqlConnection App() => new(new NpgsqlConnectionStringBuilder(db.ApplicationConnectionString) { MaxPoolSize = 1 }.ConnectionString);
    private static async Task<long> Count(NpgsqlConnection c, Guid tenant) {
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM platform.memberships WHERE tenant_id=@id", c);
        cmd.Parameters.AddWithValue("id", tenant);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
    private static async Task AssertClean(NpgsqlConnection c, Guid tenant) {
        await using var cmd = new NpgsqlCommand("SELECT nullif(current_setting('crm.tenant_id',true),'') IS NULL", c);
        Assert.Equal(true, await cmd.ExecuteScalarAsync());
        Assert.Equal(0, await Count(c, tenant));
    }
    [Fact]
    public async Task Verified_context_enforces_isolation_and_cleans_pool()
    {
        await using var c = App(); await c.OpenAsync();
        await AssertClean(c, a);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => VerifiedTenantTransaction.BeginAsync(c, "issuer", "alice", b, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => VerifiedTenantTransaction.BeginAsync(c, "issuer", "inactive", b, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => VerifiedTenantTransaction.BeginAsync(c, "issuer", "alice", suspended, CancellationToken.None));
        await AssertClean(c, a);
        await using (var tx = await VerifiedTenantTransaction.BeginAsync(c, "issuer", "alice", a, CancellationToken.None)) {
            Assert.Equal(1, await Count(c, a)); Assert.Equal(0, await Count(c, b));
            await Assert.ThrowsAsync<InvalidOperationException>(() => VerifiedTenantTransaction.BeginAsync(c, "issuer", "alice", a, CancellationToken.None));
            foreach (var sql in new[] { "INSERT INTO platform.memberships DEFAULT VALUES", "UPDATE platform.memberships SET status='inactive'", "DELETE FROM platform.memberships" }) {
                await using var savepoint = new NpgsqlCommand("SAVEPOINT attempt", c); await savepoint.ExecuteNonQueryAsync();
                var ex = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.ExecuteAsync(c, sql)); Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
                await PostgresFixture.ExecuteAsync(c, "ROLLBACK TO SAVEPOINT attempt");
            }
            await tx.CommitAsync();
        }
        await c.CloseAsync(); await c.OpenAsync(); await AssertClean(c, a);
        await using (var tx = await VerifiedTenantTransaction.BeginAsync(c, "issuer", "alice", a, CancellationToken.None)) await tx.RollbackAsync();
        await c.CloseAsync(); await c.OpenAsync(); await AssertClean(c, a);
        try { await using var tx = await VerifiedTenantTransaction.BeginAsync(c, "issuer", "alice", a, CancellationToken.None); throw new Exception("simulated"); } catch (Exception ex) when (ex.Message == "simulated") { }
        await c.CloseAsync(); await c.OpenAsync(); await AssertClean(c, a);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VerifiedTenantTransaction.BeginAsync(c, "issuer", "alice", a, cancelled.Token));
        await c.CloseAsync(); await c.OpenAsync(); await AssertClean(c, a);
    }
    private sealed class TenantProbe(string connectionString) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Path != "/test-tenant") { await continuation(); return; }
                var auth = await context.AuthenticateAsync();
                if (!auth.Succeeded) { await context.ChallengeAsync(); return; }
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                try
                {
                    await using var tx = await VerifiedTenantTransaction.BeginAsync(connection,
                        auth.Principal!.FindFirst("iss")!.Value, auth.Principal.FindFirst("sub")!.Value,
                        Guid.Parse(context.Request.Headers["X-Tenant-Id"].ToString()), context.RequestAborted);
                    context.Response.StatusCode = 200;
                }
                catch (UnauthorizedAccessException) { context.Response.StatusCode = 403; }
            });
            next(app);
        };
    }

    [Fact]
    public async Task Tenant_header_does_not_authorize_another_tenant()
    {
        await using var issuer = await FakeOidcIssuer.StartAsync();
        await using (var migration = new NpgsqlConnection(db.MigrationConnectionString))
        {
            await migration.OpenAsync();
            await using var update = new NpgsqlCommand("UPDATE platform.memberships SET issuer=@issuer WHERE subject='alice'", migration);
            update.Parameters.AddWithValue("issuer", issuer.Issuer.AbsoluteUri);
            await update.ExecuteNonQueryAsync();
        }
        await using var baseFactory = new ApiFactory(new Dictionary<string,string?> {
            ["ConnectionStrings:Platform"] = db.ApplicationConnectionString,
            ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri, ["Oidc:Audience"] = "crm-api",
            ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer, ".well-known/openid-configuration").AbsoluteUri,
            ["Oidc:AllowHttpLoopback"] = "true" });
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IStartupFilter>(new TenantProbe(db.ApplicationConnectionString))));
        using var client = factory.CreateClient();
        async Task<HttpStatusCode> Request(Guid tenant) {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/test-tenant");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.IssueToken(new() { Subject = "alice" }));
            request.Headers.Add("X-Tenant-Id", tenant.ToString());
            using var result = await client.SendAsync(request); return result.StatusCode;
        }
        Assert.Equal(HttpStatusCode.OK, await Request(a));
        Assert.Equal(HttpStatusCode.Forbidden, await Request(b));
    }

    private async Task WaitForLockAsync(int blockedPid, int blockerPid, Task pending)
    {
        await using var observer = new NpgsqlConnection(db.AdminConnectionString);
        await observer.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var cmd = new NpgsqlCommand("SELECT @blocker = ANY(pg_blocking_pids(@blocked))", observer);
            cmd.Parameters.AddWithValue("blocker", blockerPid);
            cmd.Parameters.AddWithValue("blocked", blockedPid);
            if (await cmd.ExecuteScalarAsync() is true) return;
            Assert.False(pending.IsCompleted, "Operation completed without waiting for the expected PostgreSQL lock.");
            await Task.Delay(50);
        }
        throw new TimeoutException("PostgreSQL did not report the expected blocking relationship.");
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_membership_lock_cleans_connection()
    {
        await using var migration = new NpgsqlConnection(db.MigrationConnectionString); await migration.OpenAsync();
        await using var lockTx = await migration.BeginTransactionAsync();
        await using var locked = new NpgsqlCommand("UPDATE platform.memberships SET version=version+1 WHERE tenant_id=@id", migration, lockTx);
        locked.Parameters.AddWithValue("id", a); await locked.ExecuteNonQueryAsync();
        await using var app = App(); await app.OpenAsync();
        using var cts = new CancellationTokenSource();
        var authorization = VerifiedTenantTransaction.BeginAsync(app, "issuer", "alice", a, cts.Token);
        try
        {
            await WaitForLockAsync(app.ProcessID, migration.ProcessID, authorization);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authorization);
        }
        finally { cts.Cancel(); }
        await lockTx.RollbackAsync();
        await app.CloseAsync(); await app.OpenAsync(); await AssertClean(app, a);
    }

    [Fact]
    public async Task Revocation_waits_for_authorized_transaction_and_subsequent_request_is_denied()
    {
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString); await app.OpenAsync();
        await using var admin = new NpgsqlConnection(db.MigrationConnectionString); await admin.OpenAsync();
        await using var tx = await VerifiedTenantTransaction.BeginAsync(app, "issuer", "alice", a, CancellationToken.None);
        await using var revoke = new NpgsqlCommand("UPDATE platform.memberships SET status='inactive' WHERE tenant_id=@id AND subject='alice'", admin);
        revoke.Parameters.AddWithValue("id", a);
        var pending = revoke.ExecuteNonQueryAsync();
        await WaitForLockAsync(admin.ProcessID, app.ProcessID, pending);
        await tx.CommitAsync(); Assert.Equal(1, await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => VerifiedTenantTransaction.BeginAsync(app, "issuer", "alice", a, CancellationToken.None));
        await AssertClean(app, a);
    }
}
