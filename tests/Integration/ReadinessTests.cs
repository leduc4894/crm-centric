using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Integration.Fixtures;
using Npgsql;
using Xunit;

namespace Integration;

public sealed class ReadinessTests : IAsyncLifetime
{
    private readonly PostgresFixture database = new(stableHostPort: true);
    private const string History = "platform.\"__EFMigrationsHistory\"";
    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    private ApiFactory Factory() => new(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Platform"] = new NpgsqlConnectionStringBuilder(database.ApplicationConnectionString)
            { MaxPoolSize = 1 }.ConnectionString
    });

    private async Task MigrateAsync() => Assert.Equal(0,
        (await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole)).ExitCode);

    private static async Task AssertReadyAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"ready\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    private static async Task AssertNotReadyAsync(HttpClient client, string reason)
    {
        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(reason, body.RootElement.GetProperty("reasonToken").GetString());
        Assert.Equal(Assert.Single(response.Headers.GetValues("X-Correlation-Id")),
            body.RootElement.GetProperty("correlationId").GetString());
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task Ready_uses_application_identity()
    {
        await MigrateAsync();
        await using var factory = Factory();
        using var client = factory.CreateClient();
        await AssertReadyAsync(client);
        // Revoking only app SELECT proves the API actually uses the restricted identity.
        await using var migration = new NpgsqlConnection(database.MigrationConnectionString);
        await migration.OpenAsync();
        await PostgresFixture.ExecuteAsync(migration, $"REVOKE SELECT ON {History} FROM {PostgresFixture.Quote(database.ApplicationRole)}");
        await AssertNotReadyAsync(client, "DATABASE_UNAVAILABLE");
    }

    [Fact]
    public async Task Unmigrated_database_is_not_ready_and_is_not_modified()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        await AssertNotReadyAsync(client, "DATABASE_SCHEMA_NOT_READY");
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT NOT EXISTS (SELECT FROM pg_namespace WHERE nspname='platform')", admin);
        Assert.Equal(true, await command.ExecuteScalarAsync());
        await MigrateAsync();
        await AssertReadyAsync(client);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_future_migration_is_not_ready(bool future)
    {
        await MigrateAsync();
        await using var migration = new NpgsqlConnection(database.MigrationConnectionString);
        await migration.OpenAsync();
        await PostgresFixture.ExecuteAsync(migration, future
            ? $"INSERT INTO {History} VALUES ('20990101000000_Future', '10.0.0')"
            : $"DELETE FROM {History}");
        await using var factory = Factory();
        using var client = factory.CreateClient();
        await AssertNotReadyAsync(client, "DATABASE_SCHEMA_NOT_READY");
    }

    [Fact]
    public async Task Database_outage_recovers_without_api_restart()
    {
        // Every test owns its container; stopping this one cannot disrupt another test.
        await MigrateAsync();
        await using var factory = Factory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        await AssertReadyAsync(client);
        var originalPort = database.Container.GetMappedPublicPort(5432);
        await database.Container.StopAsync();
        var elapsed = Stopwatch.StartNew();
        await AssertNotReadyAsync(client, "DATABASE_UNAVAILABLE");
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5));
        await database.Container.StartAsync();
        Assert.Equal(originalPort, database.Container.GetMappedPublicPort(5432));
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            using var response = await client.GetAsync("/health/ready", recovery.Token);
            if (response.StatusCode == HttpStatusCode.OK) break;
            await Task.Delay(100, recovery.Token);
        }
        await AssertReadyAsync(client);
    }

    [Fact]
    public async Task Blocked_history_read_times_out_and_releases_connection()
    {
        await MigrateAsync();
        await using var factory = Factory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await using var transaction = await admin.BeginTransactionAsync();
        await PostgresFixture.ExecuteAsync(admin, $"LOCK TABLE {History} IN ACCESS EXCLUSIVE MODE");
        var elapsed = Stopwatch.StartNew();
        await AssertNotReadyAsync(client, "DATABASE_UNAVAILABLE");
        Assert.InRange(elapsed.Elapsed.TotalSeconds, 2.5, 5);
        await transaction.RollbackAsync();
        await AssertReadyAsync(client);
    }

    [Fact]
    public async Task Caller_cancellation_releases_connection()
    {
        await MigrateAsync();
        await using var factory = Factory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await using var transaction = await admin.BeginTransactionAsync();
        await PostgresFixture.ExecuteAsync(admin, $"LOCK TABLE {History} IN ACCESS EXCLUSIVE MODE");
        using var cancellation = new CancellationTokenSource();
        var request = client.GetAsync("/health/ready", cancellation.Token);
        // Observe outside the lock transaction so pg_stat_activity snapshots
        // cannot remain cached across polling iterations.
        await using var observer = new NpgsqlConnection(database.AdminConnectionString);
        await observer.OpenAsync();
        using var polling = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var waiting = new NpgsqlCommand("SELECT EXISTS (SELECT FROM pg_stat_activity WHERE usename=@role AND wait_event_type='Lock')", observer);
            waiting.Parameters.AddWithValue("role", database.ApplicationRole);
            if (Equals(true, await waiting.ExecuteScalarAsync(polling.Token))) break;
            await Task.Delay(25, polling.Token);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
        // Release before the internal three-second budget: caller abort must
        // cancel the database work itself, not just the HTTP client task.
        using var release = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (true)
        {
            await using var waiting = new NpgsqlCommand("SELECT EXISTS (SELECT FROM pg_stat_activity WHERE usename=@role AND wait_event_type='Lock')", observer);
            waiting.Parameters.AddWithValue("role", database.ApplicationRole);
            if (Equals(false, await waiting.ExecuteScalarAsync(release.Token))) break;
            await Task.Delay(25, release.Token);
        }
        await transaction.RollbackAsync();
        await AssertReadyAsync(client);
    }
}
