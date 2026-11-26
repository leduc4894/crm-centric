using System.Text.Json;
using Integration.Fixtures;
using Xunit;

namespace Integration.Compose;

public sealed class ComposeStageDiagnosticsTests
{
    [Theory]
    [InlineData("COMPOSE_TLS_STARTUP_FAILED")]
    [InlineData("COMPOSE_TLS_DISCOVERY_FAILED")]
    [InlineData("COMPOSE_TLS_MANAGEMENT_FAILED")]
    public void Failure_reports_only_stage_and_correlation(string stage)
    {
        var raw = "private-diagnostic-sentinel";
        var result = new ComposeFixture.Result(1, raw);
        var exception = Record.Exception(() => ComposeFixture.Success(result, stage));
        Assert.NotNull(exception);
        Assert.Contains("reasonToken=" + stage, exception.Message);
        Assert.Matches(@"correlationId=[0-9a-f]{32}", exception.Message);
        Assert.DoesNotContain(raw, exception.Message);
    }

    [Fact]
    public void Unrelated_reason_cannot_forge_a_timeout_and_success_stays_successful()
    {
        var output = JsonSerializer.Serialize(new { reasonToken = "OTHER, LOCAL_START_TIMEOUT" });
        var exception = Record.Exception(() => ComposeFixture.Success(new(1, output), "COMPOSE_TLS_STARTUP_FAILED"));
        Assert.NotNull(exception);
        Assert.DoesNotContain("cause=LOCAL_START_TIMEOUT", exception.Message);
        Assert.Null(Record.Exception(() => ComposeFixture.Success(new(0, output), "COMPOSE_TLS_STARTUP_FAILED")));
    }

    [Fact]
    public void Startup_timeout_reason_is_safe_and_preserved()
    {
        var output = JsonSerializer.Serialize(new { reasonToken = "LOCAL_START_TIMEOUT", secret = "private-sentinel" });
        var exception = Record.Exception(() => ComposeFixture.Success(new(1, output), "COMPOSE_TLS_STARTUP_FAILED"));
        Assert.NotNull(exception);
        Assert.Contains("reasonToken=COMPOSE_TLS_STARTUP_FAILED", exception.Message);
        Assert.Contains("cause=LOCAL_START_TIMEOUT", exception.Message);
        Assert.DoesNotContain("private-sentinel", exception.Message);
    }
}
