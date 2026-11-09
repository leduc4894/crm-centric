using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Integration.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

[CollectionDefinition("Console diagnostics", DisableParallelization = true)]
public sealed class ConsoleDiagnosticsCollection;

[Collection("Console diagnostics")]
public sealed class StructuredLoggingTests
{
    // These tests catch missing structured state, invented trace IDs, and unsafe framework output.
    [Fact]
    public async Task Error_json_has_http_correlation_and_real_activity()
    {
        var result = await RequestAsync();
        AssertError(result);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("00-0123456789abcdef0123456789abcdef-0123456789abcdef-01")]
    public async Task Forged_correlation_and_malformed_trace_are_safe(string traceparent)
    {
        var result = await RequestAsync(traceparent: traceparent);
        AssertError(result);
        Assert.NotEqual("caller-controlled", result.Correlation);
        if (traceparent.StartsWith("00-", StringComparison.Ordinal))
            Assert.Equal("0123456789abcdef0123456789abcdef", result.Activity.TraceId.ToHexString());
    }

    [Fact]
    public async Task Query_headers_and_secret_sentinels_are_absent()
    {
        var sentinel = Guid.NewGuid().ToString("N");
        var result = await RequestAsync(sentinel: sentinel);
        Assert.False(result.Raw.Contains(sentinel, StringComparison.Ordinal), "Sensitive sentinel diagnostic detected.");
        AssertError(result);
        Assert.Contains(result.Entries, entry => entry.GetProperty("Category").GetString()!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Entries, entry => entry.GetProperty("Category").GetString() == "Microsoft.AspNetCore.Hosting.Diagnostics");
    }

    [Fact]
    public async Task Cli_result_shape_stays_compatible()
    {
        foreach (var api in new[] { false, true })
        {
            var result = await MigratorProcess.RunAsync(null, null, api);
            Assert.Equal(1, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput), "Unexpected process stdout.");
            var entry = Parse(result.StandardError);
            Assert.Equal("DATABASE_CONFIGURATION_INVALID", entry.GetProperty("reasonToken").GetString());
            Assert.True(Guid.TryParseExact(entry.GetProperty("correlationId").GetString(), "N", out _));
            Assert.Equal(api ? "crm-api" : "crm-migrate", entry.GetProperty("service").GetString());
            Assert.Equal("configuration_failed", entry.GetProperty("eventName").GetString());
            AssertEnvelope(entry, "Error");
            AssertTrace(entry.GetProperty("traceId").GetString(), entry.GetProperty("spanId").GetString());
        }

