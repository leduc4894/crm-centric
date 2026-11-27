using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

public partial class TenantFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE platform.tenants (
          id uuid PRIMARY KEY, name text NOT NULL CHECK (char_length(name) BETWEEN 1 AND 200),
          status text NOT NULL CHECK (status IN ('active','suspended')),
          created_at timestamptz NOT NULL
        );
        CREATE TABLE platform.memberships (
          tenant_id uuid NOT NULL REFERENCES platform.tenants(id) ON DELETE RESTRICT, id uuid NOT NULL,
          issuer text NOT NULL CHECK (length(btrim(issuer)) > 0),
          subject text NOT NULL CHECK (length(btrim(subject)) > 0),
          role text NOT NULL CHECK (role IN ('owner','admin','agent','operator')),
          status text NOT NULL CHECK (status IN ('active','inactive')),
          version bigint NOT NULL CHECK (version > 0),
          PRIMARY KEY (tenant_id,id), UNIQUE (tenant_id,issuer,subject)
        );
        ALTER TABLE platform.memberships ENABLE ROW LEVEL SECURITY;
        CREATE POLICY membership_tenant ON platform.memberships
          USING (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
        REVOKE ALL ON TABLE platform.tenants, platform.memberships FROM PUBLIC;
        CREATE FUNCTION platform.list_principal_tenants(p_issuer text, p_subject text, p_after uuid, p_take integer)
          RETURNS TABLE(id uuid, name text, status text)
          LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $body$
          SELECT t.id,t.name,t.status FROM platform.tenants AS t
          JOIN platform.memberships AS m ON m.tenant_id=t.id
          WHERE p_issuer IS NOT NULL AND length(btrim(p_issuer)) > 0
            AND p_subject IS NOT NULL AND length(btrim(p_subject)) > 0
            AND m.issuer=p_issuer AND m.subject=p_subject AND m.status='active'
            AND (p_after IS NULL OR t.id > p_after)
          ORDER BY t.id LIMIT greatest(1,least(101,coalesce(p_take,1)))
          $body$;
        CREATE FUNCTION platform.lock_active_membership(p_issuer text, p_subject text, p_tenant uuid)
          RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS $body$
        BEGIN
          IF p_issuer IS NULL OR length(btrim(p_issuer)) = 0 OR p_subject IS NULL OR length(btrim(p_subject)) = 0 THEN
            RETURN false;
          END IF;
          PERFORM 1 FROM platform.tenants AS t WHERE t.id=p_tenant AND t.status='active' FOR SHARE;
          IF NOT FOUND THEN RETURN false; END IF;
          PERFORM 1 FROM platform.memberships AS m
            WHERE m.tenant_id=p_tenant AND m.issuer=p_issuer AND m.subject=p_subject AND m.status='active' FOR SHARE;
          RETURN FOUND;
        END $body$;
        REVOKE ALL ON FUNCTION platform.list_principal_tenants(text,text,uuid,integer) FROM PUBLIC;
        REVOKE ALL ON FUNCTION platform.lock_active_membership(text,text,uuid) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION platform.lock_active_membership(text,text,uuid);
        DROP FUNCTION platform.list_principal_tenants(text,text,uuid,integer);
        DROP TABLE platform.memberships;
        DROP TABLE platform.tenants;
        """);
}
