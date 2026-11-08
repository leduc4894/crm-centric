using System.Text.Json;
using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed class MigrationTests : IAsyncLifetime
{
    private readonly PostgresFixture database = new();
    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task Up_creates_only_technical_schema_and_history()
    {
        Assert.Equal(0, (await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole)).ExitCode);
        await AssertHistoryAsync();
    }

    [Fact]
    public async Task Up_twice_is_idempotent()
    {
        Assert.Equal(0, (await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole)).ExitCode);
        Assert.Equal(0, (await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole)).ExitCode);
        await AssertHistoryAsync();
    }

    [Fact]
    public async Task Down_then_up_preserves_unowned_objects()
    {
        Assert.Equal(0, (await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole)).ExitCode);
        await using var connection = new NpgsqlConnection(database.MigrationConnectionString);
        await connection.OpenAsync();
        await PostgresFixture.ExecuteAsync(connection, "CREATE TABLE platform.sentinel(id int PRIMARY KEY); INSERT INTO platform.sentinel VALUES (42)");
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connection, provider => provider.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema))
            .Options;
        await using var context = new PlatformDbContext(options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await context.GetService<IMigrator>().MigrateAsync("0", timeout.Token);

        await AssertHistoryCountAsync(connection, 0);
        await AssertSentinelAsync(connection);
        Assert.Equal(0, (await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole)).ExitCode);
        await AssertHistoryCountAsync(connection, 1);
        await AssertSentinelAsync(connection);
    }

    [Fact]
    public async Task Ddl_failure_never_reports_success()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await using var migrationConnection = new NpgsqlConnection(database.MigrationConnectionString);
        await migrationConnection.OpenAsync();
        await using (var boundary = new NpgsqlCommand("""
            SELECT NOT rolsuper AND NOT rolbypassrls
              AND NOT EXISTS (SELECT FROM pg_database WHERE datname=current_database() AND datdba=r.oid)
              AND has_database_privilege(current_user, current_database(), 'CREATE')
            FROM pg_roles r WHERE rolname=current_user
            """, migrationConnection))
        {
            Assert.Equal(true, await boundary.ExecuteScalarAsync());
        }
        await PostgresFixture.ExecuteAsync(admin, $"REVOKE CREATE ON DATABASE {PostgresFixture.Quote(database.DatabaseName)} FROM {PostgresFixture.Quote(database.MigrationRole)}");

        AssertMigrationFailure(await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole));
        // Failure occurs before EF can create its history table or commit an initial row.
        await using (var history = new NpgsqlCommand("SELECT to_regclass('platform.\"__EFMigrationsHistory\"') IS NULL", admin))
            Assert.Equal(true, await history.ExecuteScalarAsync());

        await PostgresFixture.ExecuteAsync(admin, $"GRANT CREATE ON DATABASE {PostgresFixture.Quote(database.DatabaseName)} TO {PostgresFixture.Quote(database.MigrationRole)}");
        Assert.Equal(0, (await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole)).ExitCode);
        Assert.Equal(0, (await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole)).ExitCode);
        await AssertHistoryAsync();
    }

    [Fact]
    public async Task Missing_application_role_fails_safely()
    {
        AssertMigrationFailure(await MigratorProcess.RunAsync(database.MigrationConnectionString, "missing_" + Guid.NewGuid().ToString("N")));
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        // Role preflight fails before migration, so this failure leaves no partial provisioning.
        await using (var history = new NpgsqlCommand("SELECT to_regclass('platform.\"__EFMigrationsHistory\"') IS NULL", admin))
            Assert.Equal(true, await history.ExecuteScalarAsync());

        Assert.Equal(0, (await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole)).ExitCode);
        Assert.Equal(0, (await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole)).ExitCode);
        await AssertHistoryAsync();
    }

    private static void AssertMigrationFailure(MigratorProcess.Result result)
    {
        Assert.Equal(1, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
        using var error = JsonDocument.Parse(result.StandardError);
        Assert.Equal("DATABASE_MIGRATION_FAILED", error.RootElement.GetProperty("reasonToken").GetString());
        Assert.True(Guid.TryParseExact(error.RootElement.GetProperty("correlationId").GetString(), "N", out var correlationId));
        Assert.NotEqual(Guid.Empty, correlationId);
    }

    private static async Task AssertHistoryCountAsync(NpgsqlConnection connection, long expected)
    {
        await using var history = new NpgsqlCommand("SELECT count(*) FROM platform.\"__EFMigrationsHistory\"", connection);
        Assert.Equal(expected, await history.ExecuteScalarAsync());
    }

    private static async Task AssertSentinelAsync(NpgsqlConnection connection)
    {
        await using var sentinel = new NpgsqlCommand("SELECT id FROM platform.sentinel", connection);
        Assert.Equal(42, await sentinel.ExecuteScalarAsync());
    }

    private async Task AssertHistoryAsync()
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using (var schemas = new NpgsqlCommand("SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname <> 'information_schema' ORDER BY nspname", connection))
        await using (var reader = await schemas.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("platform", reader.GetString(0));
            Assert.True(await reader.ReadAsync());
            Assert.Equal("public", reader.GetString(0));
            Assert.False(await reader.ReadAsync());
        }
        await using var tables = new NpgsqlCommand("SELECT schemaname || '.' || tablename FROM pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema')", connection);
        await using (var reader = await tables.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("platform.__EFMigrationsHistory", reader.GetString(0));
            Assert.False(await reader.ReadAsync());
        }
        await using var history = new NpgsqlCommand("SELECT \"MigrationId\" FROM platform.\"__EFMigrationsHistory\"", connection);
        await using var rows = await history.ExecuteReaderAsync();
        Assert.True(await rows.ReadAsync());
        Assert.Equal("20261108144703_InitialPlatformInfrastructure", rows.GetString(0));
        Assert.False(await rows.ReadAsync());
    }
}
