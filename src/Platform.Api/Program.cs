using Platform.Api.Operations;
using Platform.Api.Identity;
using Platform.Api.Crm;
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
System.Net.IPAddress.TryParse(app.Configuration["Invites:TrustedTlsProxy"], out var trustedTlsProxy);
var automaticForwarding = string.Equals(app.Configuration["FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1/invites"))
    {
        // The host's automatic forwarding can rewrite both scheme and peer before this middleware.
        if (automaticForwarding)
        {
            await OperationalErrors.WriteProblemAsync(context, 503, "TLS_CONFIGURATION_INVALID", context.RequestAborted);
            return;
        }
        var peer = context.Connection.RemoteIpAddress;
        var viaTlsProxy = trustedTlsProxy is not null && peer?.MapToIPv6().Equals(trustedTlsProxy.MapToIPv6()) == true
            && context.Request.Headers["X-Forwarded-Proto"] is { Count: 1 } proto && proto[0] == "https";
        var local = app.Environment.IsEnvironment("Testing") || (app.Environment.IsDevelopment() && peer is not null && System.Net.IPAddress.IsLoopback(peer));
        if (!local && !context.Request.IsHttps && !viaTlsProxy)
        {
            await OperationalErrors.WriteProblemAsync(context, 400, "HTTPS_REQUIRED", context.RequestAborted);
            return;
        }
    }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();

app.MapOperationalEndpoints();
app.MapTenantEndpoints();
app.MapMembershipEndpoints();
app.MapInviteEndpoints();
app.MapAuditEndpoints();
app.MapContactEndpoints();
app.MapTagEndpoints();
app.MapTimelineEndpoints();
app.MapCustomFieldEndpoints();
app.Run();
return 0;

public partial class Program;
