using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Integration.Fixtures;
using Npgsql;
using Xunit;

namespace Integration;

[CollectionDefinition("Container runtime", DisableParallelization = true)]
public sealed class ContainerRuntimeCollection : ICollectionFixture<ContainerImages>;

[Collection("Container runtime")]
public sealed class ContainerRuntimeTests(ContainerImages images)
{
    // Catches adapters ignoring file settings, running as root, and granting extra mounted credentials.
    [Fact]
    public async Task Api_uses_only_application_secret_as_nonroot()
    {
        await using var db = new PostgresFixture();
        await db.InitializeAsync();
        var network = "crm-task3-" + Guid.NewGuid().ToString("N");
        var api = network + "-api";
        var migrate = network + "-migrate";
        using var secrets = new SecretDirectory();
        var application = secrets.Write("application", Connection(db.ApplicationConnectionString, db.Container.Name));
        var migration = secrets.Write("migration", Connection(db.MigrationConnectionString, db.Container.Name));
        var admin = secrets.Write("admin", Guid.NewGuid().ToString("N"));
        await ContainerImages.Docker("network", "create", network);
        try
        {
            await ContainerImages.Docker("network", "connect", network, db.Container.Name);
            var migrationResult = await ContainerImages.Docker(false, "run", "--name", migrate, "--network", network,
                "--mount", Mount(migration, "/run/secrets/platform"), "--env", "PLATFORM_CONNECTION_FILE=/run/secrets/platform",
                "--env", "Database__ApplicationRole=" + db.ApplicationRole, images.Migrator);
            if (migrationResult.ExitCode != 0)
            {
                ContainerImages.SavePrivateCapture("migration-failure", migrationResult.Output + migrationResult.Error);
                Assert.Fail("Built migrator failed; private diagnostic retained.");
            }
            await ContainerImages.Docker("run", "--detach", "--name", api, "--network", network,
                "--publish", "127.0.0.1::8080", "--mount", Mount(application, "/run/secrets/platform"),
                "--env", "PLATFORM_CONNECTION_FILE=/run/secrets/platform", images.Api);
            var inspect = await ContainerImages.Inspect(api);
            var apiMountedSecretFiles = inspect.GetProperty("Mounts").EnumerateArray().Select(x => x.GetProperty("Source").GetString()).ToArray();
            Assert.Equal(new[] { application }, apiMountedSecretFiles);
            Assert.DoesNotContain(admin, apiMountedSecretFiles);
            Assert.DoesNotContain(migration, apiMountedSecretFiles);
            Assert.False(inspect.GetProperty("Mounts")[0].GetProperty("RW").GetBoolean());
            var port = inspect.GetProperty("NetworkSettings").GetProperty("Ports").GetProperty("8080/tcp")[0].GetProperty("HostPort").GetString();
            using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(5) };
            await WaitReady(client);
            var apiLogs = await ContainerImages.Docker("logs", api);
            var password = new NpgsqlConnectionStringBuilder(db.ApplicationConnectionString).Password!;
            AssertAbsent(password, inspect.GetRawText() + apiLogs.Output + apiLogs.Error);
            var migrationInspect = await ContainerImages.Inspect(migrate);
            AssertAbsent(new NpgsqlConnectionStringBuilder(db.MigrationConnectionString).Password!, migrationInspect.GetRawText());
            var runtimeUid = (await ContainerImages.Docker("exec", api, "id", "-u")).Output.Trim();
            Assert.NotEqual("0", runtimeUid);
            Assert.Equal("1000", runtimeUid);
            Assert.Equal("600", (await ContainerImages.Docker("exec", api, "stat", "-c", "%a", "/run/secrets/platform")).Output.Trim());
            var process = (await ContainerImages.Docker("exec", api, "cat", "/proc/1/status")).Output;
            Assert.Contains("Uid:\t1000\t1000\t1000\t1000", process);
            await ContainerImages.Docker("exec", api, "/usr/local/bin/healthcheck.sh");
            // Stopping this exact dependency makes this exact container's probe fail.
            await db.Container.StopAsync();
            var probeTime = Stopwatch.StartNew();
            var probe = await ContainerImages.Docker(false, "exec", api, "/usr/local/bin/healthcheck.sh");
            Assert.True(probeTime.Elapsed < TimeSpan.FromSeconds(5), "Self probe exceeded the five-second budget.");
            Assert.NotEqual(0, probe.ExitCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
        finally
        {
            await ContainerImages.Docker(false, "rm", "--force", api, migrate);
            await ContainerImages.Docker(false, "network", "disconnect", network, db.Container.Name);
            await ContainerImages.Docker(false, "network", "rm", network);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unreadable")]
    [InlineData("empty")]
    [InlineData("public")]
    public async Task Unreadable_or_missing_secret_fails_closed(string state)
    {
        using var secrets = new SecretDirectory();
        var sentinel = Guid.NewGuid().ToString("N");
        var file = secrets.Write("application", state == "empty" ? "" : sentinel);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (state == "unreadable") File.SetUnixFileMode(file, UnixFileMode.None);
        if (state == "public") File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        foreach (var (image, service) in new[] { (images.Api, "crm-api"), (images.Migrator, "crm-migrate") })
        {
            var args = new List<string> { "run", "--rm", "--env", "PLATFORM_CONNECTION_FILE=/run/secrets/platform" };
            if (state != "missing") args.AddRange(["--mount", Mount(file, "/run/secrets/platform")]);
            args.Add(image);
            var capture = await ContainerImages.Docker(false, args.ToArray());
            Assert.NotEqual(0, capture.ExitCode);
            AssertAbsent(sentinel, capture.Output + capture.Error);
            using var json = JsonDocument.Parse(capture.Error);
            Assert.Equal("LOCAL_CONFIGURATION_INVALID", json.RootElement.GetProperty("reasonToken").GetString());
            Assert.Equal(service, json.RootElement.GetProperty("service").GetString());
            Assert.True(Guid.TryParseExact(json.RootElement.GetProperty("correlationId").GetString(), "N", out _));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Arguments_cannot_override_file_configuration(bool api)
    {
        using var secrets = new SecretDirectory();
        var file = secrets.Write("application", "Host=localhost;Database=unused;Username=unused");
        var sentinel = Guid.NewGuid().ToString("N");
        var result = await ContainerImages.Docker(false, "run", "--rm", "--mount", Mount(file, "/run/secrets/platform"),
            "--env", "PLATFORM_CONNECTION_FILE=/run/secrets/platform", api ? images.Api : images.Migrator,
            "--ConnectionStrings:Platform=" + sentinel);
        Assert.NotEqual(0, result.ExitCode);
        AssertAbsent(sentinel, result.Output + result.Error);
        using var json = JsonDocument.Parse(result.Error);
        Assert.Equal("LOCAL_CONFIGURATION_INVALID", json.RootElement.GetProperty("reasonToken").GetString());
        Assert.Equal(api ? "crm-api" : "crm-migrate", json.RootElement.GetProperty("service").GetString());
        Assert.True(Guid.TryParseExact(json.RootElement.GetProperty("correlationId").GetString(), "N", out _));
    }

    [Fact]
    public async Task Published_images_contain_no_sdk_source_or_fixture()
    {
        foreach (var image in new[] { images.Api, images.Migrator })
        {
            var capture = await ContainerImages.Docker("run", "--rm", "--entrypoint", "bash", image, "-c",
                "test ! -d /usr/share/dotnet/sdk && test ! -d /build && test ! -d /src && ! find /app -type f | /usr/bin/grep -E '(Integration|FakeOidcIssuer|TestHost|Testcontainers|xunit|\\.cs$|\\.csproj$)' && dotnet --list-runtimes");
            Assert.Contains("Microsoft.NETCore.App 10.0.14", capture.Output);
        }
    }

    [Fact]
    public async Task Image_config_history_and_build_context_exclude_sentinels()
    {
        var sentinel = Guid.NewGuid().ToString("N");
        var paths = new[] { ".env.task3", ".git/task3-sentinel", ".venv/task3-sentinel", "tests/task3-sentinel", ".local/task3/secret", ".superpowers/task3-secret", "src/Platform.Api/bin/task3-secret", "src/Platform.Api/secrets.json", "src/Platform.Api/appsettings.Local.json" };
        var createdPaths = new List<string>();
        var audit = "crm-task3-audit-" + Guid.NewGuid().ToString("N");
        using var scratch = new SecretDirectory();
        var dockerfile = scratch.Write("audit.Dockerfile", "FROM " + ContainerImages.Pins["DOTNET_RUNTIME_IMAGE"] + "\nCOPY . /context/\n");
        try
        {
            foreach (var path in paths)
            {
                var full = Path.Combine(ContainerImages.Root, path);
                Assert.False(File.Exists(full), "Sentinel path unexpectedly exists.");
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, sentinel);
                createdPaths.Add(full);
            }
            await ContainerImages.Docker("build", "--file", dockerfile, "--tag", audit, ".");
            await ContainerImages.Docker("build", "--file", "infra/docker/Platform.Dockerfile", "--target", "api", "--tag", images.Api, ".");
            await ContainerImages.Docker("build", "--file", "infra/docker/Platform.Dockerfile", "--target", "migrator", "--tag", images.Migrator, ".");
            foreach (var image in new[] { images.Api, images.Migrator, images.Keycloak, images.Postgres, audit })
            {
                AssertAbsent(sentinel, (await ContainerImages.Docker("image", "inspect", image)).Output);
                AssertAbsent(sentinel, (await ContainerImages.Docker("history", "--no-trunc", image)).Output);
                var archive = Path.Combine(scratch.Path, "image.tar");
                await ContainerImages.Docker("image", "save", "--output", archive, image);
                await using var stream = File.OpenRead(archive);
                using var tar = new TarReader(stream);
                while (await tar.GetNextEntryAsync() is { } entry)
                {
                    if (entry.DataStream is null) continue;
                    // Docker OCI exports may have gzip layer blobs; scan decompressed content too.
                    using var data = new MemoryStream();
                    await entry.DataStream.CopyToAsync(data);
                    data.Position = 0;
                    if (data.Length > 2 && data.GetBuffer()[0] == 0x1f && data.GetBuffer()[1] == 0x8b)
                    {
                        using var gzip = new GZipStream(data, CompressionMode.Decompress);
                        await AssertStreamAbsent(sentinel, gzip);
                    }
                    else await AssertStreamAbsent(sentinel, data);
                }
                File.Delete(archive);
            }
        }
        finally
        {
            foreach (var path in createdPaths) File.Delete(path);
            await ContainerImages.Docker(false, "image", "rm", "--force", audit);
        }
    }

    [Fact]
    public async Task Postgres_missing_file_has_safe_diagnostic()
    {
        var result = await ContainerImages.Docker(false, "run", "--rm", "--env", "POSTGRES_PASSWORD_FILE=/run/secrets/missing", images.Postgres);
        Assert.NotEqual(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.Equal("LOCAL_CONFIGURATION_INVALID", json.RootElement.GetProperty("reasonToken").GetString());
        Assert.True(Guid.TryParseExact(json.RootElement.GetProperty("correlationId").GetString(), "N", out _));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("invalid")]
    public async Task Root_or_invalid_uid_cannot_build_runtime(string uid)
    {
        var tag = "crm-task3-invalid-" + Guid.NewGuid().ToString("N");
        try
        {
            var result = await ContainerImages.Docker(false, "build", "--file", "infra/docker/Platform.Dockerfile", "--target", "api", "--build-arg", "LOCAL_UID=" + uid, "--tag", tag, ".");
            Assert.NotEqual(0, result.ExitCode);
        }
        finally { await ContainerImages.Docker(false, "image", "rm", "--force", tag); }
    }

    [Fact]
    public async Task Postgres_reads_private_file_then_uses_canonical_privilege_drop()
    {
        var name = "crm-task3-pg-" + Guid.NewGuid().ToString("N");
        using var secrets = new SecretDirectory();
        var sentinel = Guid.NewGuid().ToString("N");
        var file = secrets.Write("password", sentinel);
        try
        {
            await ContainerImages.Docker("run", "--detach", "--name", name, "--publish", "127.0.0.1::5432",
                "--mount", Mount(file, "/run/secrets/password"), "--env", "POSTGRES_PASSWORD_FILE=/run/secrets/password", images.Postgres);
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if ((await ContainerImages.Docker(false, "exec", name, "pg_isready", "--host", "127.0.0.1", "--username", "postgres")).ExitCode == 0) break;
                await Task.Delay(200);
            }
            var inspect = await ContainerImages.Inspect(name);
            var port = inspect.GetProperty("NetworkSettings").GetProperty("Ports").GetProperty("5432/tcp")[0].GetProperty("HostPort").GetString();
            await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = int.Parse(port!), Database = "postgres", Username = "postgres", Password = sentinel }.ConnectionString);
            await connection.OpenAsync();
            Assert.Contains("Uid:\t999\t999\t999\t999", (await ContainerImages.Docker("exec", name, "cat", "/proc/1/status")).Output);
            Assert.Equal("600", (await ContainerImages.Docker("exec", name, "stat", "-c", "%a", "/run/secrets/password")).Output.Trim());
            AssertAbsent(sentinel, inspect.GetRawText());
            AssertAbsent(sentinel, (await ContainerImages.Docker("logs", name)).Output);
            await ContainerImages.Docker("restart", name);
            await ContainerImages.Docker("exec", name, "pg_isready", "--host", "127.0.0.1", "--username", "postgres");
        }
        finally { await ContainerImages.Docker(false, "rm", "--force", "--volumes", name); }
    }

    [Fact]
    public async Task Keycloak_private_files_and_data_work_as_nonroot_with_management_health()
    {
        await using var db = new PostgresFixture();
        await db.InitializeAsync();
        var name = "crm-task3-kc-" + Guid.NewGuid().ToString("N");
        using var secrets = new SecretDirectory();
        var dbPassword = new NpgsqlConnectionStringBuilder(db.Container.GetConnectionString()).Password!;
        var password = secrets.Write("db-password", dbPassword);
        var adminValue = Guid.NewGuid().ToString("N");
        var admin = secrets.Write("admin-password", adminValue);
        await ContainerImages.Docker("network", "create", name);
        try
        {
            await ContainerImages.Docker("network", "connect", name, db.Container.Name);
            await ContainerImages.Docker("run", "--detach", "--name", name, "--network", name,
                "--mount", Mount(password, "/run/secrets/db-password"), "--mount", Mount(admin, "/run/secrets/admin-password"),
                "--env", "KC_DB_URL=jdbc:postgresql://" + db.Container.Name.TrimStart('/') + ":5432/postgres", "--env", "KC_DB_USERNAME=postgres",
                "--env", "KC_DB_PASSWORD_FILE=/run/secrets/db-password", "--env", "KC_BOOTSTRAP_ADMIN_USERNAME=local-admin",
                "--env", "KC_BOOTSTRAP_ADMIN_PASSWORD_FILE=/run/secrets/admin-password", images.Keycloak, "start-dev", "--hostname=http://localhost:8080");
            var healthy = false;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if ((await ContainerImages.Docker(false, "exec", name, "/usr/local/bin/keycloak-healthcheck.sh")).ExitCode == 0) { healthy = true; break; }
                if (!(await ContainerImages.Inspect(name)).GetProperty("State").GetProperty("Running").GetBoolean()) break;
                await Task.Delay(500);
            }
            if (!healthy)
            {
                var failure = await ContainerImages.Docker("logs", name);
                ContainerImages.SavePrivateCapture("keycloak-startup", failure.Output + failure.Error);
            }
            Assert.True(healthy, "Keycloak management health did not become ready.");
            await using var idp = new NpgsqlConnection(db.Container.GetConnectionString());
            await idp.OpenAsync();
            await using var realm = new NpgsqlCommand("SELECT count(*) FROM realm WHERE name = 'master'", idp);
            Assert.Equal(1L, await realm.ExecuteScalarAsync());
            Assert.Equal("1000", (await ContainerImages.Docker("exec", name, "id", "-u")).Output.Trim());
            Assert.Contains("Uid:\t1000\t1000\t1000\t1000", (await ContainerImages.Docker("exec", name, "cat", "/proc/1/status")).Output);
            await ContainerImages.Docker("exec", name, "bash", "-c", "test -w /opt/keycloak/data && touch /opt/keycloak/data/uid-test && test -r /run/secrets/db-password && test -r /run/secrets/admin-password");
            var inspect = (await ContainerImages.Inspect(name)).GetRawText();
            var logs = await ContainerImages.Docker("logs", name);
            AssertAbsent(dbPassword, inspect + logs.Output + logs.Error);
            AssertAbsent(adminValue, inspect + logs.Output + logs.Error);
        }
        finally
        {
            await ContainerImages.Docker(false, "rm", "--force", name);
            await ContainerImages.Docker(false, "network", "disconnect", name, db.Container.Name);
            await ContainerImages.Docker(false, "network", "rm", name);
        }
    }

    private static async Task AssertStreamAbsent(string sentinel, Stream stream)
    {
        var buffer = new byte[65536];
        var previous = "";
        int count;
        while ((count = await stream.ReadAsync(buffer)) > 0)
        {
            var current = previous + Encoding.Latin1.GetString(buffer, 0, count);
            AssertAbsent(sentinel, current);
            previous = current[^Math.Min(current.Length, sentinel.Length)..];
        }
    }
    private static void AssertAbsent(string sentinel, string capture) => Assert.False(capture.Contains(sentinel, StringComparison.Ordinal), "Sensitive sentinel detected; raw capture withheld.");
    private static string Mount(string source, string target) => "type=bind,source=" + source + ",target=" + target + ",readonly";
    private static string Connection(string connection, string host) => new NpgsqlConnectionStringBuilder(connection) { Host = host.TrimStart('/'), Port = 5432 }.ConnectionString;
    private static async Task WaitReady(HttpClient client)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try { if ((await client.GetAsync("/health/ready")).StatusCode == HttpStatusCode.OK) return; }
            catch (HttpRequestException) { /* Expected until the listener starts; bounded below. */ }
            await Task.Delay(200);
        }
        Assert.Fail("Built API image did not become ready within deadline.");
    }

    private sealed class SecretDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "crm-task3-" + Guid.NewGuid().ToString("N"));
        public SecretDirectory()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            Directory.CreateDirectory(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path));
        }
        public string Write(string name, string value)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            var file = System.IO.Path.Combine(Path, name);
            using (var stream = new FileStream(file, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
            using (var writer = new StreamWriter(stream)) writer.Write(value);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            return file;
        }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
