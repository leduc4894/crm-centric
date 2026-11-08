using Platform.Api.Operations;
using Microsoft.EntityFrameworkCore;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
// Provider diagnostics can include connection metadata even without sensitive-data logging.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
builder.Logging.AddFilter("Npgsql", LogLevel.None);
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
    Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        reasonToken = "DATABASE_CONFIGURATION_INVALID", correlationId = Guid.NewGuid().ToString("N")
    }));
    await app.DisposeAsync();
    return 1;
}

app.UseOperationalErrors();

app.MapOperationalEndpoints();
app.Run();
return 0;

public partial class Program;
