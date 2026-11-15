using Integration.Fixtures;
using Xunit;

namespace Integration;

public sealed class ImageEvidenceSecretTests
{
    [Fact]
    public void Unreadable_bound_fixture_retains_snapshot_without_changing_permissions()
    {
        using var fixture = new LocalSecretFixture();
        var sentinel = File.ReadAllText(fixture.ApplicationFile).Trim();
        File.SetUnixFileMode(fixture.ApplicationFile, UnixFileMode.None);
        var args = new[] { "run", "--mount", $"type=bind,source={fixture.ApplicationFile},target=/run/secrets/platform,readonly", "fixture" };
        var snapshots = new Dictionary<string, string> { [fixture.ApplicationFile] = sentinel };
        var values = ContainerImages.FixtureSecretValues(args, snapshots);
        Assert.True(values.Contains(sentinel), "CI_SECRET_EXTRACTION_INCOMPLETE");
        Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(fixture.ApplicationFile));
    }

    [Fact]
    public void Unknown_unreadable_fixture_closes_with_safe_reason_and_correlation()
    {
        using var fixture = new LocalSecretFixture();
        var sentinel = File.ReadAllText(fixture.ApplicationFile).Trim();
        File.SetUnixFileMode(fixture.ApplicationFile, UnixFileMode.None);
        var failure = Assert.Throws<InvalidOperationException>(() => ContainerImages.FixtureSecretValues([
            "run", "--mount", $"type=bind,source={fixture.ApplicationFile},target=/run/secrets/platform,readonly", "fixture"]));
        Assert.StartsWith("CI_SECRET_EXTRACTION_INCOMPLETE correlation=", failure.Message);
        Assert.True(Guid.TryParseExact(failure.Message.Split('=')[1], "N", out _));
        Assert.False(failure.Message.Contains(sentinel), "CI_SECRET_GUARD_LEAK");
    }

    [Fact]
    public void Direct_docker_mount_and_environment_values_reach_private_guard()
    {
        using var fixture = new LocalSecretFixture();
        var sentinel = File.ReadAllText(fixture.ApplicationFile).Trim();
        var environmentSentinel = File.ReadAllText(fixture.MigratorFile).Trim();
        var values = ContainerImages.FixtureSecretValues([
            "run", "--mount", $"type=bind,source={fixture.ApplicationFile},target=/run/secrets/password,readonly",
            "--env", "FIXTURE_TOKEN=" + environmentSentinel,
            "--env=FIXTURE_PASSWORD=" + environmentSentinel,
            "-e", "PLATFORM_CONNECTION=Host=localhost;Username=fixture;Password=" + environmentSentinel]);
        Assert.True(values.Contains(sentinel), "CI_SECRET_EXTRACTION_INCOMPLETE");
        Assert.True(values.Contains(environmentSentinel), "CI_SECRET_EXTRACTION_INCOMPLETE");
        Assert.True(values.Any(value => value.StartsWith("Host=localhost;", StringComparison.Ordinal)), "CI_SECRET_EXTRACTION_INCOMPLETE");
    }

    [Theory]
    [InlineData("api-fixture")]
    [InlineData("migrator-fixture")]
    public void Argument_override_fixture_sentinel_reaches_private_guard(string image)
    {
        using var fixture = new LocalSecretFixture();
        var sentinel = Guid.NewGuid().ToString("N");
        var values = ContainerImages.FixtureSecretValues([
            "run", "--rm", "--mount", $"type=bind,source={fixture.ApplicationFile},target=/run/secrets/platform,readonly",
            "--env", "PLATFORM_CONNECTION_FILE=/run/secrets/platform", image,
            "--ConnectionStrings:Platform=" + sentinel]);
        Assert.True(values.Contains(sentinel), "CI_SECRET_EXTRACTION_INCOMPLETE");
    }

    [Fact]
    public void Secretless_docker_command_has_no_invented_sentinel()
    {
        Assert.True(ContainerImages.FixtureSecretValues(["run", "--env", "KC_HOSTNAME=http://localhost:8080", "fixture"]).Length == 0,
            "CI_SECRET_EXTRACTION_INCOMPLETE");
    }
}
