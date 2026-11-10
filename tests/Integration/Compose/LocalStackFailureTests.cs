using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Integration.Fixtures;
using Xunit;

namespace Integration.Compose;

[CollectionDefinition("Compose failures", DisableParallelization = true)]
public sealed class ComposeFailureCollection;

// These gates catch stale readiness, skipped warm validation, unsafe repair,
// checkout-scoped locks and lifecycle actions affecting foreign resources.
[Collection("Compose failures")]
[Trait("Category", "Compose")]
public sealed class LocalStackFailureTests
{
    [Fact]
    public async Task Crm_db_outage_recovers_same_api_instance()
    {
        await using var stack = new ComposeFixture();
        ComposeFixture.Success(await stack.Command("up"));
        var originalApi = await stack.Container("crm-api");
        ComposeFixture.Success(await ComposeFixture.Docker("stop", "--time", "5", await stack.Container("crm-db")));
        await stack.Health(HttpStatusCode.ServiceUnavailable);
        ComposeFixture.Success(await ComposeFixture.Docker("start", await stack.Container("crm-db")));
        await stack.WaitHealthy("crm-db");
        await stack.Health(HttpStatusCode.OK);
        Assert.Equal(originalApi, await stack.Container("crm-api"));
        ComposeFixture.Success(await stack.Command("smoke"));
    }

    [Fact]
    public async Task Idp_outage_does_not_change_crm_readiness()
    {
        await using var stack = new ComposeFixture();
        ComposeFixture.Success(await stack.Command("up"), "initial startup");
        var originalApi = await stack.Container("crm-api");
        await stack.Sql("CREATE TABLE public.fixture_sentinel(value integer); INSERT INTO public.fixture_sentinel VALUES (61)");
        foreach (var service in new[] { "keycloak", "idp-db" })
        {
            ComposeFixture.Success(await ComposeFixture.Docker("stop", "--time", "5", await stack.Container(service)));
            await stack.Health(HttpStatusCode.OK);
            ComposeFixture.Failure(await stack.Command("smoke"), "LOCAL_IDP_NOT_READY");
            ComposeFixture.Success(await ComposeFixture.Docker("start", await stack.Container(service)));
            await stack.WaitHealthy("idp-db"); await stack.WaitHealthy("keycloak");
            await stack.WaitSmoke();
        }
        Assert.Equal(originalApi, await stack.Container("crm-api"));
        Assert.Equal("61", await stack.Sql("SELECT value FROM public.fixture_sentinel"));
    }

    [Fact]
    public async Task Bootstrap_and_migration_failure_block_new_api()
    {
        await using var stack = new ComposeFixture();
        ComposeFixture.Success(await stack.Command("up"));
        await stack.Sql("CREATE TABLE public.fixture_sentinel(value integer); INSERT INTO public.fixture_sentinel VALUES (62)");
        var secrets = Snapshot(stack);
        var oldBootstrap = await stack.Container("crm-bootstrap");
        await stack.Sql("ALTER ROLE crm_app CREATEDB");
        ComposeFixture.Failure(await stack.Command("up"), "LOCAL_BOOTSTRAP_FAILED");
        Assert.NotEqual(oldBootstrap, await stack.Container("crm-bootstrap"));
        await NoApi(stack);
        await stack.Sql("ALTER ROLE crm_app NOCREATEDB");
        ComposeFixture.Success(await stack.Command("up")); await stack.Health(HttpStatusCode.OK);
        var oldMigration = await stack.Container("crm-migrate");
        // The current migration must actually try to insert its history row and
        // fail at PostgreSQL, rather than run a deliberately broken image.
        await stack.Sql("DELETE FROM platform.\"__EFMigrationsHistory\"; ALTER TABLE platform.\"__EFMigrationsHistory\" ADD CONSTRAINT fixture_block_history CHECK (false) NOT VALID");
        ComposeFixture.Failure(await stack.Command("up"), "LOCAL_MIGRATION_FAILED");
        Assert.NotEqual(oldMigration, await stack.Container("crm-migrate")); await NoApi(stack);
        Assert.Equal("0", await stack.Sql("SELECT count(*) FROM platform.\"__EFMigrationsHistory\""));
        await stack.Sql("ALTER TABLE platform.\"__EFMigrationsHistory\" DROP CONSTRAINT fixture_block_history");
        ComposeFixture.Success(await stack.Command("up")); await stack.Ready();
        Assert.Equal("62", await stack.Sql("SELECT value FROM public.fixture_sentinel"));
        Unchanged(stack, secrets);
    }

