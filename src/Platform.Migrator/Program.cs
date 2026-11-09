using System.Diagnostics;
using Platform.Infrastructure.Local;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Observability;

var correlationId = Guid.NewGuid().ToString("N");
using var activity = Activity.Current is null ? new Activity("crm-migrate.migration").SetIdFormat(ActivityIdFormat.W3C).Start() : null;
if (args.Length > 0)
{
    var local = args.Length == 1 && args[0] == "--local-bootstrap";
    try
    {
        if (!local) throw new ArgumentException();
        string Setting(string name) => Environment.GetEnvironmentVariable("LocalBootstrap__" + name) ?? throw new ArgumentException();
        var options = new LocalBootstrapOptions(Setting("AdminConnectionString"), Setting("DatabaseName"),
            Setting("ProjectId"), Setting("MigratorSecretFile"), Setting("ApplicationSecretFile"));
        await LocalDatabaseBootstrap.RunAsync(options, CancellationToken.None);
        ProcessDiagnostic.Write(Console.Out, "crm-bootstrap", "bootstrap_completed", correlationId, status: "bootstrapped");
        return 0;
    }
    catch (Exception error)
    {
        ProcessDiagnostic.Write(Console.Error, local ? "crm-bootstrap" : "crm-migrate", "bootstrap_failed", correlationId,
            reasonToken: error is LocalSecretStateMismatchException ? "LOCAL_SECRET_STATE_MISMATCH" :
                error is ArgumentException ? "LOCAL_CONFIGURATION_INVALID" : "LOCAL_BOOTSTRAP_FAILED");
        return 1;
    }
}
DatabaseOptions configuration;
var applicationRole = Environment.GetEnvironmentVariable("Database__ApplicationRole");
try
{
    configuration = DatabaseOptions.Parse(Environment.GetEnvironmentVariable("ConnectionStrings__Platform"));
    if (string.IsNullOrWhiteSpace(applicationRole)) throw new ArgumentException();
}
catch
{
    ProcessDiagnostic.Write(Console.Error, "crm-migrate", "configuration_failed", correlationId,
        reasonToken: "DATABASE_CONFIGURATION_INVALID");
    return 1;
}

try
{
    await using var connection = new NpgsqlConnection(configuration.ConnectionString);
    await connection.OpenAsync();
    await DatabaseRoleGrants.ValidateAsync(connection, applicationRole, CancellationToken.None);
    var options = new DbContextOptionsBuilder<PlatformDbContext>()
        .UseNpgsql(connection, provider => provider.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema))
        .Options;
    await using var context = new PlatformDbContext(options);
    await context.Database.MigrateAsync();
    await DatabaseRoleGrants.ApplyAsync(connection, applicationRole, CancellationToken.None);
    ProcessDiagnostic.Write(Console.Out, "crm-migrate", "migration_completed", correlationId, status: "migrated");
    return 0;
}
catch
{
    ProcessDiagnostic.Write(Console.Error, "crm-migrate", "migration_failed", correlationId,
        reasonToken: "DATABASE_MIGRATION_FAILED");
    return 1;
}
