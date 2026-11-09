using Integration.Fixtures;
using Xunit;

namespace Integration.Compose;

[Trait("Category", "Compose")]
public sealed class LocalStackTests
{
    [Fact]
    public async Task Cold_up_needs_no_host_sdk()
    {
        await using var stack = new ComposeFixture();
        ComposeFixture.Success(await stack.Command("up"));
        await stack.Ready();
    }
    [Fact]
    public async Task Warm_up_revalidates_oneshots_and_preserves_data()
    {
        await using var stack = new ComposeFixture();
        ComposeFixture.Success(await stack.Command("up"));
        await stack.Sql("CREATE TABLE public.fixture_sentinel(value integer); INSERT INTO public.fixture_sentinel VALUES (42)");
        var oldBootstrap = await stack.Container("crm-bootstrap"); var oldMigrate = await stack.Container("crm-migrate"); var oldApi = await stack.Container("crm-api");
        ComposeFixture.Success(await stack.Command("up"));
        Assert.NotEqual(oldBootstrap, await stack.Container("crm-bootstrap")); Assert.NotEqual(oldMigrate, await stack.Container("crm-migrate")); Assert.NotEqual(oldApi, await stack.Container("crm-api"));
        Assert.Equal("42", await stack.Sql("SELECT value FROM public.fixture_sentinel")); await stack.Ready();
    }
    [Fact]
    public async Task Down_up_keeps_secrets_volumes_and_realm()
    {
        await using var stack = new ComposeFixture();
        ComposeFixture.Success(await stack.Command("up"));
        var secret = File.ReadAllBytes(Path.Combine(stack.State, "crm_app_password"));
        await stack.Sql("CREATE TABLE public.fixture_sentinel(value integer); INSERT INTO public.fixture_sentinel VALUES (73)");
        var realmId = await stack.Sql("SELECT id FROM realm WHERE name='crm-local'", "postgres", "keycloak", "idp-db");
        var volume = (await stack.Inspect("crm-db")).GetProperty("Mounts").EnumerateArray().Single(m => m.GetProperty("Type").GetString() == "volume").GetProperty("Name").GetString();
        var metadata = File.ReadAllText(stack.State + ".stack.json");
        using (var manifest = System.Text.Json.JsonDocument.Parse(metadata))
        {
            Assert.Equal(stack.ApiPort, manifest.RootElement.GetProperty("apiPort").GetInt32());
            Assert.Equal(stack.IdpPort, manifest.RootElement.GetProperty("idpPort").GetInt32());
            Assert.Equal("ready", manifest.RootElement.GetProperty("provisioning").GetString());
        }
        ComposeFixture.Success(await stack.Command("down"));
        Assert.Equal(metadata, File.ReadAllText(stack.State + ".stack.json"));
        Assert.True(secret.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(stack.State, "crm_app_password"))), "Secret bytes changed after down.");
        Assert.Equal(0, (await ComposeFixture.Docker("volume", "inspect", volume!)).ExitCode);
        ComposeFixture.Success(await stack.Command("up"));
        Assert.Equal("73", await stack.Sql("SELECT value FROM public.fixture_sentinel"));
        Assert.Equal(realmId, await stack.Sql("SELECT id FROM realm WHERE name='crm-local'", "postgres", "keycloak", "idp-db")); await stack.Ready();
    }
    [Fact]
    public async Task Sql_and_secret_mount_boundaries_hold()
    {
        await using var stack = new ComposeFixture();
        ComposeFixture.Success(await stack.Command("up")); await stack.Ready();
        Assert.Equal("t", await stack.Sql("SELECT NOT rolsuper AND NOT rolbypassrls AND NOT has_database_privilege(current_user,'crm','CREATE') AND NOT has_schema_privilege(current_user,'platform','CREATE') AND NOT has_table_privilege(current_user,'platform.\"__EFMigrationsHistory\"','INSERT') FROM pg_roles WHERE rolname=current_user", "crm_app"));
        Assert.Equal("t", await stack.Sql("SELECT NOT rolsuper AND NOT rolbypassrls AND has_database_privilege(current_user,'crm','CREATE') AND oid<>(SELECT datdba FROM pg_database WHERE datname='crm') FROM pg_roles WHERE rolname=current_user", "crm_migrator"));
        var denied = await ComposeFixture.Docker("exec", await stack.Container("crm-db"), "psql", "-U", "crm_app", "-d", "crm", "-c", "CREATE TABLE public.forbidden(id integer)"); Assert.NotEqual(0, denied.ExitCode);
        var deniedHistory = await ComposeFixture.Docker("exec", await stack.Container("crm-db"), "psql", "-U", "crm_app", "-d", "crm", "-c", "INSERT INTO platform.\"__EFMigrationsHistory\" VALUES ('forbidden','10.0')"); Assert.NotEqual(0, deniedHistory.ExitCode);
        Assert.Equal("t", await stack.Sql("SELECT NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolbypassrls AND oid=(SELECT datdba FROM pg_database WHERE datname='keycloak') FROM pg_roles WHERE rolname='keycloak'", "postgres", "keycloak", "idp-db"));
        var api = await stack.Inspect("crm-api"); var idp = await stack.Inspect("keycloak");
        Assert.Equal("/run/secrets/platform_connection", Assert.Single(api.GetProperty("Mounts").EnumerateArray()).GetProperty("Destination").GetString());
        Assert.Equal(2, idp.GetProperty("Mounts").GetArrayLength());
        Assert.DoesNotContain(idp.GetProperty("Mounts").EnumerateArray(), m => m.GetProperty("Source").GetString()!.Contains("crm_"));
        var crmNetwork = Assert.Single(api.GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject()).Name;
        var idpNetwork = Assert.Single(idp.GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject()).Name; Assert.NotEqual(crmNetwork, idpNetwork);
        foreach (var service in new[] { "crm-db", "crm-bootstrap", "crm-migrate" })
            Assert.Equal(crmNetwork, Assert.Single((await stack.Inspect(service)).GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject()).Name);
        Assert.Equal(idpNetwork, Assert.Single((await stack.Inspect("idp-db")).GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject()).Name);
        foreach (var (service, destinations) in new[]
        {
            ("crm-db", new[] { "/run/secrets/crm_admin_password" }),
            ("crm-bootstrap", new[] { "/run/secrets/crm_admin_connection", "/run/secrets/crm_migrator_password", "/run/secrets/crm_app_password" }),
            ("crm-migrate", new[] { "/run/secrets/platform_connection" }),
            ("idp-db", new[] { "/run/secrets/idp_db_admin_password", "/run/secrets/idp_db_password" })
        })
        {
            var mounts = (await stack.Inspect(service)).GetProperty("Mounts").EnumerateArray().Where(m => m.GetProperty("Destination").GetString()!.StartsWith("/run/secrets/", StringComparison.Ordinal)).ToArray();
            Assert.Equal(destinations.Order(), mounts.Select(m => m.GetProperty("Destination").GetString()!).Order());
            Assert.All(mounts, m => Assert.False(m.GetProperty("RW").GetBoolean()));
        }
        var crmVolume = (await stack.Inspect("crm-db")).GetProperty("Mounts").EnumerateArray().Single(m => m.GetProperty("Type").GetString() == "volume").GetProperty("Name").GetString();
        var idpVolume = (await stack.Inspect("idp-db")).GetProperty("Mounts").EnumerateArray().Single(m => m.GetProperty("Type").GetString() == "volume").GetProperty("Name").GetString(); Assert.NotEqual(crmVolume,idpVolume);

        // A healthy API using the higher-privilege migrator file must fail smoke.
        // Replace only this random fixture's API and retain its ownership labels.
        Assert.Equal(0, (await ComposeFixture.Docker("rm", "-f", await stack.Container("crm-api"))).ExitCode);
        var replacement = await ComposeFixture.Docker("run", "-d", "--name", stack.Project + "-wrong-api",
            "--label", "com.docker.compose.project=" + stack.Project,
            "--label", "com.docker.compose.service=crm-api",
            "--label", "com.docker.compose.project.working_dir=" + stack.Root,
            "--label", "com.docker.compose.project.config_files=" + Path.Combine(stack.Root, "compose.yaml"),
            "--label", "com.docker.compose.oneoff=False", "--network", crmNetwork,
            "-p", $"127.0.0.1:{stack.ApiPort}:8080", "-e", "ASPNETCORE_URLS=http://+:8080",
            "--mount", $"type=bind,source={Path.Combine(stack.State, "crm_migrator_connection")},target=/run/secrets/platform_connection,readonly",
            api.GetProperty("Config").GetProperty("Image").GetString()!);
        Assert.Equal(0, replacement.ExitCode);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while ((await stack.Inspect("crm-api")).GetProperty("State").GetProperty("Health").GetProperty("Status").GetString() != "healthy" && DateTime.UtcNow < deadline)
            await Task.Delay(200);
        Assert.Equal("healthy", (await stack.Inspect("crm-api")).GetProperty("State").GetProperty("Health").GetProperty("Status").GetString());
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync($"http://127.0.0.1:{stack.ApiPort}/health/live")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync($"http://127.0.0.1:{stack.ApiPort}/health/ready")).StatusCode);
        var rejected = await stack.Command("smoke");
        Assert.Equal(1, rejected.ExitCode);
        using var rejection = System.Text.Json.JsonDocument.Parse(rejected.Output);
        Assert.Equal("LOCAL_SECRET_STATE_MISMATCH", rejection.RootElement.GetProperty("reasonToken").GetString());
        Assert.False(string.IsNullOrEmpty(rejection.RootElement.GetProperty("correlationId").GetString()));
    }
}
