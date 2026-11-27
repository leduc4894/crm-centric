using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

public partial class TenantLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA audit;
        REVOKE ALL ON SCHEMA audit FROM PUBLIC;
        CREATE TABLE audit.entries (
          id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES platform.tenants(id) ON DELETE RESTRICT,
          actor_issuer text NOT NULL CHECK (char_length(actor_issuer) BETWEEN 1 AND 2048),
          actor_subject text NOT NULL CHECK (char_length(actor_subject) BETWEEN 1 AND 2048),
          action text NOT NULL CHECK (action IN ('tenant.created','membership.updated')),
          target_id uuid NOT NULL,
          summary text NOT NULL CHECK (char_length(summary) <= 512),
          correlation_id text NOT NULL CHECK (char_length(correlation_id) BETWEEN 1 AND 128),
          created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX entries_tenant_created ON audit.entries (tenant_id,created_at,id);
        ALTER TABLE audit.entries ENABLE ROW LEVEL SECURITY;
        CREATE POLICY audit_tenant ON audit.entries USING (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
        REVOKE ALL ON TABLE audit.entries FROM PUBLIC;
        CREATE FUNCTION platform.create_tenant_with_owner(p_tenant uuid,p_name text,p_issuer text,p_subject text,p_correlation text)
          RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $body$
        BEGIN
          IF p_tenant IS NULL OR p_name IS NULL OR char_length(btrim(p_name))=0 OR char_length(p_name)>200
            OR p_issuer IS NULL OR char_length(btrim(p_issuer))=0 OR char_length(p_issuer)>2048
            OR p_subject IS NULL OR char_length(btrim(p_subject))=0 OR char_length(p_subject)>2048
            OR p_correlation IS NULL OR char_length(btrim(p_correlation))=0 OR char_length(p_correlation)>128 THEN
            RAISE EXCEPTION 'invalid lifecycle input' USING ERRCODE='22023';
          END IF;
          INSERT INTO platform.tenants(id,name,status,created_at) VALUES(p_tenant,p_name,'active',now());
          INSERT INTO platform.memberships(tenant_id,id,issuer,subject,role,status,version)
            VALUES(p_tenant,gen_random_uuid(),p_issuer,p_subject,'owner','active',1);
          INSERT INTO audit.entries(id,tenant_id,actor_issuer,actor_subject,action,target_id,summary,correlation_id)
            VALUES(gen_random_uuid(),p_tenant,p_issuer,p_subject,'tenant.created',p_tenant,'{}',p_correlation);
          RETURN p_tenant;
        END $body$;
        CREATE FUNCTION platform.update_membership(p_tenant uuid,p_target uuid,p_version bigint,p_role text,p_status text,p_issuer text,p_subject text,p_correlation text)
          RETURNS TABLE(outcome text,id uuid,issuer text,subject text,role text,status text,version bigint)
          LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $body$
        DECLARE target platform.memberships%ROWTYPE; new_role text; new_status text;
        BEGIN
          PERFORM pg_advisory_xact_lock(hashtextextended('crm.membership:' || p_tenant::text,0));
          IF NULLIF(current_setting('crm.tenant_id',true),'') IS DISTINCT FROM p_tenant::text THEN
            RETURN QUERY SELECT 'not_found',NULL::uuid,NULL::text,NULL::text,NULL::text,NULL::text,NULL::bigint; RETURN;
          END IF;
          IF NOT EXISTS (SELECT 1 FROM platform.memberships m WHERE m.tenant_id=p_tenant AND m.issuer=p_issuer AND m.subject=p_subject AND m.status='active' AND m.role IN ('owner','admin')) THEN
            RETURN QUERY SELECT 'forbidden',NULL::uuid,NULL::text,NULL::text,NULL::text,NULL::text,NULL::bigint; RETURN;
          END IF;
          SELECT * INTO target FROM platform.memberships m WHERE m.tenant_id=p_tenant AND m.id=p_target FOR UPDATE;
          IF NOT FOUND THEN RETURN QUERY SELECT 'not_found',NULL::uuid,NULL::text,NULL::text,NULL::text,NULL::text,NULL::bigint; RETURN; END IF;
          IF p_version IS NULL OR target.version<>p_version THEN RETURN QUERY SELECT 'version_conflict',NULL::uuid,NULL::text,NULL::text,NULL::text,NULL::text,NULL::bigint; RETURN; END IF;
          new_role := coalesce(p_role,target.role); new_status := coalesce(p_status,target.status);
          IF new_role NOT IN ('owner','admin','agent','operator') OR new_status NOT IN ('active','inactive') OR (p_role IS NULL AND p_status IS NULL)
             OR p_correlation IS NULL OR char_length(btrim(p_correlation))=0 OR char_length(p_correlation)>128 THEN
            RETURN QUERY SELECT 'invalid',NULL::uuid,NULL::text,NULL::text,NULL::text,NULL::text,NULL::bigint; RETURN;
          END IF;
          IF (new_role='owner' AND target.role<>'owner') OR (target.role='owner' AND new_role<>'owner') OR
             (target.role='owner' AND target.status='active' AND new_status='inactive' AND
               (SELECT count(*) FROM platform.memberships m WHERE m.tenant_id=p_tenant AND m.role='owner' AND m.status='active')<=1) THEN
            RETURN QUERY SELECT 'owner_invariant',NULL::uuid,NULL::text,NULL::text,NULL::text,NULL::text,NULL::bigint; RETURN;
          END IF;
          IF new_role=target.role AND new_status=target.status THEN
            RETURN QUERY SELECT 'unchanged',target.id,target.issuer,target.subject,target.role,target.status,target.version; RETURN;
          END IF;
          UPDATE platform.memberships m SET role=new_role,status=new_status,version=m.version+1 WHERE m.tenant_id=p_tenant AND m.id=p_target AND m.version=p_version;
          INSERT INTO audit.entries(id,tenant_id,actor_issuer,actor_subject,action,target_id,summary,correlation_id)
            VALUES(gen_random_uuid(),p_tenant,p_issuer,p_subject,'membership.updated',p_target,
              json_build_object('oldRole',target.role,'newRole',new_role,'oldStatus',target.status,'newStatus',new_status)::text,p_correlation);
          RETURN QUERY SELECT 'updated',target.id,target.issuer,target.subject,new_role,new_status,target.version+1;
        END $body$;
        REVOKE ALL ON FUNCTION platform.create_tenant_with_owner(uuid,text,text,text,text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION platform.update_membership(uuid,uuid,bigint,text,text,text,text,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $body$ BEGIN
          IF EXISTS (SELECT 1 FROM audit.entries) THEN RAISE EXCEPTION 'audit data exists; back up and use a forward fix'; END IF;
        END $body$;
        DROP FUNCTION platform.update_membership(uuid,uuid,bigint,text,text,text,text,text);
        DROP FUNCTION platform.create_tenant_with_owner(uuid,text,text,text,text);
        DROP TABLE audit.entries;
        DROP SCHEMA audit;
        """);
}
