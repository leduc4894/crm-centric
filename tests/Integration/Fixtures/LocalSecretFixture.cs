namespace Integration.Fixtures;

public sealed class LocalSecretFixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "crm-bootstrap-" + Guid.NewGuid().ToString("N"));
    public string MigratorFile => Path.Combine(DirectoryPath, "migrator");
    public string ApplicationFile => Path.Combine(DirectoryPath, "application");
    public LocalSecretFixture()
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Directory.CreateDirectory(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var file in new[] { MigratorFile, ApplicationFile })
        {
            File.WriteAllText(file, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) + "'\\");
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
}
