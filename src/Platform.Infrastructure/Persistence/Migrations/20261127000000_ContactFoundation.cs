using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261127000000_ContactFoundation")]
public sealed class ContactFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA crm;
        CREATE SCHEMA outbox;
        REVOKE ALL ON SCHEMA crm, outbox FROM PUBLIC;
        CREATE TABLE crm.contacts (
          tenant_id uuid NOT NULL REFERENCES platform.tenants(id) ON DELETE RESTRICT,
          id uuid NOT NULL,
          display_name text NOT NULL CHECK (char_length(display_name) BETWEEN 1 AND 200),
          email text CHECK (email IS NULL OR char_length(email) <= 320),
          phone text CHECK (phone IS NULL OR char_length(phone) <= 40),
          version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
          created_at timestamptz NOT NULL DEFAULT now(),
          updated_at timestamptz NOT NULL DEFAULT now(),
          deleted_at timestamptz,
          PRIMARY KEY (tenant_id,id)
        );
        CREATE INDEX contacts_tenant_updated ON crm.contacts (tenant_id,updated_at DESC,id DESC) WHERE deleted_at IS NULL;
        ALTER TABLE crm.contacts ENABLE ROW LEVEL SECURITY;
        ALTER TABLE crm.contacts FORCE ROW LEVEL SECURITY;
        CREATE POLICY contacts_tenant ON crm.contacts USING (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid)
          WITH CHECK (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
        REVOKE ALL ON crm.contacts FROM PUBLIC;
        CREATE TABLE outbox.events (
          tenant_id uuid NOT NULL,
          id uuid NOT NULL,
          contact_id uuid NOT NULL,
          type text NOT NULL CHECK (type IN ('crm.contact.created.v1','crm.contact.updated.v1','crm.contact.deleted.v1')),
          occurred_at timestamptz NOT NULL,
          payload jsonb NOT NULL CHECK (jsonb_typeof(payload)='object'),
          PRIMARY KEY (tenant_id,id),
          FOREIGN KEY (tenant_id,contact_id) REFERENCES crm.contacts(tenant_id,id) ON DELETE RESTRICT
        );
        ALTER TABLE outbox.events ENABLE ROW LEVEL SECURITY;
        ALTER TABLE outbox.events FORCE ROW LEVEL SECURITY;
        CREATE POLICY events_tenant ON outbox.events USING (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid)
          WITH CHECK (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
        REVOKE ALL ON outbox.events FROM PUBLIC;
        ALTER TABLE audit.entries DROP CONSTRAINT entries_action_check;
        ALTER TABLE audit.entries ADD CONSTRAINT entries_action_check CHECK (action IN
          ('tenant.created','membership.updated','invite.created','invite.revoked','invite.accepted','contact.created','contact.updated','contact.deleted'));
        CREATE FUNCTION platform.append_contact_audit(t uuid, target uuid, iss text, sub text, action_name text, correlation text)
        RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $f$
        BEGIN
          IF t IS DISTINCT FROM nullif(current_setting('crm.tenant_id',true),'')::uuid
              OR action_name NOT IN ('contact.created','contact.updated','contact.deleted')
              OR NOT platform.lock_active_membership(iss,sub,t)
              OR NOT platform.lock_verified_staff(iss,sub)
              OR NOT EXISTS (SELECT FROM platform.memberships m WHERE m.tenant_id=t AND m.issuer=iss
                  AND m.subject=sub AND m.status='active' AND m.role IN ('owner','admin','agent'))
              OR NOT EXISTS (SELECT FROM crm.contacts c WHERE c.tenant_id=t AND c.id=target) THEN
            RAISE insufficient_privilege;
          END IF;
          INSERT INTO audit.entries(id,tenant_id,actor_issuer,actor_subject,action,target_id,summary,correlation_id)
            VALUES(gen_random_uuid(),t,iss,sub,action_name,target,'Contact changed',correlation);
        END $f$;
        REVOKE ALL ON FUNCTION platform.append_contact_audit(uuid,uuid,text,text,text,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE crm.contacts, outbox.events, audit.entries IN ACCESS EXCLUSIVE MODE;
        DO $body$ DECLARE tenant_row record; BEGIN
          FOR tenant_row IN SELECT id FROM platform.tenants LOOP
            PERFORM set_config('crm.tenant_id',tenant_row.id::text,true);
            IF EXISTS (SELECT FROM crm.contacts) OR EXISTS (SELECT FROM outbox.events) THEN
              RAISE EXCEPTION 'contact, outbox or audit data exists; back up and use a forward fix';
            END IF;
          END LOOP;
          IF EXISTS (SELECT FROM audit.entries WHERE action IN ('contact.created','contact.updated','contact.deleted')) THEN
            RAISE EXCEPTION 'contact, outbox or audit data exists; back up and use a forward fix';
          END IF;
        END $body$;
        DROP FUNCTION platform.append_contact_audit(uuid,uuid,text,text,text,text);
        DROP TABLE outbox.events;
        DROP TABLE crm.contacts;
        DROP SCHEMA outbox;
        DROP SCHEMA crm;
        ALTER TABLE audit.entries DROP CONSTRAINT entries_action_check;
        ALTER TABLE audit.entries ADD CONSTRAINT entries_action_check CHECK (action IN
          ('tenant.created','membership.updated','invite.created','invite.revoked','invite.accepted'));
        """);
}
