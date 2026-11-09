using System.Net;
using System.Net.Sockets;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Integration.Fixtures;

public sealed class PostgresFixture(bool stableHostPort = false) : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = CreateContainer(stableHostPort);

    private static PostgreSqlContainer CreateContainer(bool stableHostPort)
    {
        var builder = new PostgreSqlBuilder(
            ContainerImages.Pins["POSTGRES_IMAGE"])
            .WithPassword(Guid.NewGuid().ToString("N"));
        if (stableHostPort)
        {
            // Docker can reassign dynamic mappings on restart. Outage tests need
            // the same dependency address for the lifetime of their API host.
            using var reservation = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            reservation.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            builder = builder.WithPortBinding(((IPEndPoint)reservation.LocalEndPoint!).Port, 5432);
        }
        return builder.Build();
    }

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
