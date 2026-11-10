using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text;
using Integration.Fixtures;
using Npgsql;
using Xunit;

namespace Integration.Compose;

[Collection("Compose failures")]
[Trait("Category", "Compose")]
public sealed class LocalStackSecretTests
{
    // Catches file secrets entering logs/config/history/layers in both healthy
    // operation and actual dependency/configuration failures.
    [Fact]
    public async Task Secrets_and_request_sentinels_are_absent_across_success_and_failure()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        await using var stack = new ComposeFixture(); ComposeFixture.Success(await stack.Command("up"));
        var original = LocalStackFailureTests.Snapshot(stack);
        var sentinels = original.Where(p => p.Key != "manifest.json").Select(p => Encoding.UTF8.GetString(p.Value)).ToList();
        var requestSecret = "request-" + Guid.NewGuid().ToString("N"); sentinels.Add(requestSecret);
        await Audit(stack, sentinels, "bootstrap_completed", "migration_completed");
        await AuditImages(stack, sentinels);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{stack.ApiPort}/health/live?token={requestSecret}");
        request.Headers.Add("Authorization", "Bearer " + requestSecret); request.Headers.Add("Cookie", "session=" + requestSecret); request.Headers.Add("X-Sentinel", requestSecret);
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Audit(stack, sentinels);

        var connectionFile = Path.Combine(stack.State, "crm_app_connection");
        var wrongPassword = "bad-auth-" + Guid.NewGuid().ToString("N"); sentinels.Add(wrongPassword);
        var wrongConnection = new NpgsqlConnectionStringBuilder(Encoding.UTF8.GetString(original["crm_app_connection"])) { Password = wrongPassword }.ConnectionString;
        sentinels.Add(wrongConnection); File.WriteAllText(connectionFile, wrongConnection);
        ComposeFixture.Success(await ComposeFixture.Docker("restart", await stack.Container("crm-api")));
        await WaitListening(stack); await stack.Health(HttpStatusCode.ServiceUnavailable);
        await Audit(stack, sentinels, "DATABASE_UNAVAILABLE");
        File.WriteAllBytes(connectionFile, original["crm_app_connection"]);
        ComposeFixture.Success(await ComposeFixture.Docker("restart", await stack.Container("crm-api"))); await stack.WaitHealthy("crm-api");

        await stack.Sql("ALTER ROLE crm_app CREATEDB");
        ComposeFixture.Failure(await stack.Command("up"), "LOCAL_BOOTSTRAP_FAILED");
        await Audit(stack, sentinels, "LOCAL_SECRET_STATE_MISMATCH", "LOCAL_BOOTSTRAP_FAILED");
        await stack.Sql("ALTER ROLE crm_app NOCREATEDB"); ComposeFixture.Success(await stack.Command("up"));

        await stack.Sql("DELETE FROM platform.\"__EFMigrationsHistory\"; ALTER TABLE platform.\"__EFMigrationsHistory\" ADD CONSTRAINT fixture_block_history CHECK(false) NOT VALID");
        ComposeFixture.Failure(await stack.Command("up"), "LOCAL_MIGRATION_FAILED");
        await Audit(stack, sentinels, "DATABASE_MIGRATION_FAILED", "LOCAL_MIGRATION_FAILED");
        await stack.Sql("ALTER TABLE platform.\"__EFMigrationsHistory\" DROP CONSTRAINT fixture_block_history"); ComposeFixture.Success(await stack.Command("up"));

        var idpPassword = Path.Combine(stack.State, "idp_db_password");
        File.SetUnixFileMode(idpPassword, UnixFileMode.None);
        ComposeFixture.Success(await ComposeFixture.Docker("restart", await stack.Container("keycloak")));
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((await stack.Inspect("keycloak")).GetProperty("State").GetProperty("Running").GetBoolean() && DateTime.UtcNow < deadline) await Task.Delay(200);
        Assert.False((await stack.Inspect("keycloak")).GetProperty("State").GetProperty("Running").GetBoolean(), "IdP accepted an unreadable startup credential.");
        await Audit(stack, sentinels, "LOCAL_CONFIGURATION_INVALID");
        File.SetUnixFileMode(idpPassword, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        ComposeFixture.Failure(await stack.Command("smoke"), "LOCAL_IDP_NOT_READY");
        await Audit(stack, sentinels, "LOCAL_IDP_NOT_READY");
        ComposeFixture.Success(await ComposeFixture.Docker("start", await stack.Container("keycloak"))); await stack.WaitHealthy("keycloak");
        ComposeFixture.Success(await stack.Command("smoke")); await Audit(stack, sentinels);
        await AuditImages(stack, sentinels);
        LocalStackFailureTests.Unchanged(stack, original);
    }

