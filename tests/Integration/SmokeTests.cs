using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Integration.Fixtures;
using Npgsql;
using Xunit;

namespace Integration;

public sealed class SmokeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Script_checks_real_api_and_cleans_up_on_success_or_readiness_failure(bool invalidApplication)
    {
        await using var database = new PostgresFixture();
        await database.InitializeAsync();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CrmPlatform.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        using var reservation = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reservation.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)reservation.LocalEndPoint!).Port;
        reservation.Close();
        var start = new ProcessStartInfo("bash")
        {
            WorkingDirectory = root.FullName, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("scripts/backend-smoke.sh");
        start.Environment.Remove("ConnectionStrings__Platform");
        start.Environment["SMOKE_MIGRATION_CONNECTION"] = database.MigrationConnectionString;
        start.Environment["SMOKE_APPLICATION_CONNECTION"] = invalidApplication ? new NpgsqlConnectionStringBuilder(database.ApplicationConnectionString) { Password = Guid.NewGuid().ToString("N") }.ConnectionString : database.ApplicationConnectionString;
        start.Environment["Database__ApplicationRole"] = database.ApplicationRole;
        start.Environment["SMOKE_PORT"] = port.ToString();
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Smoke exceeded test budget.");
        }
        var output = await stdout;
        var errors = await stderr;
        if (invalidApplication)
        {
            Assert.True(process.ExitCode != 0, "Smoke accepted unavailable application database.");
            Assert.True(output.Length == 0, "Smoke claimed success on readiness failure.");
            Assert.True(errors.StartsWith("reasonToken=BACKEND_SMOKE_FAILED correlationId=", StringComparison.Ordinal), "Missing safe failure diagnostic.");
            Assert.True(Guid.TryParse(errors.Trim().Split('=')[2], out _), "Missing correlation GUID.");
        }
        else
        {
            Assert.True(process.ExitCode == 0, "Smoke script did not succeed (captured diagnostics withheld).");
            Assert.True(output.Trim() == "SMOKE_PASS live=200 ready=200", "Missing health evidence.");
            Assert.True(errors.Length == 0, "Unexpected smoke stderr (withheld).");
        }
        Assert.False(output.Contains(database.MigrationConnectionString) || output.Contains(database.ApplicationConnectionString), "Sensitive output.");
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connected = false;
        try { await probe.ConnectAsync(IPAddress.Loopback, port); connected = true; }
        catch (SocketException) { }
        Assert.False(connected, "Smoke left a listener running.");
    }
}