    [Fact]
    public async Task Partial_init_retry_and_mismatched_secrets_are_safe()
    {
        await using var stack = new ComposeFixture();
        ComposeFixture.Success(await stack.Command("up"));
        await stack.Sql("CREATE TABLE public.fixture_sentinel(value integer); INSERT INTO public.fixture_sentinel VALUES (63)");
        var secrets = Snapshot(stack);
        await stack.Sql("UPDATE _local.bootstrap SET complete=false", database: "postgres");
        await stack.Sql("REVOKE CREATE ON DATABASE crm FROM crm_migrator");
        ComposeFixture.Success(await stack.Command("up"));
        Assert.Equal("t", await stack.Sql("SELECT complete FROM _local.bootstrap", database: "postgres"));
        Assert.Equal("t", await stack.Sql("SELECT has_database_privilege('crm_migrator','crm','CREATE')"));
        var file = Path.Combine(stack.State, "crm_app_password");
        var original = File.ReadAllBytes(file);
        File.Delete(file);
        ComposeFixture.Failure(await stack.Command("up"), "LOCAL_SECRET_STATE_MISMATCH");
        Assert.False(File.Exists(file), "Lifecycle silently regenerated a missing credential.");
        WritePrivate(file, original);
        var changed = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(file, changed);
        ComposeFixture.Failure(await stack.Command("up"), "LOCAL_SECRET_STATE_MISMATCH");
        Assert.True(File.ReadAllText(file) == changed, "Lifecycle silently replaced a changed credential.");
        File.WriteAllBytes(file, original);
        // A DB-side credential mismatch also fails despite an intact manifest.
        await stack.SqlInput("ALTER ROLE crm_app PASSWORD '" + changed + "';");
        ComposeFixture.Failure(await stack.Command("up"), "LOCAL_BOOTSTRAP_FAILED"); await NoApi(stack);
        await stack.SqlInput("ALTER ROLE crm_app PASSWORD '" + Encoding.UTF8.GetString(original) + "';");
        ComposeFixture.Success(await stack.Command("up")); await stack.Ready();
        Assert.Equal("63", await stack.Sql("SELECT value FROM public.fixture_sentinel")); Unchanged(stack, secrets);
    }

    [Fact]
    public async Task Realm_drift_is_not_overwritten()
    {
        await using var stack = new ComposeFixture();
        ComposeFixture.Success(await stack.Command("up"));
        var realm = await stack.Sql("SELECT id FROM realm WHERE name='crm-local'", database: "keycloak", service: "idp-db");
        var secrets = Snapshot(stack);
        await stack.Sql("UPDATE client SET direct_access_grants_enabled=true WHERE client_id='crm-web-local'", database: "keycloak", service: "idp-db");
        // Restart to discard Keycloak's cached client representation.
        ComposeFixture.Success(await ComposeFixture.Docker("restart", await stack.Container("keycloak")));
        await stack.WaitHealthy("keycloak");
        ComposeFixture.Failure(await stack.Command("up"), "LOCAL_REALM_DRIFT");
        Assert.Equal("t", await stack.Sql("SELECT direct_access_grants_enabled FROM client WHERE client_id='crm-web-local'", database: "keycloak", service: "idp-db"));
        Assert.Equal(realm, await stack.Sql("SELECT id FROM realm WHERE name='crm-local'", database: "keycloak", service: "idp-db"));
        await stack.Sql("UPDATE client SET direct_access_grants_enabled=false WHERE client_id='crm-web-local'", database: "keycloak", service: "idp-db");
        ComposeFixture.Success(await ComposeFixture.Docker("restart", await stack.Container("keycloak")));
        await stack.WaitHealthy("keycloak"); ComposeFixture.Success(await stack.Command("up")); Unchanged(stack, secrets);
    }

