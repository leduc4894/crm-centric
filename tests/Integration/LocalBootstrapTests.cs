using System.Text.Json;
using System.Diagnostics;
using Integration.Fixtures;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Integration;

public sealed class LocalBootstrapTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder(ContainerImages.Pins["POSTGRES_IMAGE"])
        .WithPassword(Guid.NewGuid().ToString("N")).WithCommand("-c", "log_statement=none", "-c", "log_min_error_statement=panic").Build();
    private readonly LocalSecretFixture secrets = new();
    private readonly string project = "test-" + Guid.NewGuid().ToString("N");
    public Task InitializeAsync() => database.StartAsync();
    public async Task DisposeAsync() { secrets.Dispose(); await database.DisposeAsync(); }
    private string Connection(string? role = null) => new NpgsqlConnectionStringBuilder(database.GetConnectionString())
    { Database = "crm", Username = role ?? "postgres", Password = role is null ? new NpgsqlConnectionStringBuilder(database.GetConnectionString()).Password : File.ReadAllText(role == "crm_app" ? secrets.ApplicationFile : secrets.MigratorFile), Pooling = false }.ConnectionString;
    private Task<MigratorProcess.Result> Bootstrap() => MigratorProcess.RunAsync(null, null, arguments: ["--local-bootstrap"], environment: new Dictionary<string, string?>
    {
        ["LocalBootstrap__AdminConnectionString"] = database.GetConnectionString(), ["LocalBootstrap__DatabaseName"] = "crm", ["LocalBootstrap__ProjectId"] = project,
        ["LocalBootstrap__MigratorSecretFile"] = secrets.MigratorFile, ["LocalBootstrap__ApplicationSecretFile"] = secrets.ApplicationFile
    });
    private async Task Sql(string sql, bool maintenance = false)
    {
        await using var c = new NpgsqlConnection(maintenance ? database.GetConnectionString() : Connection());
        await c.OpenAsync(); await PostgresFixture.ExecuteAsync(c, sql);
    }
    private async Task<bool> Check(string sql, string? role = null)
    {
        await using var c = new NpgsqlConnection(Connection(role)); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql,c); return await cmd.ExecuteScalarAsync() is true;
    }
    private void AssertSafe(MigratorProcess.Result result, bool success)
    {
        var output = result.StandardOutput + result.StandardError;
        foreach (var value in new[] { File.ReadAllText(secrets.MigratorFile), File.ReadAllText(secrets.ApplicationFile), new NpgsqlConnectionStringBuilder(database.GetConnectionString()).Password! })
            Assert.False(output.Contains(value, StringComparison.Ordinal), "Credential appeared in diagnostics.");
        Assert.Equal(success ? 0 : 1, result.ExitCode);
        using var json = JsonDocument.Parse(output);
        Assert.Equal("crm-bootstrap", json.RootElement.GetProperty("service").GetString());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("correlationId").GetString()));
        if (!success) Assert.Equal("LOCAL_SECRET_STATE_MISMATCH", json.RootElement.GetProperty("reasonToken").GetString());
    }
    [Fact]
    public async Task Adapter_bootstrap_reads_only_mounted_files_and_runs_real_cli()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CrmPlatform.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var adminFile = Path.Combine(secrets.DirectoryPath, "admin");
        File.WriteAllText(adminFile, database.GetConnectionString());
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        File.SetUnixFileMode(adminFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var start = new ProcessStartInfo("bash") { WorkingDirectory = Path.Combine(root.FullName, "src/Platform.Migrator/bin/Release/net10.0"), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(root.FullName, "infra/compose/entrypoints/platform.sh"));
        start.ArgumentList.Add("migrator"); start.ArgumentList.Add("--local-bootstrap");
        start.Environment["LOCAL_BOOTSTRAP_ADMIN_CONNECTION_FILE"] = adminFile;
        start.Environment["LOCAL_BOOTSTRAP_MIGRATOR_SECRET_FILE"] = secrets.MigratorFile;
        start.Environment["LOCAL_BOOTSTRAP_APPLICATION_SECRET_FILE"] = secrets.ApplicationFile;
        start.Environment["LocalBootstrap__DatabaseName"] = "crm";
        start.Environment["LocalBootstrap__ProjectId"] = project;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        AssertSafe(new(process.ExitCode, await stdout, await stderr), true);
        Assert.True(await Check("SELECT current_user='crm_app'", "crm_app"));
    }
    [Fact]
    public async Task Bootstrap_is_idempotent_and_restricted()
    {
        var original = File.ReadAllBytes(secrets.ApplicationFile);
        AssertSafe(await Bootstrap(), true);
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(secrets.ApplicationFile));
        Assert.Equal(0, (await MigratorProcess.RunAsync(Connection("crm_migrator"), "crm_app")).ExitCode);
        await Sql("CREATE TABLE public.sentinel(id integer); INSERT INTO public.sentinel VALUES (42)");
        AssertSafe(await Bootstrap(), true);
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(secrets.ApplicationFile)), "Secret changed during warm start.");
        Assert.True(await Check("SELECT EXISTS(SELECT FROM public.sentinel WHERE id=42)"));
        foreach (var role in new[] { "crm_app", "crm_migrator" })
            Assert.True(await Check("""
                SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreaterole AND NOT rolcreatedb AND NOT rolreplication
                AND NOT EXISTS(SELECT FROM pg_auth_members WHERE member=r.oid)
                AND NOT EXISTS(SELECT FROM pg_database WHERE datname=current_database() AND datdba=r.oid)
                AND NOT has_database_privilege(current_user,current_database(),'TEMPORARY')
                AND NOT has_schema_privilege(current_user,'public','CREATE') FROM pg_roles r WHERE rolname=current_user
                """, role));
        var applicationCanCreateSchema = await Check("SELECT has_database_privilege(current_user,current_database(),'CREATE')", "crm_app");
        Assert.False(applicationCanCreateSchema);
        Assert.True(await Check("SELECT has_database_privilege(current_user,current_database(),'CREATE')", "crm_migrator"));
        Assert.True(await Check("SELECT NOT EXISTS(SELECT FROM pg_database d CROSS JOIN LATERAL aclexplode(d.datacl) a WHERE d.datname=current_database() AND a.grantee=0)"));
        Assert.True(await Check("SELECT count(*)=1 FROM platform.\"__EFMigrationsHistory\"", "crm_app"));
        var logs = (await database.GetLogsAsync());
        foreach (var value in new[] { File.ReadAllText(secrets.ApplicationFile), File.ReadAllText(secrets.MigratorFile) })
            Assert.False((logs.Stdout + logs.Stderr).Contains(value, StringComparison.Ordinal), "Credential appeared in database logs.");
    }
    [Fact]
    public async Task Partial_bootstrap_retries_without_reset()
    {
        AssertSafe(await Bootstrap(), true);
        await Sql("CREATE TABLE public.sentinel(id int); INSERT INTO public.sentinel VALUES (42); REVOKE CONNECT ON DATABASE crm FROM crm_app");
        // Only the private durable provisioning marker permits completing grants.
        await Sql("UPDATE _local.bootstrap SET complete=false WHERE database_name='crm'", true);
        AssertSafe(await Bootstrap(), true);
        Assert.True(await Check("SELECT EXISTS(SELECT FROM public.sentinel WHERE id=42)"));
        Assert.True(await Check("SELECT current_user='crm_app'", "crm_app"));
    }
    [Fact]
    public async Task Existing_unowned_database_is_rejected()
    {
        await Sql("CREATE DATABASE crm", true);
        AssertSafe(await Bootstrap(), false);
        Assert.True(await Check("SELECT NOT EXISTS(SELECT FROM pg_roles WHERE rolname IN ('crm_app','crm_migrator'))"));
    }
    [Theory]
    [InlineData("password")]
    [InlineData("acl")]
    [InlineData("membership")]
    [InlineData("schema")]
    public async Task Existing_volume_with_wrong_secret_or_acl_does_not_repair_silently(string drift)
    {
        AssertSafe(await Bootstrap(), true);
        await Sql("CREATE TABLE public.sentinel(id int); INSERT INTO public.sentinel VALUES (42)");
        if (drift == "password") File.WriteAllText(secrets.ApplicationFile, Guid.NewGuid().ToString("N"));
        if (drift == "acl") await Sql("GRANT CREATE ON DATABASE crm TO crm_app");
        if (drift == "schema") await Sql("CREATE SCHEMA platform; GRANT CREATE ON SCHEMA platform TO crm_app");
        if (drift == "membership") await Sql("GRANT pg_read_all_data TO crm_app");
        AssertSafe(await Bootstrap(), false);
        Assert.True(await Check("SELECT EXISTS(SELECT FROM public.sentinel WHERE id=42)"));
        if (drift == "acl") Assert.True(await Check("SELECT has_database_privilege('crm_app',current_database(),'CREATE')"));
    }
}
