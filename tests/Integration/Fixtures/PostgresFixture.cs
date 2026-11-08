using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Integration.Fixtures;

public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder(
        "postgres:18.6-bookworm@sha256:f1ea2a1a4fb7b68d4836be1fdcb8a6466a89b072158bf9d97663600538c294c5")
        .WithPassword(Guid.NewGuid().ToString("N")).Build();
    public string MigrationConnectionString { get; private set; } = "";
    public string ApplicationConnectionString { get; private set; } = "";
    public string ApplicationRole { get; } = "app-\"" + Guid.NewGuid().ToString("N");
    public string MigrationRole { get; } = "migrator_" + Guid.NewGuid().ToString("N");
    public string DatabaseName { get; } = "test_" + Guid.NewGuid().ToString("N");
    public string AdminConnectionString => new NpgsqlConnectionStringBuilder(Container.GetConnectionString())
        { Database = DatabaseName }.ConnectionString;
    public static string Quote(string identifier) => new NpgsqlCommandBuilder().QuoteIdentifier(identifier);

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        await using var admin = new NpgsqlConnection(Container.GetConnectionString());
        await admin.OpenAsync();
        var migrationPassword = Guid.NewGuid().ToString("N");
        var appPassword = Guid.NewGuid().ToString("N");
        // Passwords are generated hex values, never caller-controlled SQL or logged.
        await ExecuteAsync(admin, $"CREATE ROLE {Quote(MigrationRole)} LOGIN PASSWORD '{migrationPassword}' NOSUPERUSER NOBYPASSRLS; CREATE ROLE {Quote(ApplicationRole)} LOGIN PASSWORD '{appPassword}' NOSUPERUSER NOBYPASSRLS");
        await ExecuteAsync(admin, $"CREATE DATABASE {Quote(DatabaseName)}");
        await using var databaseAdmin = new NpgsqlConnection(AdminConnectionString);
        await databaseAdmin.OpenAsync();
        await ExecuteAsync(databaseAdmin, $"REVOKE ALL ON DATABASE {Quote(DatabaseName)} FROM PUBLIC; REVOKE ALL ON SCHEMA public FROM PUBLIC; GRANT CONNECT, CREATE ON DATABASE {Quote(DatabaseName)} TO {Quote(MigrationRole)}; GRANT CONNECT ON DATABASE {Quote(DatabaseName)} TO {Quote(ApplicationRole)}");
        MigrationConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString)
            { Username = MigrationRole, Password = migrationPassword }.ConnectionString;
        ApplicationConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString)
            { Username = ApplicationRole, Password = appPassword }.ConnectionString;
    }

    public static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await Container.DisposeAsync();
}
