using Integration.Fixtures;
using Npgsql;
using Xunit;

namespace Integration;

public sealed class RolePrivilegeTests : IAsyncLifetime
{
    private readonly PostgresFixture database = new();
    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task Application_has_only_readiness_privileges()
    {
        Assert.Equal(0, (await MigratorProcess.RunAsync(database.MigrationConnectionString, database.ApplicationRole)).ExitCode);
        await using var connection = new NpgsqlConnection(database.ApplicationConnectionString);
        await connection.OpenAsync();
        await using (var check = new NpgsqlCommand("""
            SELECT NOT rolsuper AND NOT rolbypassrls
              AND NOT has_database_privilege(current_user, current_database(), 'CREATE')
              AND NOT has_schema_privilege(current_user, 'platform', 'CREATE')
              AND NOT has_schema_privilege(current_user, 'public', 'CREATE')
              AND NOT EXISTS (SELECT FROM pg_database WHERE datname=current_database() AND datdba=r.oid)
              AND NOT EXISTS (SELECT FROM pg_namespace WHERE nspname IN ('platform','public') AND nspowner=r.oid)
              AND NOT EXISTS (SELECT FROM pg_class WHERE relnamespace='platform'::regnamespace AND relowner=r.oid)
            FROM pg_roles r WHERE rolname=current_user
            """, connection))
            Assert.Equal(true, await check.ExecuteScalarAsync());
        await PostgresFixture.ExecuteAsync(connection, "SELECT * FROM platform.\"__EFMigrationsHistory\"");
        foreach (var sql in new[] {
            "INSERT INTO platform.\"__EFMigrationsHistory\" VALUES ('bad','bad')",
            "UPDATE platform.\"__EFMigrationsHistory\" SET \"ProductVersion\"='bad'",
            "DELETE FROM platform.\"__EFMigrationsHistory\"",
            "CREATE TABLE platform.denied(id int)", "CREATE TABLE public.denied(id int)",
            "CREATE SCHEMA denied" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.ExecuteAsync(connection, sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
    }
}
