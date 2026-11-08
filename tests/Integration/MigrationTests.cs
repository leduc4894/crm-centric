using Integration.Fixtures;
using Npgsql;
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
