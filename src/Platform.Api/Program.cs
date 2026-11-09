using Platform.Api.Operations;
using Microsoft.EntityFrameworkCore;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Observability;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
PlatformLogging.Configure(builder.Logging, "platform-api");
builder.Services.AddDbContext<PlatformDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var database = DatabaseOptions.Parse(configuration.GetConnectionString("Platform"));
    options.UseNpgsql(database.ConnectionString, provider =>
        provider.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema));
});
builder.Services.AddScoped<DatabaseReadiness>();
var app = builder.Build();

try
{
    // Validate after host configuration is complete, before any listener starts.
    DatabaseOptions.Parse(app.Configuration.GetConnectionString("Platform"));
}
catch (ArgumentException)
{
    using var activity = Activity.Current is null ? new Activity("platform-api.configuration").SetIdFormat(ActivityIdFormat.W3C).Start() : null;
    ProcessDiagnostic.Write(Console.Error, "platform-api", "configuration_failed", Guid.NewGuid().ToString("N"),
        reasonToken: "DATABASE_CONFIGURATION_INVALID");
    await app.DisposeAsync();
    return 1;
}

using (var activity = Activity.Current is null ? new Activity("platform-api.startup").SetIdFormat(ActivityIdFormat.W3C).Start() : null)
using (PlatformLogging.BeginOperation(app.Logger, "platform-api", "startup", Guid.NewGuid().ToString("N")))
    app.Logger.LogInformation("Application configured");

app.UseOperationalErrors();

app.MapOperationalEndpoints();
app.Run();
return 0;

public partial class Program;