        await using var database = new PostgresFixture();
        await database.InitializeAsync();
        var migrated = await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole);
        Assert.Equal(0, migrated.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(migrated.StandardError), "Unexpected process stderr.");
        var success = Parse(migrated.StandardOutput);
        Assert.Equal("migrated", success.GetProperty("status").GetString());
        Assert.Equal("crm-migrate", success.GetProperty("service").GetString());
        Assert.Equal("migration_completed", success.GetProperty("eventName").GetString());
        AssertEnvelope(success, "Information");
        AssertTrace(success.GetProperty("traceId").GetString(), success.GetProperty("spanId").GetString());
    }

    private static void AssertError(Captured result)
    {
        var entry = Assert.Single(result.Entries, entry => entry.GetProperty("Category").GetString() == "Platform.OperationalErrors");
        AssertEnvelope(entry, "Error");
        Assert.Equal("crm-api", Field(entry, "service"));
        Assert.Equal("dependency_failed", Field(entry, "eventName"));
        Assert.Equal("INTERNAL_ERROR", Field(entry, "reasonToken"));
        var loggedCorrelationId = Field(entry, "correlationId");
        var loggedTraceId = Field(entry, "traceId");
        var loggedSpanId = Field(entry, "spanId");
        Assert.Equal(result.Correlation, loggedCorrelationId);
        Assert.Equal(result.Activity.TraceId.ToHexString(), loggedTraceId);
        Assert.Equal(result.Activity.SpanId.ToHexString(), loggedSpanId);
        AssertTrace(loggedTraceId, loggedSpanId);
    }

    private static void AssertEnvelope(JsonElement entry, string severity)
    {
        Assert.True(entry.TryGetProperty("Timestamp", out var timestamp), "Missing UTC timestamp.");
        Assert.EndsWith("Z", timestamp.GetString());
        Assert.True(DateTimeOffset.TryParse(timestamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) && parsed.Offset == TimeSpan.Zero, "Invalid UTC timestamp.");
        Assert.Equal(severity, entry.GetProperty("LogLevel").GetString());
    }

    private static void AssertTrace(string? trace, string? span)
    {
        Assert.Matches("^[0-9a-f]{32}$", trace!);
        Assert.Matches("^[0-9a-f]{16}$", span!);
        Assert.NotEqual(new string('0', 32), trace);
        Assert.NotEqual(new string('0', 16), span);
    }

    // Read application state independently of the built-in formatter's envelope/casing.
    private static string? Field(JsonElement entry, string name)
    {
        if (entry.TryGetProperty("State", out var state) && state.TryGetProperty(name, out var value)) return value.GetString();
        if (entry.TryGetProperty("Scopes", out var scopes))
            foreach (var scope in scopes.EnumerateArray().Reverse())
                if (scope.ValueKind == JsonValueKind.Object && scope.TryGetProperty(name, out value)) return value.GetString();
        Assert.Fail("Missing owned diagnostic field: " + name);
        return null;
    }

    private static JsonElement Parse(string text)
    {
        try { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
        catch (JsonException) { Assert.Fail("Non-JSON diagnostic category detected."); return default; }
    }

    private static async Task<Captured> RequestAsync(string? traceparent = null, string? sentinel = null)
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);
        using var capture = new ConsoleCapture();
        string correlation;
        await using (var baseFactory = new ApiFactory(new Dictionary<string, string?> { ["ConnectionStrings:Platform"] = "Host=localhost;Database=unused;Username=unused" }))
        await using (var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Information));
            builder.ConfigureTestServices(services => services.AddScoped<DatabaseReadiness>(sp => new ThrowingReadiness(sp.GetRequiredService<PlatformDbContext>(), sentinel ?? "controlled")));
        }))
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health/ready" + (sentinel is null ? "" : "?secret=" + sentinel));
            request.Headers.Add("X-Correlation-Id", "caller-controlled");
            if (traceparent is not null) request.Headers.TryAddWithoutValidation("traceparent", traceparent);
            if (sentinel is not null)
            {
                request.Headers.Add("Authorization", "Bearer " + sentinel);
                request.Headers.Add("Cookie", "session=" + sentinel);
            }
            using var response = await client.SendAsync(request);
            Assert.Equal(500, (int)response.StatusCode);
            correlation = response.Headers.GetValues("X-Correlation-Id").Single();
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(correlation, problem.RootElement.GetProperty("correlationId").GetString());
        }
        var raw = capture.Text;
        var entries = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(Parse).ToArray();
        Assert.NotEmpty(entries);
        var observedActivity = Assert.Single(activities, activity => activity.OperationName == "Microsoft.AspNetCore.Hosting.HttpRequestIn");
        return new Captured(raw, entries, correlation, observedActivity);
    }

    private sealed record Captured(string Raw, JsonElement[] Entries, string Correlation, Activity Activity);
    private sealed class ThrowingReadiness(PlatformDbContext context, string sentinel) : DatabaseReadiness(context)
    {
        public override Task<DatabaseReadinessResult> CheckAsync(CancellationToken cancellationToken) => throw new InvalidOperationException(sentinel);
    }

    private sealed class ConsoleCapture : IDisposable
    {
        private readonly TextWriter previous = Console.Out;
        private readonly StringWriter output = new();
        public ConsoleCapture() => Console.SetOut(TextWriter.Synchronized(output));
        public string Text => output.ToString();
        public void Dispose() { Console.SetOut(previous); output.Dispose(); }
    }
}
