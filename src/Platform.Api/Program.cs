using Platform.Api.Operations;
using Platform.Api.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Observability;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
PlatformLogging.Configure(builder.Logging, "crm-api");
builder.Services.AddDbContext<PlatformDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var database = DatabaseOptions.Parse(configuration.GetConnectionString("Platform"));
    options.UseNpgsql(database.ConnectionString, provider =>
        provider.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema));
});
builder.Services.AddScoped<DatabaseReadiness>();
builder.Services.AddPlatformAuthentication(builder.Configuration);
var app = builder.Build();

try
{
    // Validate after host configuration is complete, before any listener starts.
    DatabaseOptions.Parse(app.Configuration.GetConnectionString("Platform"));

}
catch (ArgumentException)
{
    using var activity = Activity.Current is null ? new Activity("crm-api.configuration").SetIdFormat(ActivityIdFormat.W3C).Start() : null;
    ProcessDiagnostic.Write(Console.Error, "crm-api", "configuration_failed", Guid.NewGuid().ToString("N"),
        reasonToken: "DATABASE_CONFIGURATION_INVALID");
    await app.DisposeAsync();
    return 1;
}

try
{
    OidcOptions.Parse(app.Configuration, app.Environment);
}
catch (ArgumentException)
{
    using var activity = Activity.Current is null ? new Activity("crm-api.configuration").SetIdFormat(ActivityIdFormat.W3C).Start() : null;
    ProcessDiagnostic.Write(Console.Error, "crm-api", "configuration_failed", Guid.NewGuid().ToString("N"),
        reasonToken: "OIDC_CONFIGURATION_INVALID");
    await app.DisposeAsync();
    return 1;
}

using (var activity = Activity.Current is null ? new Activity("crm-api.startup").SetIdFormat(ActivityIdFormat.W3C).Start() : null)
using (PlatformLogging.BeginOperation(app.Logger, "crm-api", "startup", Guid.NewGuid().ToString("N")))
    app.Logger.LogInformation("Application configured");

app.UseOperationalErrors();
app.UseAuthentication();
app.UseAuthorization();

app.MapOperationalEndpoints();
app.MapTenantEndpoints();
app.MapMembershipEndpoints();
app.Run();
return 0;

public partial class Program;
