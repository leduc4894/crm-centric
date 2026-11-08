using Npgsql;

namespace Platform.Infrastructure.Persistence;

public static class DatabaseRoleGrants
{
    public static async Task ApplyAsync(NpgsqlConnection connection, string applicationRole, CancellationToken cancellationToken)
    {
        await ValidateAsync(connection, applicationRole, cancellationToken);
        var role = new NpgsqlCommandBuilder().QuoteIdentifier(applicationRole);
        await using var command = new NpgsqlCommand($"""
            GRANT USAGE ON SCHEMA platform TO {role};
            GRANT SELECT ON TABLE platform."__EFMigrationsHistory" TO {role};
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task ValidateAsync(NpgsqlConnection connection, string applicationRole, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT FROM pg_roles WHERE rolname = @role AND rolname <> current_user
                  AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
                  AND NOT has_database_privilege(rolname, current_database(), 'CREATE')
                  AND NOT has_schema_privilege(rolname, 'public', 'CREATE')
            )
            """, connection);
        command.Parameters.AddWithValue("role", applicationRole);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("Application role boundary invalid.");
    }
}
