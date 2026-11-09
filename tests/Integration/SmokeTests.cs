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
    [InlineData(true)]
    [InlineData(false)]
    public async Task Script_rejects_foreign_listener_and_bounds_term_ignoring_cleanup(bool foreignListener)
    {
        // The old script accepts the foreign listener, or hangs forever on TERM.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CrmPlatform.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(Path.GetTempPath(), "crm-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = new CancellationTokenSource();
        var responder = Task.Run(async () =>
        {
            try
            {
                while (foreignListener)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    var stream = client.GetStream();
                    var buffer = new byte[4096];
                    var count = await stream.ReadAsync(buffer, stop.Token);
                    var request = System.Text.Encoding.ASCII.GetString(buffer, 0, count);
                    var body = request.Contains("/health/ready") ? "{\"status\":\"ready\"}" : "{\"status\":\"live\"}";
                    var response = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(response), stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            // Stop can race the next Accept call after cancellation is requested.
            catch (InvalidOperationException) when (stop.IsCancellationRequested) { }
        });
        if (!foreignListener) listener.Stop();
        var fakeDotnet = Path.Combine(directory, "dotnet");
        await File.WriteAllTextAsync(fakeDotnet, """
            #!/usr/bin/python3
            import http.server, os, signal, sys, time
            if 'Platform.Migrator' in sys.argv[1]:
                sys.exit(0)
            signal.signal(signal.SIGTERM, signal.SIG_IGN)
            if os.environ['FIXTURE_FOREIGN'] == 'true':
                time.sleep(60) # Candidate remains alive but never owns the occupied port.
            else:
                class Handler(http.server.BaseHTTPRequestHandler):
                    def do_GET(self):
                        body = b'{"status":"ready"}' if self.path == '/health/ready' else b'{"status":"live"}'
                        self.send_response(200)
                        self.end_headers()
                        self.wfile.write(body)
                    def log_message(self, *args): pass
                http.server.HTTPServer(('127.0.0.1', int(os.environ['SMOKE_PORT'])), Handler).serve_forever()
            """);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux smoke fixture requires /proc.");
        File.SetUnixFileMode(fakeDotnet, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var start = new ProcessStartInfo("bash")
        {
            WorkingDirectory = root.FullName, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("scripts/backend-smoke.sh");
        start.Environment["PATH"] = directory + Path.PathSeparator + start.Environment["PATH"];
        start.Environment["SMOKE_MIGRATION_CONNECTION"] = "disposable-fixture";
        start.Environment["SMOKE_APPLICATION_CONNECTION"] = "disposable-fixture";
        start.Environment["Database__ApplicationRole"] = "disposable-fixture";
        start.Environment["SMOKE_PORT"] = port.ToString();
        start.Environment["FIXTURE_FOREIGN"] = foreignListener ? "true" : "false";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            try { await process.WaitForExitAsync(budget.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                Assert.Fail("Smoke cleanup exceeded finite test budget for TERM-ignoring child.");
            }
            var output = await stdout;
            var errors = await stderr;
            if (foreignListener)
            {
                Assert.True(process.ExitCode != 0, "Smoke accepted another process's healthy listener.");
                Assert.DoesNotContain("SMOKE_PASS", output);
                Assert.StartsWith("reasonToken=BACKEND_SMOKE_FAILED correlationId=", errors);
                using var http = new HttpClient();
                Assert.Equal("{\"status\":\"ready\"}", await http.GetStringAsync($"http://127.0.0.1:{port}/health/ready"));
            }
            else
            {
                Assert.Equal(0, process.ExitCode);
                Assert.Equal("SMOKE_PASS live=200 ready=200", output.Trim());
                Assert.Empty(errors);
                using var probe = new TcpClient();
                await Assert.ThrowsAsync<SocketException>(() => probe.ConnectAsync(IPAddress.Loopback, port));
            }
        }
        finally
        {
            stop.Cancel();
            listener.Stop();
            await responder;
            Directory.Delete(directory, recursive: true);
        }
    }

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
