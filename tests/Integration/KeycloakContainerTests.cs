using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Integration.Fixtures;
using Npgsql;
using Xunit;

namespace Integration;

[Collection("Container runtime")]
public sealed class KeycloakContainerTests(ContainerImages images)
{
    [Fact]
    public async Task Nonlocal_hostname_fails_before_startup_without_secret_output()
    {
        using var secrets = new LocalSecretFixture();
        var result = await ContainerImages.Docker(false, "run", "--rm",
            "--mount", $"type=bind,source={secrets.ApplicationFile},target=/run/secrets/idp_db_password,readonly",
            "--mount", $"type=bind,source={secrets.MigratorFile},target=/run/secrets/idp_admin_password,readonly",
            "--env", "KC_HOSTNAME=http://example.com:8080", images.Keycloak, "--help");
        Assert.NotEqual(0, result.ExitCode);
        using var error = JsonDocument.Parse(result.Error);
        Assert.Equal("LOCAL_CONFIGURATION_INVALID", error.RootElement.GetProperty("reasonToken").GetString());
        Assert.True(Guid.TryParseExact(error.RootElement.GetProperty("correlationId").GetString(), "N", out _));
    }

    [Fact]
    public async Task Idp_database_initializer_creates_nonprivileged_owner()
    {
        await using var stack = new IdpStack(images);
        await stack.Start(databaseOnly: true);
        await stack.AssertDatabaseRole();
    }

