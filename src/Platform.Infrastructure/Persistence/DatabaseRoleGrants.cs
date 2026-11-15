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
            GRANT SELECT ON TABLE platform."__EFMigrationsHistory", platform.memberships TO {role};
            GRANT EXECUTE ON FUNCTION platform.list_principal_tenants(text,text,uuid,integer), platform.lock_active_membership(text,text,uuid) TO {role};
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task ValidateAsync(NpgsqlConnection connection, string applicationRole, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT FROM pg_roles WHERE rolname = @role AND rolname <> current_user
                  AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
                  AND NOT EXISTS (SELECT FROM pg_roles privileged WHERE (privileged.rolsuper OR privileged.rolbypassrls) AND pg_has_role(@role, privileged.oid, 'MEMBER'))
                  AND NOT has_database_privilege(rolname, current_database(), 'CREATE')
                  AND NOT has_schema_privilege(rolname, 'public', 'CREATE')
                  AND NOT EXISTS (
                    SELECT FROM pg_namespace n WHERE n.nspname='platform'
                    AND (has_schema_privilege(rolname, n.oid, 'CREATE') OR pg_has_role(rolname, n.nspowner, 'MEMBER'))
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='platform' AND pg_has_role(rolname, c.relowner, 'MEMBER')
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                    WHERE n.nspname='platform' AND pg_has_role(rolname, p.proowner, 'MEMBER')
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='platform' AND c.relname='memberships'
                    AND has_table_privilege(rolname, c.oid, 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='platform' AND c.relname='tenants'
                    AND has_table_privilege(rolname, c.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                  )
            )
            """, connection);
        command.Parameters.AddWithValue("role", applicationRole);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("Application role boundary invalid.");
    }
}
