using System.Diagnostics;

namespace Integration.Fixtures;

public static class MigratorProcess
{
    public sealed record Result(int ExitCode, string StandardOutput, string StandardError);

    public static async Task<Result> RunAsync(string? connectionString, string? applicationRole, bool api = false,
        IReadOnlyList<string>? arguments = null, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CrmPlatform.sln")))
            root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root unavailable.");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root.FullName, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var argument in new[] { "run", "--project", api ? "src/Platform.Api" : "src/Platform.Migrator", "--no-build", "-c", "Release" })
            start.ArgumentList.Add(argument);
        if (arguments is { Count: > 0 })
        {
            start.ArgumentList.Add("--");
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
        }
        if (environment is not null)
            foreach (var pair in environment)
                if (pair.Value is null) start.Environment.Remove(pair.Key);
                else start.Environment[pair.Key] = pair.Value;
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        start.Environment.Remove("ConnectionStrings__Platform");
        start.Environment.Remove("Database__ApplicationRole");
        if (connectionString is not null) start.Environment["ConnectionStrings__Platform"] = connectionString;
        if (applicationRole is not null) start.Environment["Database__ApplicationRole"] = applicationRole;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(api ? 8 : 60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Operational process did not exit within its test budget.");
        }
        return new Result(process.ExitCode, await stdout, await stderr);
    }
}
