using Platform.Api.Operations;
using Microsoft.EntityFrameworkCore;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<PlatformDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var database = DatabaseOptions.Parse(configuration.GetConnectionString("Platform"));
    options.UseNpgsql(database.ConnectionString, provider =>
        provider.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema));
});
builder.Services.AddScoped<DatabaseReadiness>();
var app = builder.Build();

app.Use(async (context, next) =>
{
    context.TraceIdentifier = Guid.NewGuid().ToString("N");
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
    await next(context);
});

app.MapOperationalEndpoints();
app.Run();

public partial class Program;
