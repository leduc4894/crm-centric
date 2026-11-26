using Integration.Fixtures;
using Xunit;

namespace Integration.Compose;

[Trait("Category", "Compose")]
public sealed class LocalStackTests
{
    [Fact]
    public async Task Docker_run_keeps_diagnostics_out_of_json_response()
    {
        var result = await ComposeFixture.Docker("run", "--rm", ContainerImages.Pins["DOTNET_SDK_IMAGE"],
            "sh", "-c", "printf '{\"issuer\":\"expected\"}'; printf 'diagnostic\\n' >&2");
        Assert.Equal(0, result.ExitCode);
        using var body = System.Text.Json.JsonDocument.Parse(result.Output);
        Assert.Equal("expected", body.RootElement.GetProperty("issuer").GetString());
    }

    [Fact]
    public async Task Docker_run_keeps_stderr_when_command_fails()
    {
        var result = await ComposeFixture.Docker("run", "--rm", ContainerImages.Pins["DOTNET_SDK_IMAGE"],
            "sh", "-c", "printf 'failure diagnostic\\n' >&2; exit 7");
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("failure diagnostic", result.Output);
    }

    [Fact]
    public async Task Existing_v1_stack_migrates_tls_without_losing_data_and_rejects_missing_tls_afterwards()
    {
        await using var stack = new ComposeFixture();
        ComposeFixture.Success(await stack.Command("up"));
        await stack.Sql("CREATE TABLE public.fixture_migration(value integer); INSERT INTO public.fixture_migration VALUES (19)");
        var credential = File.ReadAllBytes(Path.Combine(stack.State, "crm_app_password"));
        var volume = (await stack.Inspect("crm-db")).GetProperty("Mounts").EnumerateArray()
            .Single(m => m.GetProperty("Type").GetString() == "volume").GetProperty("Name").GetString();
        ComposeFixture.Success(await stack.Command("down"));
        var metadataPath = stack.State + ".stack.json";
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(metadataPath))!.AsObject();
        metadata["version"] = 1;
        await File.WriteAllTextAsync(metadataPath, metadata.ToJsonString());
        Directory.Delete(stack.State + ".tls", recursive: true);
        ComposeFixture.Success(await stack.Command("up"));
        Assert.Equal("19", await stack.Sql("SELECT value FROM public.fixture_migration"));
        Assert.True(credential.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(stack.State, "crm_app_password"))));
        Assert.Equal(volume, (await stack.Inspect("crm-db")).GetProperty("Mounts").EnumerateArray()
            .Single(m => m.GetProperty("Type").GetString() == "volume").GetProperty("Name").GetString());
        using (var migrated = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath)))
            Assert.Equal(2, migrated.RootElement.GetProperty("version").GetInt32());
        ComposeFixture.Success(await stack.Command("down"));
        Directory.Delete(stack.State + ".tls", recursive: true);
        ComposeFixture.Failure(await stack.Command("up"), "LOCAL_SECRET_STATE_MISMATCH");
        Assert.False(Directory.Exists(stack.State + ".tls"));
        Assert.Equal(0, (await ComposeFixture.Docker("volume", "inspect", volume!)).ExitCode);
    }

    [Fact]
    public async Task Api_uses_trusted_tls_backchannel_with_unchanged_public_issuer()
    {
        await using var stack = new ComposeFixture();
        ComposeFixture.Success(await stack.Command("up", startTimeout: 90), "COMPOSE_TLS_STARTUP_FAILED");
        var apiId = await stack.Container("crm-api");
        var certificate = stack.State + ".tls/cert.pem";
        var discovery = await ComposeFixture.Docker("run", "--rm", "--network", "container:" + apiId,
            "--mount", $"type=bind,source={certificate},target=/tmp/ca.pem,readonly",
            ContainerImages.Pins["DOTNET_SDK_IMAGE"], "curl", "--silent", "--show-error", "--fail",
            "--noproxy", "*", "--cacert", "/tmp/ca.pem",
            "https://keycloak:8443/realms/crm-local/.well-known/openid-configuration");
        ComposeFixture.Success(discovery, "COMPOSE_TLS_DISCOVERY_FAILED");
        var validDiscovery = false;
        try
        {
            using var body = System.Text.Json.JsonDocument.Parse(discovery.Output);
            validDiscovery = body.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && body.RootElement.TryGetProperty("issuer", out var issuer)
                && issuer.ValueKind == System.Text.Json.JsonValueKind.String
                && issuer.GetString() == $"http://localhost:{stack.IdpPort}/realms/crm-local"
                && body.RootElement.TryGetProperty("jwks_uri", out var jwks)
                && jwks.ValueKind == System.Text.Json.JsonValueKind.String
                && jwks.GetString() == "https://keycloak:8443/realms/crm-local/protocol/openid-connect/certs";
        }
        catch (System.Text.Json.JsonException) { /* Report malformed discovery through the safe stage token below. */ }
        ComposeFixture.Success(new(validDiscovery ? 0 : 1, ""), "COMPOSE_TLS_DISCOVERY_FAILED");
        var management = await ComposeFixture.Docker("run", "--rm", "--network", "container:" + apiId,
            ContainerImages.Pins["DOTNET_SDK_IMAGE"], "curl", "--silent", "--fail", "--noproxy", "*",
            "--connect-timeout", "2", "http://keycloak:9000/health/ready");
        ComposeFixture.Success(new(management.ExitCode == 0 ? 1 : 0, ""), "COMPOSE_TLS_MANAGEMENT_FAILED");
    }

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
        Assert.Equal(new[] { "/run/secrets/idp_tls_cert", "/run/secrets/platform_connection" }, api.GetProperty("Mounts").EnumerateArray().Select(m => m.GetProperty("Destination").GetString()).Order().ToArray());
        Assert.Equal(4, idp.GetProperty("Mounts").GetArrayLength());
        Assert.DoesNotContain(idp.GetProperty("Mounts").EnumerateArray(), m => m.GetProperty("Source").GetString()!.Contains("crm_"));
        var apiNetworks = api.GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject().Select(n => n.Name).ToHashSet();
        var idpNetworks = idp.GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject().Select(n => n.Name).ToHashSet();
        var crmNetwork = Assert.Single((await stack.Inspect("crm-db")).GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject()).Name;
        var idpNetwork = Assert.Single((await stack.Inspect("idp-db")).GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject()).Name;
        Assert.NotEqual(crmNetwork, idpNetwork);
        Assert.Equal(2, apiNetworks.Count); Assert.Equal(2, idpNetworks.Count);
        Assert.Contains(crmNetwork, apiNetworks); Assert.DoesNotContain(crmNetwork, idpNetworks);
        Assert.Contains(idpNetwork, idpNetworks); Assert.DoesNotContain(idpNetwork, apiNetworks);
        Assert.Single(apiNetworks.Intersect(idpNetworks));
        foreach (var service in new[] { "crm-bootstrap", "crm-migrate" })
            Assert.Equal(crmNetwork, Assert.Single((await stack.Inspect(service)).GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject()).Name);
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
            "-e", "ASPNETCORE_ENVIRONMENT=Development", "-e", $"Oidc__Issuer=http://localhost:{stack.IdpPort}/realms/crm-local",
            "-e", "Oidc__Audience=crm-api", "-e", "Oidc__MetadataAddress=https://keycloak:8443/realms/crm-local/.well-known/openid-configuration",
            "-e", "Oidc__AllowHttpLoopback=true", "-e", "SSL_CERT_FILE=/run/secrets/idp_tls_cert",
            "--mount", $"type=bind,source={stack.State}.tls/cert.pem,target=/run/secrets/idp_tls_cert,readonly",
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