    private static async Task WaitListening(ComposeFixture stack)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try { using var response = await client.GetAsync($"http://127.0.0.1:{stack.ApiPort}/health/live"); if (response.StatusCode == HttpStatusCode.OK) return; }
            catch (HttpRequestException) { }
            await Task.Delay(200);
        }
        Assert.Fail("API did not start within the deadline.");
    }
    private static void Absent(IEnumerable<string> sentinels, string output, string category)
    { Assert.True(sentinels.All(s => !output.Contains(s, StringComparison.Ordinal)), "Sensitive diagnostic category detected: " + category); }

    private static async Task Audit(ComposeFixture stack, List<string> sentinels, params string[] reasons)
    {
        var logs = await stack.Logs(); var cli = string.Join('\n', stack.Captured);
        Absent(sentinels, logs, "service logs"); Absent(sentinels, cli, "lifecycle output");
        foreach (var reason in reasons) Assert.True((logs + cli).Contains(reason, StringComparison.Ordinal), "Expected safe diagnostic missing: " + reason);
        var config = await stack.Compose("config"); ComposeFixture.Success(config); Absent(sentinels, config.Output, "Compose config");
        foreach (var suffix in new[] { "api", "migrator", "postgres", "keycloak" })
        foreach (var args in new[] { new[] { "image", "inspect", stack.Project + "-" + suffix }, new[] { "history", "--no-trunc", stack.Project + "-" + suffix } })
        { var result = await ComposeFixture.Docker(args); ComposeFixture.Success(result); Absent(sentinels, result.Output, "image config/history"); }
        var ids = await ComposeFixture.Docker("ps", "-aq", "--filter", "label=com.docker.compose.project=" + stack.Project);
        foreach (var id in ids.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        { var inspect = await ComposeFixture.Docker("inspect", id); ComposeFixture.Success(inspect); Absent(sentinels, inspect.Output, "container config"); }
    }

    private static async Task AuditImages(ComposeFixture stack, List<string> sentinels)
    {
        var directory = Path.Combine(Path.GetTempPath(), "crm-layer-audit-" + Guid.NewGuid().ToString("N"));
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            foreach (var suffix in new[] { "api", "migrator", "postgres", "keycloak" })
            {
                var image = stack.Project + "-" + suffix;
                foreach (var args in new[] { new[] { "image", "inspect", image }, new[] { "history", "--no-trunc", image } })
                { var result = await ComposeFixture.Docker(args); ComposeFixture.Success(result); Absent(sentinels, result.Output, "image config/history"); }
                var archive = Path.Combine(directory, "image.tar"); ComposeFixture.Success(await ComposeFixture.Docker("image", "save", "--output", archive, image));
                await using (var stream = File.OpenRead(archive))
                using (var tar = new TarReader(stream))
                {
                    while (await tar.GetNextEntryAsync() is { } entry)
                    {
                        if (entry.DataStream is null) continue;
                        var blob = Path.Combine(directory, "blob");
                        await using (var file = File.Create(blob)) await entry.DataStream.CopyToAsync(file);
                        await using (var file = File.OpenRead(blob))
                        {
                            var first = file.ReadByte(); var second = file.ReadByte(); file.Position = 0;
                            if (first == 0x1f && second == 0x8b)
                            { using var gzip = new GZipStream(file, CompressionMode.Decompress); await Scan(gzip, sentinels); }
                            else await Scan(file, sentinels);
                        }
                        File.Delete(blob);
                    }
                }
                File.Delete(archive);
            }
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Scan(Stream stream, List<string> sentinels)
    {
        var buffer = new byte[131072]; var tail = ""; int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            var text = tail + Encoding.Latin1.GetString(buffer, 0, read);
            Absent(sentinels, text, "published layer files"); tail = text.Length > 4096 ? text[^4096..] : text;
        }
    }
}
