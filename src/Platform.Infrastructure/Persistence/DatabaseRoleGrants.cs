using Npgsql;

namespace Platform.Infrastructure.Persistence;

public static class DatabaseRoleGrants
{
    public static async Task ApplyAsync(NpgsqlConnection connection, string applicationRole, CancellationToken cancellationToken)
    {
        await ValidateAsync(connection, applicationRole, cancellationToken, requireContactSchema: true);
        var role = new NpgsqlCommandBuilder().QuoteIdentifier(applicationRole);
        await using var command = new NpgsqlCommand($"""
            GRANT EXECUTE ON FUNCTION platform.register_manifest(bytea,text,bigint,text[],text[],text[],bytea[],text,text,text[],bytea[]) TO {role};
            GRANT USAGE ON SCHEMA platform, crm, outbox TO {role};
            GRANT SELECT, INSERT, UPDATE ON crm.contacts TO {role};
            GRANT EXECUTE ON FUNCTION platform.create_crm_definition(uuid,text,text,text,text,text[],text,text,boolean) TO {role};
            GRANT EXECUTE ON FUNCTION platform.mutate_contact_attribute(uuid,uuid,uuid,text,text,bigint,text,text,text) TO {role};
            GRANT EXECUTE ON FUNCTION platform.create_contact_note(uuid,uuid,text,text,bigint,text,text[]) TO {role};
            GRANT SELECT ON crm.tags, crm.contact_tags, crm.custom_field_defs, crm.contact_field_values, crm.timeline_entries TO {role};
            GRANT INSERT ON outbox.events TO {role};
            GRANT EXECUTE ON FUNCTION platform.append_contact_audit(uuid,uuid,text,text,text,text) TO {role};
            GRANT SELECT ON TABLE platform."__EFMigrationsHistory", platform.memberships TO {role};
            GRANT EXECUTE ON FUNCTION platform.list_principal_tenants(text,text,uuid,integer), platform.lock_active_membership(text,text,uuid), platform.create_tenant_with_owner(uuid,text,text,text,text), platform.update_membership(uuid,uuid,bigint,text,text,text,text,text), platform.lock_verified_staff(text,text), platform.invite_tenant_for_hash(bytea), platform.create_invite(uuid,text,text,text,bytea,text,text,text), platform.list_audit_entries(uuid,text,text,timestamptz,uuid,integer), platform.list_invites(uuid,text,text,uuid,integer), platform.revoke_invite(uuid,text,text,text,uuid), platform.accept_invite(bytea,text,text,text,text) TO {role};
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task ValidateAsync(NpgsqlConnection connection, string applicationRole, CancellationToken cancellationToken, bool requireContactSchema = false)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
              SELECT FROM pg_roles WHERE rolname = @role AND rolname <> current_user
                AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
                AND NOT EXISTS (SELECT FROM pg_roles privileged WHERE (privileged.rolsuper OR privileged.rolbypassrls) AND pg_has_role(@role, privileged.oid, 'MEMBER'))
            ) AND (
              SELECT (count(*)=2 OR (count(*)=0 AND NOT @require_contact_schema))
                AND coalesce(bool_and(c.relrowsecurity AND c.relforcerowsecurity),true)
              FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
              WHERE (n.nspname='crm' AND c.relname='contacts') OR (n.nspname='outbox' AND c.relname='events')
            ) AND (
              SELECT (count(*)=2 OR (count(*)=0 AND NOT @require_contact_schema))
                AND coalesce(bool_and(c.relrowsecurity AND c.relforcerowsecurity),true)
              FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
              WHERE n.nspname='crm' AND c.relname IN ('tags','contact_tags')
            ) AND (
              SELECT (count(*)=2 OR (count(*)=0 AND NOT @require_contact_schema))
                AND coalesce(bool_and(c.relrowsecurity AND c.relforcerowsecurity),true)
              FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
              WHERE n.nspname='crm' AND c.relname IN ('custom_field_defs','contact_field_values')
            ) AND (
              SELECT (count(*)=1 OR NOT @require_contact_schema)
                AND coalesce(bool_and(c.relrowsecurity AND c.relforcerowsecurity),true)
              FROM pg_class c WHERE c.oid=to_regclass('crm.timeline_entries')
            ) AND (
              -- Evaluate the login and every role it can assume, including non-inherited grants.
              SELECT bool_and(
                  NOT has_database_privilege(rolname, current_database(), 'CREATE')
                  AND NOT has_schema_privilege(rolname, 'public', 'CREATE')
                  AND NOT EXISTS (
                    SELECT FROM pg_namespace n WHERE n.nspname IN ('platform','audit','crm','outbox')
                    AND (has_schema_privilege(rolname, n.oid, 'CREATE') OR pg_has_role(rolname, n.nspowner, 'MEMBER'))
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname IN ('platform','audit','crm','outbox') AND pg_has_role(rolname, c.relowner, 'MEMBER')
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                    WHERE n.nspname IN ('platform','audit','crm','outbox') AND pg_has_role(rolname, p.proowner, 'MEMBER')
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                    WHERE n.nspname='platform' AND p.proname IN ('register_manifest','create_tenant_with_owner','update_membership','lock_verified_staff','audit_staff_binding_change','invite_tenant_for_hash','create_invite','list_invites','list_audit_entries','revoke_invite','require_invite_manager','accept_invite','append_contact_audit','create_crm_definition','create_contact_note','mutate_contact_attribute','set_contact_tag','set_contact_field')
                    AND (has_function_privilege('public', p.oid, 'EXECUTE') OR NOT p.prosecdef OR ('search_path=pg_catalog' = ANY(p.proconfig)) IS DISTINCT FROM true)
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_proc p WHERE p.pronamespace=to_regnamespace('platform')
                      AND (@require_contact_schema OR to_regprocedure('platform.mutate_contact_attribute(uuid,uuid,uuid,text,text,bigint,text,text,text)') IS NOT NULL)
                      AND p.proname IN ('set_contact_tag','set_contact_field') AND has_function_privilege(rolname,p.oid,'EXECUTE')
                  )
                  AND NOT EXISTS (
                    SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE (n.nspname='crm' AND c.relname='contacts' AND has_table_privilege(rolname,c.oid,'DELETE,TRUNCATE,REFERENCES,TRIGGER'))
                       OR (n.nspname='crm' AND c.relname IN ('tags','contact_tags','custom_field_defs','contact_field_values','timeline_entries') AND (has_table_privilege(rolname,c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') OR has_any_column_privilege(rolname,c.oid,'INSERT,UPDATE,REFERENCES')))
                       OR (n.nspname='outbox' AND c.relname='events' AND (has_table_privilege(rolname,c.oid,'SELECT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') OR has_any_column_privilege(rolname,c.oid,'SELECT,UPDATE,REFERENCES')))
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
                    WHERE n.nspname='platform' AND c.relname IN ('tenants','staff_bindings','invites','manifest_candidates','manifest_schemas')
                    AND (has_table_privilege(rolname, c.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                      OR has_any_column_privilege(rolname, c.oid, 'SELECT,INSERT,UPDATE,REFERENCES'))
                  )
              )
              FROM pg_roles WHERE pg_has_role(@role, oid, 'SET')
            )
            """, connection);
        command.Parameters.AddWithValue("role", applicationRole);
        command.Parameters.AddWithValue("require_contact_schema", requireContactSchema);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("Application role boundary invalid.");
    }
}
