using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Integration.Fixtures;

// Captures all CLI output privately; failures expose operation/exit category only.
public sealed class ContainerImages : IAsyncLifetime
{
    public static string Root { get; } = FindRoot();
    static ContainerImages()
    {
        // CI has explicit owner-label cleanup, so no unobserved Ryuk sidecar runs.
        if (Environment.GetEnvironmentVariable("CI_RESOURCE_OWNER") is not null)
            DotNet.Testcontainers.Configurations.TestcontainersSettings.ResourceReaperEnabled = false;
    }
    public string Api { get; } = "crm-task3-api-" + Guid.NewGuid().ToString("N");
    public string Migrator { get; } = "crm-task3-migrate-" + Guid.NewGuid().ToString("N");
    public string Keycloak { get; } = "crm-task3-idp-" + Guid.NewGuid().ToString("N");
    public string Postgres { get; } = "crm-task3-postgres-" + Guid.NewGuid().ToString("N");
    public static Dictionary<string, string> Pins => File.ReadAllLines(Path.Combine(Root, "infra/compose/images.lock.env"))
        .Where(line => line.Length > 0 && !line.StartsWith('#')).Select(line => line.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1]);

    public async Task InitializeAsync()
    {
        var duration = Stopwatch.StartNew();
        foreach (var (target, image) in new[] { ("api", Api), ("migrator", Migrator) })
            await Docker("build", "--file", "infra/docker/Platform.Dockerfile", "--target", target, "--build-arg", "DOTNET_SDK_IMAGE=" + Pins["DOTNET_SDK_IMAGE"], "--build-arg", "DOTNET_RUNTIME_IMAGE=" + Pins["DOTNET_RUNTIME_IMAGE"], "--tag", image, ".");
        await Docker("build", "--file", "infra/docker/Postgres.Dockerfile", "--target", "runtime", "--build-arg", "POSTGRES_IMAGE=" + Pins["POSTGRES_IMAGE"], "--tag", Postgres, ".");
        await Docker("build", "--file", "infra/docker/Keycloak.Dockerfile", "--target", "runtime", "--build-arg", "KEYCLOAK_IMAGE=" + Pins["KEYCLOAK_IMAGE"], "--tag", Keycloak, ".");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "crm-task3-build-timing.json"), JsonSerializer.Serialize(new { cachedBuildSeconds = duration.Elapsed.TotalSeconds, images = 4 }));
    }

    public async Task DisposeAsync()
    {
        await Docker(false, "image", "rm", "--force", Api, Migrator, Keycloak, Postgres);
    }

    public static Task<Capture> Docker(params string[] args) => Docker(true, args);
    public static Task<Capture> Docker(bool requireSuccess, params string[] args) => Docker(requireSuccess, args, null);
    public static async Task<Capture> Docker(bool requireSuccess, string[] args, IReadOnlyDictionary<string, string>? fixtureSecretFiles)
    {
        var evidence = Environment.GetEnvironmentVariable("CI_IMAGE_EVIDENCE") is not null && args[0] == "run";
        var fixtureSecrets = evidence ? FixtureSecretValues(args, fixtureSecretFiles) : Array.Empty<string>();
        var removeAfter = evidence && args.Contains("--rm");
        var cid = evidence ? Path.Combine(Path.GetTempPath(), "crm-ci-container-" + Guid.NewGuid().ToString("N")) : null;
        if (evidence) args = new[] { "run", "--cidfile", cid! }.Concat(args.Skip(1).Where(arg => arg != "--rm")).ToArray();
        var owner = Environment.GetEnvironmentVariable("CI_RESOURCE_OWNER");
        if (args[0] == "run" && owner is not null)
            args = new[] { "run", "--label", "io.crm-centric.ci-owner=" + owner }.Concat(args.Skip(1)).ToArray();
        var start = new ProcessStartInfo("docker") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Container operation exceeded test deadline.");
        }
        var result = new Capture(process.ExitCode, await stdout, await stderr);
        if (cid is not null)
        {
            try
            {
                if (File.Exists(cid))
                {
                    var container = File.ReadAllText(cid).Trim();
                    await CaptureImageEvidence(container, fixtureSecrets);
                }
            }
            finally
            {
                if (removeAfter && File.Exists(cid)) await Docker(false, "container", "rm", "--force", File.ReadAllText(cid).Trim());
                File.Delete(cid);
            }
        }
        if (requireSuccess && result.ExitCode != 0) SavePrivateCapture("failed-" + args[0], result.Output + result.Error);
        if (requireSuccess) Assert.True(result.ExitCode == 0, "Container operation failed: " + args[0] + " exit " + result.ExitCode);
        return result;
    }

    public static string[] FixtureSecretValues(string[] arguments, IReadOnlyDictionary<string, string>? fixtureSecretFiles = null)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            value = value.Trim(); values.Add(value);
            if (value.Contains("Password=", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var password = new Npgsql.NpgsqlConnectionStringBuilder(value).Password;
                    if (!string.IsNullOrEmpty(password)) values.Add(password);
                }
                catch (ArgumentException)
                {
                    throw new InvalidOperationException("CI_SECRET_EXTRACTION_INCOMPLETE correlation=" + Guid.NewGuid().ToString("N"));
                }
            }
        }
        void SecretFile(string source)
        {
            // Negative fixtures can deliberately revoke access after creating a
            // secret. Preserve their explicit pre-revocation snapshot privately;
            // also capture current bytes whenever readable. Never change modes.
            string? snapshot = null;
            fixtureSecretFiles?.TryGetValue(Path.GetFullPath(source), out snapshot);
            Add(snapshot);
            try
            {
                // Do not let a privileged CI process turn an intentionally
                // unreadable fixture into a readable one. The guard must fail
                // closed from the file mode as well as from the read attempt.
                if (OperatingSystem.IsLinux() &&
                    (File.GetUnixFileMode(source) & (UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0)
                    throw new UnauthorizedAccessException();
                Add(File.ReadAllText(source));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                if (string.IsNullOrWhiteSpace(snapshot))
                    throw new InvalidOperationException("CI_SECRET_EXTRACTION_INCOMPLETE correlation=" + Guid.NewGuid().ToString("N"));
            }
        }
        void Mount(string mount)
        {
            var parts = mount.Split(',').Select(part => part.Split('=', 2))
                .Where(part => part.Length == 2).ToDictionary(part => part[0], part => part[1], StringComparer.Ordinal);
            if (parts.GetValueOrDefault("type") == "bind" &&
                parts.GetValueOrDefault("target", parts.GetValueOrDefault("dst", "")).StartsWith("/run/secrets/", StringComparison.Ordinal) &&
                parts.GetValueOrDefault("source", parts.GetValueOrDefault("src", "")) is { } source && File.Exists(source))
                SecretFile(source);
        }
        void EnvironmentValue(string entry)
        {
            var parts = entry.Split('=', 2); var name = parts[0];
            if (!name.EndsWith("_FILE", StringComparison.OrdinalIgnoreCase) &&
                new[] { "PASSWORD", "TOKEN", "SECRET", "CONNECTION" }.Any(key => name.Contains(key, StringComparison.OrdinalIgnoreCase)))
                Add(parts.Length == 2 ? parts[1] : Environment.GetEnvironmentVariable(name));
        }
        for (var index = 0; index < arguments.Length; index++)
        {
            var arg = arguments[index];
            if (arg is "--mount" && index + 1 < arguments.Length) Mount(arguments[++index]);
            else if (arg.StartsWith("--mount=", StringComparison.Ordinal)) Mount(arg[8..]);
            else if (arg is "--env" or "-e" && index + 1 < arguments.Length) EnvironmentValue(arguments[++index]);
            else if (arg.StartsWith("--env=", StringComparison.Ordinal)) EnvironmentValue(arg[6..]);
            else if (arg.StartsWith("--ConnectionStrings:", StringComparison.OrdinalIgnoreCase))
            {
                var separator = arg.IndexOf('=');
                if (separator >= 0) Add(arg[(separator + 1)..]);
            }
            else if (arg is "--volume" or "-v" && index + 1 < arguments.Length)
            {
                var mount = arguments[++index].Split(':');
                if (mount.Length >= 2 && mount[1].StartsWith("/run/secrets/", StringComparison.Ordinal) && File.Exists(mount[0]))
                    SecretFile(mount[0]);
            }
        }
        return values.ToArray();
    }

    public static async Task CaptureImageEvidence(string container, params string[] secrets)
    {
        if (Environment.GetEnvironmentVariable("CI_IMAGE_EVIDENCE") is null) return;
        var start = new ProcessStartInfo("python3") { WorkingDirectory = Root, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-m"); start.ArgumentList.Add("scripts.ci.image_identity");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { singleContainer = container, secretValues = secrets }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            // TERM lets the helper dispose its scanner groups before forced kill.
            using var terminate = Process.Start(new ProcessStartInfo("kill", "-TERM " + process.Id) { RedirectStandardOutput = true, RedirectStandardError = true });
            if (terminate is not null) await terminate.WaitForExitAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) { process.Kill(true); await process.WaitForExitAsync(); }
            throw new InvalidOperationException("CI_IMAGE_EVIDENCE_INCOMPLETE correlation=" + Guid.NewGuid().ToString("N"));
        }
        await stdout; await stderr;
        Assert.True(process.ExitCode == 0, "CI_IMAGE_EVIDENCE_INCOMPLETE correlation=" + Guid.NewGuid().ToString("N"));
    }

    public static void SavePrivateCapture(string operation, string capture)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var directory = Path.Combine(Path.GetTempPath(), "crm-task3-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var stream = new FileStream(Path.Combine(directory, operation + ".txt"), new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
        using var writer = new StreamWriter(stream);
        writer.Write(capture);
    }

    public static async Task<JsonElement> Inspect(string name)
    {
        var capture = await Docker("inspect", name);
        using var json = JsonDocument.Parse(capture.Output);
        return json.RootElement[0].Clone();
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CrmPlatform.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root unavailable.");
    }
    public sealed record Capture(int ExitCode, string Output, string Error);
}