    [Fact]
    public async Task Foreign_listener_cannot_supply_smoke_success()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var cancellation = new CancellationTokenSource();
        var serving = Task.Run(async () =>
        {
            try { while (!cancellation.IsCancellationRequested)
            { using var connection = await listener.AcceptTcpClientAsync(cancellation.Token); await connection.GetStream().WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"), cancellation.Token); } }
            catch (OperationCanceledException) { }
        });
        try
        {
            await using var stack = new ComposeFixture(apiPort: ((IPEndPoint)listener.LocalEndpoint).Port);
            using var client = new HttpClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"http://127.0.0.1:{stack.ApiPort}/health/ready")).StatusCode);
            ComposeFixture.Failure(await stack.Command("smoke"), "LOCAL_SMOKE_FAILED");
            ComposeFixture.Failure(await stack.Command("up"), "LOCAL_PORT_CONFLICT");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"http://127.0.0.1:{stack.ApiPort}/health/ready")).StatusCode);
        }
        finally { cancellation.Cancel(); await serving; }
    }

    [Fact]
    public async Task Concurrent_lifecycle_is_rejected_including_distinct_state_paths()
    {
        await using var stack = new ComposeFixture();
        var checkout = Path.Combine(Path.GetTempPath(), "crm-checkout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(checkout, "scripts/local")); Directory.CreateDirectory(Path.Combine(checkout, "infra/compose"));
        foreach (var relative in new[] { "scripts/dev-down.sh", "scripts/local_stack.py", "compose.yaml", "infra/compose/images.lock.env" }.Concat(Directory.GetFiles(Path.Combine(stack.Root, "scripts/local"), "*.py").Select(p => "scripts/local/" + Path.GetFileName(p))))
            File.Copy(Path.Combine(stack.Root, relative), Path.Combine(checkout, relative));
        ComposeFixture.Success(await stack.Command("up"));
        await stack.Sql("CREATE TABLE public.fixture_lock_sentinel(value integer); INSERT INTO public.fixture_lock_sentinel VALUES (87)");
        var secrets = Snapshot(stack);
        var originalApi = await stack.Container("crm-api");
        // Hold the real lifecycle in its bounded stop of this exact owned API.
        // A PostgreSQL session blocker would be lost if up recreates the DB.
        ComposeFixture.Success(await ComposeFixture.Docker("kill", "--signal", "STOP", originalApi));
        using var holder = Process.Start(stack.CommandInfo("up"))!;
        var holderError = holder.StandardError.ReadToEndAsync(); Task<string>? holderOutput = null;
        try
        {
            var firstLine = await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(90));
            Assert.NotNull(firstLine);
            using (var started = System.Text.Json.JsonDocument.Parse(firstLine))
                Assert.Equal("build_completed", started.RootElement.GetProperty("eventName").GetString());
            holderOutput = holder.StandardOutput.ReadToEndAsync();
            Assert.False(holder.HasExited, "Actual dev-up exited before lifecycle contention was exercised.");
            foreach (var action in new[] { "up", "down", "smoke" })
                ComposeFixture.Failure(await stack.Command(action), "LOCAL_LIFECYCLE_BUSY");
            ComposeFixture.Failure(await stack.Command("down", state: stack.State + "-other", root: checkout), "LOCAL_LIFECYCLE_BUSY");
            Assert.False(holder.HasExited, "Owned API stop did not hold the real lifecycle through contention checks.");
            holder.Kill(true); await holder.WaitForExitAsync();
            stack.Captured.Add(firstLine + "\n" + await holderOutput + await holderError);
            // A new real CLI must acquire the released process lock. Its
            // remaining prerequisites can be incomplete after process kill.
            ComposeFixture.Failure(await stack.Command("smoke"), "LOCAL_BOOTSTRAP_FAILED", "LOCAL_MIGRATION_FAILED", "LOCAL_SMOKE_FAILED");
        }
        finally
        {
            if (!holder.HasExited) { holder.Kill(true); await holder.WaitForExitAsync(); }
            // The stop may already have removed this owned container.
            var resumed = await ComposeFixture.Docker("kill", "--signal", "CONT", originalApi);
            stack.Captured.Add(resumed.Output);
            if (holderOutput is not null) stack.Captured.Add(await holderOutput + await holderError);
            Directory.Delete(checkout, true);
        }
        ComposeFixture.Success(await stack.Command("up")); await stack.Ready();
        Assert.Equal("87", await stack.Sql("SELECT value FROM public.fixture_lock_sentinel"));
        Unchanged(stack, secrets);
    }

    [Fact]
    public async Task Parallel_projects_do_not_share_resources()
    {
        await using var first = new ComposeFixture(); await using var second = new ComposeFixture();
        var results = await Task.WhenAll(first.Command("up"), second.Command("up"));
        foreach (var result in results) ComposeFixture.Success(result);
        await first.Ready(); await second.Ready();
        foreach (var service in new[] { "crm-db", "idp-db", "crm-api", "keycloak", "crm-bootstrap", "crm-migrate" })
        {
            Assert.NotEqual(await first.Container(service), await second.Container(service));
            var one = await first.Inspect(service); var two = await second.Inspect(service);
            Assert.Empty(one.GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject().Select(p => p.Name).Intersect(two.GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject().Select(p => p.Name)));
            Assert.Empty(one.GetProperty("Mounts").EnumerateArray().Select(m => m.GetProperty("Source").GetString()).Intersect(two.GetProperty("Mounts").EnumerateArray().Select(m => m.GetProperty("Source").GetString())));
        }
        await first.Sql("CREATE TABLE public.fixture_isolation(value integer); INSERT INTO public.fixture_isolation VALUES (64)");
        Assert.Equal("", await second.Sql("SELECT to_regclass('public.fixture_isolation')"));
        ComposeFixture.Success(await first.Command("down")); await second.Ready();
    }

    [Fact]
    public async Task Shutdown_is_bounded()
    {
        await using var stack = new ComposeFixture(); ComposeFixture.Success(await stack.Command("up"));
        var secrets = Snapshot(stack);
        ComposeFixture.Success(await ComposeFixture.Docker("kill", "--signal", "STOP", await stack.Container("crm-api")));
        var watch = Stopwatch.StartNew(); ComposeFixture.Success(await stack.Command("down"));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(50), "Shutdown exceeded its finite deadline.");
        var containers = await ComposeFixture.Docker("ps", "-aq", "--filter", "label=com.docker.compose.project=" + stack.Project);
        Assert.Equal("", containers.Output.Trim()); Unchanged(stack, secrets);
        var volumes = await ComposeFixture.Docker("volume", "ls", "-q", "--filter", "label=com.docker.compose.project=" + stack.Project);
        Assert.Equal(2, volumes.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    internal static Dictionary<string, byte[]> Snapshot(ComposeFixture stack) => Directory.GetFiles(stack.State).ToDictionary(p => Path.GetFileName(p)!, File.ReadAllBytes);
    internal static void Unchanged(ComposeFixture stack, Dictionary<string, byte[]> snapshot)
    { foreach (var (file, bytes) in snapshot) Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(stack.State, file))), "Persistent credential state changed."); }
    internal static void WritePrivate(string path, byte[] bytes)
    { if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException(); using var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }); stream.Write(bytes); }
    internal static async Task NoApi(ComposeFixture stack)
    { var result = await ComposeFixture.Docker("ps", "-aq", "--filter", "label=com.docker.compose.project=" + stack.Project, "--filter", "label=com.docker.compose.service=crm-api"); ComposeFixture.Success(result); Assert.Equal("", result.Output.Trim()); }
}
