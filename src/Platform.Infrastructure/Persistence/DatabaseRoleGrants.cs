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
            GRANT EXECUTE ON FUNCTION platform.list_principal_tenants(text,text,uuid,integer), platform.lock_active_membership(text,text,uuid), platform.create_tenant_with_owner(uuid,text,text,text,text), platform.update_membership(uuid,uuid,bigint,text,text,text,text,text), platform.lock_verified_staff(text,text), platform.invite_tenant_for_hash(bytea), platform.create_invite(uuid,text,text,text,bytea,text,text,text), platform.list_invites(uuid,text,text,uuid,integer), platform.revoke_invite(uuid,text,text,text,uuid), platform.accept_invite(bytea,text,text,text,text) TO {role};
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
            ) AND (
              -- Evaluate the login and every role it can assume, including non-inherited grants.
              SELECT bool_and(
                  NOT has_database_privilege(rolname, current_database(), 'CREATE')
                  AND NOT has_schema_privilege(rolname, 'public', 'CREATE')
                  AND NOT EXISTS (
                    SELECT FROM pg_namespace n WHERE n.nspname IN ('platform','audit')
                    AND (has_schema_privilege(rolname, n.oid, 'CREATE') OR pg_has_role(rolname, n.nspowner, 'MEMBER'))
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname IN ('platform','audit') AND pg_has_role(rolname, c.relowner, 'MEMBER')
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                    WHERE n.nspname IN ('platform','audit') AND pg_has_role(rolname, p.proowner, 'MEMBER')
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                    WHERE n.nspname='platform' AND p.proname IN ('create_tenant_with_owner','update_membership','lock_verified_staff','audit_staff_binding_change','invite_tenant_for_hash','create_invite','list_invites','revoke_invite','require_invite_manager','accept_invite')
                    AND (has_function_privilege('public', p.oid, 'EXECUTE') OR NOT p.prosecdef OR ('search_path=pg_catalog' = ANY(p.proconfig)) IS DISTINCT FROM true)
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='platform' AND c.relname='memberships'
                    AND (has_table_privilege(rolname, c.oid, 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                      OR has_any_column_privilege(rolname, c.oid, 'INSERT,UPDATE,REFERENCES'))
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='audit' AND c.relname IN ('entries','staff_binding_changes')
                    AND (has_table_privilege(rolname, c.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') OR has_any_column_privilege(rolname, c.oid, 'SELECT,INSERT,UPDATE,REFERENCES'))
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='platform' AND c.relname IN ('tenants','staff_bindings','invites')
                    AND (has_table_privilege(rolname, c.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                      OR has_any_column_privilege(rolname, c.oid, 'SELECT,INSERT,UPDATE,REFERENCES'))
                  )
              )
              FROM pg_roles WHERE pg_has_role(@role, oid, 'SET')
            )
            """, connection);
        command.Parameters.AddWithValue("role", applicationRole);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("Application role boundary invalid.");
    }
}
