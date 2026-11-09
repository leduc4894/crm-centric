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
    public ComposeFixture()
    {
        using var api = new TcpListener(IPAddress.Loopback, 0);
        using var idp = new TcpListener(IPAddress.Loopback, 0);
        api.Start(); idp.Start();
        ApiPort = ((IPEndPoint)api.LocalEndpoint).Port;
        IdpPort = ((IPEndPoint)idp.LocalEndpoint).Port;
    }
    public record Result(int ExitCode, string Output)
    {
        public string SafeDiagnostic => string.Join(", ", Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        { try { return JsonDocument.Parse(line).RootElement.TryGetProperty("reasonToken", out var token) ? token.GetString() : "status"; } catch (JsonException) { return "non-json diagnostic"; } }));
    }
    public static void Success(Result result) => Assert.True(result.ExitCode == 0, result.SafeDiagnostic);
    private static string FindRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "CrmPlatform.sln"))) d = d.Parent; return d?.FullName ?? throw new InvalidOperationException(); }
    public async Task<Result> Command(string action)
    {
        var p = new ProcessStartInfo("/bin/bash") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        p.ArgumentList.Add("scripts/dev-" + action + ".sh");
        p.Environment["PATH"] = "/usr/local/bin:/usr/bin:/bin";
        p.Environment.Remove("DOTNET_ROOT");
        p.Environment["LOCAL_STACK_PROJECT"] = Project; p.Environment["LOCAL_STACK_STATE_DIR"] = State;
        p.Environment["LOCAL_API_PORT"] = ApiPort.ToString(); p.Environment["LOCAL_IDP_PORT"] = IdpPort.ToString();
        return await Run(p, 1200);
    }
    public static async Task<Result> Docker(params string[] arguments)
    {
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
