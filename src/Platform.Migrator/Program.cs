using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Persistence;

var correlationId = Guid.NewGuid().ToString("N");
DatabaseOptions configuration;
var applicationRole = Environment.GetEnvironmentVariable("Database__ApplicationRole");
try
{
    configuration = DatabaseOptions.Parse(Environment.GetEnvironmentVariable("ConnectionStrings__Platform"));
    if (string.IsNullOrWhiteSpace(applicationRole)) throw new ArgumentException();
}
catch
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { reasonToken = "DATABASE_CONFIGURATION_INVALID", correlationId }));
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
    Console.WriteLine(JsonSerializer.Serialize(new { status = "migrated", correlationId }));
    return 0;
}
catch
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { reasonToken = "DATABASE_MIGRATION_FAILED", correlationId }));
    return 1;
}
