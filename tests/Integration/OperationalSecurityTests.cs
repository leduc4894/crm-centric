using System.Collections.Concurrent;
using System.Text.Json;
using Integration.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed class OperationalSecurityTests
{
    private static void Safe(string text, params string[] secrets)
    {
        foreach (var secret in secrets.Concat(new[] { "NpgsqlException", "PostgresException", "InvalidOperationException", "Password=", " at Npgsql.", "Host=" }))
            Assert.False(text.Contains(secret, StringComparison.OrdinalIgnoreCase), "Sensitive diagnostic category detected.");
    }

    private static async Task<string> Problem(HttpResponseMessage response, int status, string reason)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        var body = json.RootElement;
        Assert.True(body.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(new[] { "type", "title", "status", "reasonToken", "correlationId" }), "Problem field set mismatch.");
        Assert.True(body.GetProperty("type").GetString() == "urn:crm:problem:" + reason.ToLowerInvariant().Replace('_', '-'), "Problem type mismatch.");
        Assert.True(body.GetProperty("title").GetString() == (status == 500 ? "Internal server error" : "Dependency unavailable"), "Problem title mismatch.");
        Assert.True(body.GetProperty("status").GetInt32() == status, "Problem status mismatch.");
        Assert.True(body.GetProperty("reasonToken").GetString() == reason, "Problem reason mismatch.");
        var correlation = response.Headers.GetValues("X-Correlation-Id").Single();
        Assert.True(Guid.TryParseExact(correlation, "N", out _), "Invalid correlation.");
        Assert.True(body.GetProperty("correlationId").GetString() == correlation, "Correlation mismatch.");
        return text;
    }

    private static ApiFactory Factory(string connection) => new(new Dictionary<string, string?> { ["ConnectionStrings:Platform"] = connection });

    [Fact]
    public async Task Unexpected_exception_returns_safe_problem()
    {
        var secret = Guid.NewGuid().ToString("N");
        var logs = new Capture();
        await using var baseFactory = Factory("Host=localhost;Database=unused;Username=unused");
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
            builder.ConfigureTestServices(services => services.AddScoped<DatabaseReadiness>(sp => new Throwing(sp.GetRequiredService<PlatformDbContext>(), secret)));
        });
        using var client = factory.CreateClient();
        string payload;
        try { using var response = await client.GetAsync("/health/ready"); payload = await Problem(response, 500, "INTERNAL_ERROR"); }
        catch { Assert.Fail("Unexpected exception escaped safe HTTP boundary."); return; }
        Safe(payload + logs.Text, secret);
        using var body = JsonDocument.Parse(payload);
        Assert.True(logs.Text.Contains(body.RootElement.GetProperty("correlationId").GetString()!), "Safe log correlation missing.");
        Assert.True(logs.Text.Contains("INTERNAL_ERROR"), "Required safe log category missing.");
        Assert.True(logs.Text.Contains("Microsoft.AspNetCore"), "Required safe log category missing.");
    }

    [Fact]
    public async Task Caller_cannot_set_server_correlation()
    {
        await using var database = new PostgresFixture();
        await database.InitializeAsync();
        await using var factory = Factory(database.ApplicationConnectionString);
        using var client = factory.CreateClient();
        var seen = new HashSet<string>();
        foreach (var forged in new[] { "forged", new string('x', 1024) })
        {
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            client.DefaultRequestHeaders.Add("X-Correlation-Id", forged);
            using var error = await client.GetAsync("/health/ready");
            await Problem(error, 503, "DATABASE_SCHEMA_NOT_READY");
            Assert.True(seen.Add(error.Headers.GetValues("X-Correlation-Id").Single()));
            using var live = await client.GetAsync("/health/live");
            Assert.Equal("no-store", live.Headers.CacheControl?.ToString());
            Assert.True(Guid.TryParseExact(live.Headers.GetValues("X-Correlation-Id").Single(), "N", out _));
        }
        Assert.Equal(0, (await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole)).ExitCode);
        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(200, (int)ready.StatusCode);
        Assert.Equal("no-store", ready.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_configuration_exits_without_listening(bool malformed)
    {
        var secret = Guid.NewGuid().ToString("N");
        var setting = malformed ? "UnrecognizedSetting=" + secret : null;
        foreach (var api in new[] { true, false })
        {
            var result = await MigratorProcess.RunAsync(setting, "unused", api);
            Safe(result.StandardOutput + result.StandardError, secret);
            Assert.False(result.StandardOutput.Contains("Now listening", StringComparison.OrdinalIgnoreCase), "Host listened with invalid configuration.");
            Assert.True(result.ExitCode != 0, "Invalid configuration exited successfully.");
            Diagnostic(result.StandardError, "DATABASE_CONFIGURATION_INVALID");
        }
    }

    [Fact]
    public async Task Wrong_password_has_safe_http_logs_and_runner_diagnostics()
    {
        await using var database = new PostgresFixture();
        await database.InitializeAsync();
        var secret = Guid.NewGuid().ToString("N");
        var connection = new NpgsqlConnectionStringBuilder(database.ApplicationConnectionString) { Password = secret }.ConnectionString;
        var logs = new Capture();
        await using var baseFactory = Factory(connection);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace)));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/ready");
        var payload = await response.Content.ReadAsStringAsync();
        Safe(payload + logs.Text, secret, connection, database.DatabaseName, database.ApplicationRole);
        await Problem(response, 503, "DATABASE_UNAVAILABLE");
        Assert.True(logs.Text.Contains(response.Headers.GetValues("X-Correlation-Id").Single()), "Safe log correlation missing.");
        Assert.True(logs.Text.Contains("DATABASE_UNAVAILABLE"), "Required safe log category missing.");
        Assert.True(logs.Text.Contains("Microsoft.AspNetCore"), "Required safe log category missing.");
        var result = await MigratorProcess.RunAsync(connection, database.ApplicationRole);
        Safe(result.StandardOutput + result.StandardError, secret, connection, database.DatabaseName, database.ApplicationRole);
        Assert.True(result.ExitCode != 0);
        Diagnostic(result.StandardError, "DATABASE_MIGRATION_FAILED");
    }

    [Fact]
    public void Database_options_diagnostics_do_not_expose_connection()
    {
        var secret = Guid.NewGuid().ToString("N");
        Safe(DatabaseOptions.Parse("Host=localhost;Database=unused;Username=unused;Password=" + secret).ToString(), secret);
    }

    private static void Diagnostic(string text, string reason)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            Assert.True(json.RootElement.GetProperty("reasonToken").GetString() == reason, "Diagnostic reason mismatch.");
            Assert.True(Guid.TryParseExact(json.RootElement.GetProperty("correlationId").GetString(), "N", out _), "Diagnostic correlation mismatch.");
            Assert.True(json.RootElement.GetProperty("service").GetString() is "platform-api" or "platform-migrator", "Diagnostic service mismatch.");
            Assert.Equal("Error", json.RootElement.GetProperty("LogLevel").GetString());
        }
        catch { Assert.Fail("Invalid safe process diagnostic."); }
    }

    private sealed class Throwing(PlatformDbContext context, string secret) : DatabaseReadiness(context)
    {
        public override Task<DatabaseReadinessResult> CheckAsync(CancellationToken cancellationToken) => throw new InvalidOperationException(secret);
    }

    private sealed class Capture : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> entries = new();
        public string Text => string.Join("\n", entries);
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, entries);
        public void Dispose() { }
        private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => entries.Enqueue(category + " " + formatter(state, exception) + " " + exception);
        }
    }
}