    [Fact]
    public async Task Cold_import_has_two_pkce_clients_and_canonical_discovery()
    {
        await using var stack = new IdpStack(images);
        await stack.Start();
        await AssertDiscovery(stack);
        using var admin = await stack.Admin();
        var clients = await admin.GetFromJsonAsync<JsonElement>("admin/realms/crm-local/clients");
        foreach (var (name, port) in new[] { ("crm-web-local", 5173), ("demo-module-local", 5180) })
        {
            var client = Assert.Single(clients.EnumerateArray(), x => x.GetProperty("clientId").GetString() == name);
            Assert.True(client.GetProperty("publicClient").GetBoolean());
            Assert.True(client.GetProperty("standardFlowEnabled").GetBoolean());
            Assert.Equal("S256", client.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString());
            Assert.False(client.GetProperty("directAccessGrantsEnabled").GetBoolean());
            Assert.False(client.GetProperty("implicitFlowEnabled").GetBoolean());
            Assert.False(client.GetProperty("serviceAccountsEnabled").GetBoolean());
            Assert.False(client.TryGetProperty("secret", out _));
            Assert.Equal(new[] { $"http://localhost:{port}/auth/callback" }, client.GetProperty("redirectUris").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(new[] { $"http://localhost:{port}" }, client.GetProperty("webOrigins").EnumerateArray().Select(x => x.GetString()));
        }
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>("admin/realms/crm-local/users")).EnumerateArray());
        await stack.AssertDatabaseIsolation();
        Assert.Equal("ready", await stack.InspectRealm());
    }

    [Fact]
    public async Task Warm_start_preserves_realm_and_detects_drift()
    {
        await using var stack = new IdpStack(images);
        await stack.Start();
        using var admin = await stack.Admin();
        var before = await admin.GetFromJsonAsync<JsonElement>("admin/realms/crm-local");
        Assert.Equal("ready", await stack.InspectRealm());
        await stack.Restart();
        Assert.Equal("ready", await stack.InspectRealm());
        // Fetch a fresh management token after restart; no token survives in files.
        using var warmAdmin = await stack.Admin();
        var clients = await warmAdmin.GetFromJsonAsync<JsonElement>("admin/realms/crm-local/clients?clientId=crm-web-local");
        var id = clients[0].GetProperty("id").GetString();
        using var response = await warmAdmin.PutAsJsonAsync("admin/realms/crm-local/clients/" + id, new { directAccessGrantsEnabled = true });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await stack.Restart();
        using var driftAdmin = await stack.Admin();
        var after = await driftAdmin.GetFromJsonAsync<JsonElement>("admin/realms/crm-local");
        Assert.Equal(before.GetProperty("id").GetString(), after.GetProperty("id").GetString());
        Assert.Equal("drift", await stack.InspectRealm());
        var persisted = await driftAdmin.GetFromJsonAsync<JsonElement>("admin/realms/crm-local/clients/" + id);
        Assert.True(persisted.GetProperty("directAccessGrantsEnabled").GetBoolean());
        await ContainerImages.Docker("stop", stack.Keycloak);
        Assert.Equal("unavailable", await stack.InspectRealm());
    }

    [Fact]
    public async Task Host_port_override_changes_discovery_consistently()
    {
        await using var stack = new IdpStack(images);
        await stack.Start();
        Assert.NotEqual(8080, stack.Port);
        await AssertDiscovery(stack);
        Assert.Equal("ready", await stack.InspectRealm());
    }

    private static async Task AssertDiscovery(IdpStack stack)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(stack.BaseUrl + "/realms/crm-local/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var discovery = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(stack.BaseUrl + "/realms/crm-local", discovery.GetProperty("issuer").GetString());
        foreach (var key in new[] { "authorization_endpoint", "token_endpoint", "end_session_endpoint", "jwks_uri" })
            Assert.StartsWith(stack.BaseUrl + "/realms/crm-local/", discovery.GetProperty(key).GetString());
        var jwks = await client.GetFromJsonAsync<JsonElement>(discovery.GetProperty("jwks_uri").GetString());
        Assert.NotEmpty(jwks.GetProperty("keys").EnumerateArray());
        foreach (var key in jwks.GetProperty("keys").EnumerateArray())
            foreach (var field in new[] { "d", "p", "q", "dp", "dq", "qi", "k" }) Assert.False(key.TryGetProperty(field, out _));
    }

    private sealed class IdpStack(ContainerImages images) : IAsyncDisposable
    {
        private readonly string name = "crm-task5-" + Guid.NewGuid().ToString("N");
        private readonly string directory = Path.Combine(Path.GetTempPath(), "crm-idp-" + Guid.NewGuid().ToString("N"));
        private readonly string dbAdmin = Guid.NewGuid().ToString("N");
        private readonly string dbPassword = Guid.NewGuid().ToString("N");
        private readonly string adminPassword = Guid.NewGuid().ToString("N");
        public string Keycloak => name + "-keycloak";
        public int Port { get; } = FreePort();
        public string BaseUrl => "http://localhost:" + Port;
        private string Db => name + "-db";
        private static int FreePort() { using var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); return ((IPEndPoint)l.LocalEndpoint).Port; }
        private string Mount(string file, string destination) => $"type=bind,source={directory}/{file},target={destination},readonly";
        public async Task Start(bool databaseOnly = false)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            foreach (var (file, value) in new[] { ("db-admin", dbAdmin), ("db-password", dbPassword), ("admin-password", adminPassword) })
            {
                File.WriteAllText(Path.Combine(directory, file), value);
                File.SetUnixFileMode(Path.Combine(directory, file), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            await ContainerImages.Docker("network", "create", name);
            await ContainerImages.Docker("run", "--detach", "--name", Db, "--network", name, "--publish", "127.0.0.1::5432",
                "--env", "POSTGRES_PASSWORD_FILE=/run/secrets/idp_db_admin_password", "--env", "LOCAL_DATABASE_SERVICE=idp-db",
                "--mount", Mount("db-admin", "/run/secrets/idp_db_admin_password"),
                "--env", "LOCAL_IDP_DB_PASSWORD_FILE=/run/secrets/idp_db_password",
                "--mount", Mount("db-password", "/run/secrets/idp_db_password"), images.Postgres);
            for (var i = 0; i < 100; i++)
            {
                if ((await ContainerImages.Docker(false, "exec", Db, "pg_isready", "-h", "127.0.0.1", "-U", "postgres")).ExitCode == 0) break;
                await Task.Delay(300);
            }
            if (databaseOnly) return;
            await ContainerImages.Docker("run", "--detach", "--name", Keycloak, "--network", name, "--publish", $"127.0.0.1:{Port}:8080",
                "--env", "KC_DB_URL=jdbc:postgresql://" + Db + ":5432/keycloak", "--env", "KC_DB_USERNAME=keycloak",
                "--env", "KC_BOOTSTRAP_ADMIN_USERNAME=local-admin", "--env", "KC_HOSTNAME=" + BaseUrl,
                "--mount", Mount("db-password", "/run/secrets/idp_db_password"), "--mount", Mount("admin-password", "/run/secrets/idp_admin_password"),
                images.Keycloak, "start-dev", "--import-realm");
            await Ready();
        }
        public async Task Restart() { await ContainerImages.Docker("restart", Keycloak); await Ready(); }
        private async Task Ready()
        {
            for (var i = 0; i < 160; i++)
            {
                if ((await ContainerImages.Docker(false, "exec", Keycloak, "/usr/local/bin/keycloak-healthcheck.sh")).ExitCode == 0) return;
                if (!(await ContainerImages.Inspect(Keycloak)).GetProperty("State").GetProperty("Running").GetBoolean()) break;
                await Task.Delay(500);
            }
            var capture = await ContainerImages.Docker("logs", Keycloak);
            ContainerImages.SavePrivateCapture("idp-startup", capture.Output + capture.Error);
            Assert.Fail("Keycloak did not become management-ready; private capture retained.");
        }
        public async Task<HttpClient> Admin()
        {
            var client = new HttpClient { BaseAddress = new Uri(BaseUrl + "/") };
            using var tokenResponse = await client.PostAsync("realms/master/protocol/openid-connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["grant_type"] = "password", ["client_id"] = "admin-cli", ["username"] = "local-admin", ["password"] = adminPassword }));
            Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
            var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());
            return client;
        }
        private async Task<string> AdminConnection()
        {
            var inspect = await ContainerImages.Inspect(Db);
            var port = int.Parse(inspect.GetProperty("NetworkSettings").GetProperty("Ports").GetProperty("5432/tcp")[0].GetProperty("HostPort").GetString()!);
            return new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = port, Database = "postgres", Username = "postgres", Password = dbAdmin }.ConnectionString;
        }
        public async Task AssertDatabaseRole()
        {
            await using var connection = new NpgsqlConnection(await AdminConnection());
            await connection.OpenAsync();
            await using var sql = new NpgsqlCommand("SELECT rolcanlogin AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication AND NOT rolbypassrls FROM pg_roles WHERE rolname='keycloak'", connection);
            Assert.Equal(true, await sql.ExecuteScalarAsync());
            sql.CommandText = "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname='keycloak'";
            Assert.Equal("keycloak", await sql.ExecuteScalarAsync());
            await using var owner = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(await AdminConnection())
                { Database = "keycloak", Username = "keycloak", Password = dbPassword }.ConnectionString);
            await owner.OpenAsync();
            await using var schema = new NpgsqlCommand("SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname='public'", owner);
            Assert.Equal("keycloak", await schema.ExecuteScalarAsync());
        }
        public async Task AssertDatabaseIsolation()
        {
            await AssertDatabaseRole();
            var mounts = (await ContainerImages.Inspect(Keycloak)).GetProperty("Mounts").EnumerateArray().Select(x => x.GetProperty("Destination").GetString()).ToArray();
            Assert.Equal(new[] { "/run/secrets/idp_db_password", "/run/secrets/idp_admin_password" }.Order(), mounts.Order());
        }
        public async Task<string> InspectRealm()
        {
            var start = new ProcessStartInfo("python3") { WorkingDirectory = ContainerImages.Root, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-c", "import sys; sys.path.insert(0, 'scripts/local'); from realm import AdminSecretFiles, inspect_realm; from pathlib import Path; r=inspect_realm(sys.argv[1], AdminSecretFiles(Path(sys.argv[2])), 8); assert r.reason_token == {'ready':None, 'drift':'LOCAL_REALM_DRIFT', 'unavailable':'LOCAL_IDP_NOT_READY'}[r.status]; assert len(r.correlation_id)==32; print(r.status)", BaseUrl, Path.Combine(directory, "admin-password") }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, "Realm helper failed; raw output withheld.");
            Assert.True((await error).Length == 0, "Realm helper emitted diagnostics; capture withheld.");
            return (await output).Trim();
        }
        public async ValueTask DisposeAsync()
        {
            var leaked = false;
            foreach (var container in new[] { Keycloak, Db })
            {
                var logs = await ContainerImages.Docker(false, "logs", container);
                var capture = logs.Output + logs.Error + (await ContainerImages.Docker(false, "inspect", container)).Output;
                foreach (var secret in new[] { dbAdmin, dbPassword, adminPassword }) leaked |= capture.Contains(secret, StringComparison.Ordinal);
            }
            await ContainerImages.Docker(false, "rm", "--force", "--volumes", Keycloak, Db);
            await ContainerImages.Docker(false, "network", "rm", name);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            Assert.False(leaked, "Sensitive sentinel detected; capture withheld.");
        }
    }
}
