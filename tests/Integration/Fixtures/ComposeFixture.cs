using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Xunit;

namespace Integration.Fixtures;

public sealed class ComposeFixture : IAsyncDisposable
{
    public string Root { get; } = FindRoot();
    public string Project { get; } = "crm-test-" + Guid.NewGuid().ToString("N");
    public string State { get; } = Path.Combine(Path.GetTempPath(), "crm-compose-" + Guid.NewGuid().ToString("N"));
    public int ApiPort { get; }
    public int IdpPort { get; }
    public ComposeFixture(int? apiPort = null, int? idpPort = null)
    {
        using var api = new TcpListener(IPAddress.Loopback, 0);
        using var idp = new TcpListener(IPAddress.Loopback, 0);
        api.Start(); idp.Start();
        ApiPort = apiPort ?? ((IPEndPoint)api.LocalEndpoint).Port;
        IdpPort = idpPort ?? ((IPEndPoint)idp.LocalEndpoint).Port;
    }
    public record Result(int ExitCode, string Output)
    {
        public string SafeDiagnostic => string.Join(", ", Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        { try { using var json = JsonDocument.Parse(line); return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("reasonToken", out var token) && token.ValueKind == JsonValueKind.String ? token.GetString() : "status"; } catch (JsonException) { return "non-json diagnostic"; } }));
    }
    public static void Success(Result result, string stage = "operation") => Assert.True(result.ExitCode == 0, stage + ": " + result.SafeDiagnostic);
    private static string FindRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "CrmPlatform.sln"))) d = d.Parent; return d?.FullName ?? throw new InvalidOperationException(); }
    public ProcessStartInfo CommandInfo(string action, string? state = null, string? root = null, int? startTimeout = null)
    {
        var p = new ProcessStartInfo("/bin/bash") { WorkingDirectory = root ?? Root, RedirectStandardOutput = true, RedirectStandardError = true };
        p.ArgumentList.Add("scripts/dev-" + action + ".sh");
        p.Environment["PATH"] = "/usr/local/bin:/usr/bin:/bin";
        p.Environment.Remove("DOTNET_ROOT");
        p.Environment["LOCAL_STACK_PROJECT"] = Project; p.Environment["LOCAL_STACK_STATE_DIR"] = state ?? State;
        p.Environment["LOCAL_API_PORT"] = ApiPort.ToString(); p.Environment["LOCAL_IDP_PORT"] = IdpPort.ToString();
        if (startTimeout is not null) p.Environment["LOCAL_START_TIMEOUT_SECONDS"] = startTimeout.ToString();
        return p;
    }
    public List<string> Captured { get; } = [];
    public async Task<Result> Command(string action, string? state = null, string? root = null, int? startTimeout = null)
    {
        var result = await Run(CommandInfo(action, state, root, startTimeout), 1200);
        Captured.Add(result.Output);
        return result;
    }
    public async Task<Result> Compose(params string[] arguments)
    {
        var p = CommandInfo("up"); p.FileName = "docker"; p.ArgumentList.Clear();
        foreach (var line in File.ReadAllLines(Path.Combine(Root, "infra/compose/images.lock.env")).Where(l => l.Length > 0 && !l.StartsWith('#')))
        { var parts = line.Split('=', 2); p.Environment[parts[0]] = parts[1]; }
        p.Environment["LOCAL_UID"] = (await Run(new ProcessStartInfo("id", "-u") { RedirectStandardOutput = true, RedirectStandardError = true }, 5)).Output.Trim();
        p.Environment["LOCAL_GID"] = (await Run(new ProcessStartInfo("id", "-g") { RedirectStandardOutput = true, RedirectStandardError = true }, 5)).Output.Trim();
        foreach (var arg in new[] { "compose", "--project-name", Project, "--file", Path.Combine(Root, "compose.yaml") }.Concat(arguments)) p.ArgumentList.Add(arg);
        var result = await Run(p, 120); Captured.Add(result.Output); return result;
    }
    public async Task<string> Logs()
    {
        var logs = new List<string>();
        foreach (var service in new[] { "crm-db", "crm-bootstrap", "crm-migrate", "crm-api", "idp-db", "keycloak" })
        {
            var ids = await Docker("ps", "-aq", "--filter", "label=com.docker.compose.project=" + Project, "--filter", "label=com.docker.compose.service=" + service);
            foreach (var id in ids.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            { var result = await Docker("logs", id); Assert.Equal(0, result.ExitCode); logs.Add(result.Output); }
        }
        return string.Join("\n", logs);
    }
    public static void Failure(Result result, params string[] tokens)
    {
        Assert.NotEqual(0, result.ExitCode);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var json = JsonDocument.Parse(lines.Last());
        Assert.Contains(json.RootElement.GetProperty("reasonToken").GetString(), tokens);
        Assert.True(Guid.TryParseExact(json.RootElement.GetProperty("correlationId").GetString(), "N", out _), "Safe correlation missing.");
    }
    public async Task Health(System.Net.HttpStatusCode ready)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        using var liveResponse = await client.GetAsync($"http://127.0.0.1:{ApiPort}/health/live");
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        var clock = Stopwatch.StartNew();
        using var response = await client.GetAsync($"http://127.0.0.1:{ApiPort}/health/ready");
        Assert.Equal(ready, response.StatusCode);
        if (ready == HttpStatusCode.ServiceUnavailable)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "Readiness exceeded its budget.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var correlation = response.Headers.GetValues("X-Correlation-Id").Single();
            Assert.Equal(correlation, body.RootElement.GetProperty("correlationId").GetString());
            Assert.Contains(correlation, await Logs());
        }
    }
    public async Task WaitSmoke()
    {
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while ((deadline - DateTime.UtcNow).TotalSeconds > 1)
        {
            var result = await Command("smoke", startTimeout: Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalSeconds));
            if (result.ExitCode == 0) return;
            Failure(result, "LOCAL_IDP_NOT_READY");
            await Task.Delay(500);
        }
        Assert.Fail("Full IdP readiness did not recover within the 180-second deadline.");
    }
    public async Task WaitHealthy(string service)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if ((await Inspect(service)).GetProperty("State").GetProperty("Health").GetProperty("Status").GetString() == "healthy") return;
            await Task.Delay(300);
        }
        Assert.Fail("Dependency did not recover within deadline.");
    }
    public static async Task<Result> Docker(params string[] arguments)
    {
        if (arguments[0] == "run" && Environment.GetEnvironmentVariable("CI_IMAGE_EVIDENCE") is not null)
        {
            var capture = await ContainerImages.Docker(false, arguments);
            return new(capture.ExitCode, capture.Output + capture.Error);
        }
        var p = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) p.ArgumentList.Add(arg);
        return await Run(p, 120);
    }
    private static async Task<Result> Run(ProcessStartInfo info, int timeout)
    {
        using var p = Process.Start(info)!; var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(timeout)); }
        catch { p.Kill(true); throw; }
        return new(p.ExitCode, await stdout + await stderr);
    }
    public async Task<string> Container(string service)
    {
        var r = await Docker("ps", "-aq", "--filter", "label=com.docker.compose.project=" + Project, "--filter", "label=com.docker.compose.service=" + service);
        Assert.Equal(0, r.ExitCode); return Assert.Single(r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
    public async Task<JsonElement> Inspect(string service)
    {
        var r = await Docker("inspect", await Container(service)); Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Output).RootElement[0].Clone();
    }
    public async Task<string> Sql(string sql, string role = "postgres", string database = "crm", string service = "crm-db")
    {
        var r = await Docker("exec", await Container(service), "psql", "-U", role, "-d", database, "-At", "-v", "ON_ERROR_STOP=1", "-c", sql);
        Assert.True(r.ExitCode == 0, "SQL inspection failed."); return r.Output.Trim();
    }
    public async Task SqlInput(string sql)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "exec", "-i", await Container("crm-db"), "psql", "-U", "postgres", "-d", "crm", "-v", "ON_ERROR_STOP=1" }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(sql); process.StandardInput.Close();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch { process.Kill(true); throw; }
        Captured.Add(await output + await error);
        Assert.True(process.ExitCode == 0, "Private SQL operation failed.");
    }
    public async Task Ready()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"http://127.0.0.1:{ApiPort}/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"http://127.0.0.1:{ApiPort}/health/ready")).StatusCode);
        Success(await Command("smoke"));
        foreach (var s in new[] { "crm-db", "idp-db", "crm-api", "keycloak" }) Assert.Equal("healthy", (await Inspect(s)).GetProperty("State").GetProperty("Health").GetProperty("Status").GetString());
        foreach (var s in new[] { "crm-bootstrap", "crm-migrate" }) Assert.Equal(0, (await Inspect(s)).GetProperty("State").GetProperty("ExitCode").GetInt32());
        Assert.Equal("1", await Sql("SELECT count(*) FROM platform.\"__EFMigrationsHistory\""));
    }
    public async ValueTask DisposeAsync()
    {
        // Only random test-owned labeled resources are eligible for destructive cleanup.
        foreach (var kind in new[] { "container", "network", "volume" })
        {
            var listed = await Docker(kind, "ls", kind == "container" ? "-aq" : "-q", "--filter", "label=com.docker.compose.project=" + Project);
            Assert.Equal(0, listed.ExitCode);
            foreach (var id in listed.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Assert.Equal(0, (await Docker(kind == "container" ? [kind, "rm", "-f", id] : [kind, "rm", id])).ExitCode);
        }
        var images = await Docker("image", "ls", "--format", "{{.Repository}}", "--filter", "reference=" + Project + "-*");
        Assert.Equal(0, images.ExitCode);
        foreach (var image in images.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Distinct())
        {
            Assert.Contains(image, new[] { Project + "-api", Project + "-migrator", Project + "-postgres", Project + "-keycloak" });
            Assert.Equal(0, (await Docker("image", "rm", image)).ExitCode);
        }
        if (Directory.Exists(State)) Directory.Delete(State, true);
        File.Delete(State + ".stack.json");
    }
}
