using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261127030000_CustomFields")]
public sealed class CustomFields : Migration
{
    private const string PreviousActions = "'tenant.created','membership.updated','invite.created','invite.revoked','invite.accepted','contact.created','contact.updated','contact.deleted'";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($$"""
        CREATE TABLE crm.custom_field_defs (
            tenant_id uuid NOT NULL REFERENCES platform.tenants(id), id uuid NOT NULL,
            key text COLLATE "C" NOT NULL CHECK(key ~ '^[a-z][a-z0-9_]{0,63}$'),
            display_name text NOT NULL CHECK(char_length(display_name) BETWEEN 1 AND 200),
            data_type text NOT NULL CHECK(data_type IN ('string','number','boolean','date')),
            required boolean NOT NULL, version integer NOT NULL DEFAULT 1 CHECK(version=1),
            PRIMARY KEY(tenant_id,id), UNIQUE(tenant_id,key), UNIQUE(tenant_id,id,data_type)
        );
        CREATE FUNCTION crm.reject_field_definition_change() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $f$
        BEGIN
            RAISE EXCEPTION 'field definitions are immutable' USING ERRCODE='55000';
        END $f$;
        REVOKE ALL ON FUNCTION crm.reject_field_definition_change() FROM PUBLIC;
        CREATE TRIGGER immutable_field_definition BEFORE UPDATE OR DELETE ON crm.custom_field_defs
            FOR EACH ROW EXECUTE FUNCTION crm.reject_field_definition_change();
        CREATE TABLE crm.contact_field_values (
            tenant_id uuid NOT NULL, contact_id uuid NOT NULL, field_id uuid NOT NULL,
            data_type text NOT NULL, value jsonb NOT NULL,
            PRIMARY KEY(tenant_id,contact_id,field_id),
            FOREIGN KEY(tenant_id,contact_id) REFERENCES crm.contacts(tenant_id,id),
            FOREIGN KEY(tenant_id,field_id,data_type) REFERENCES crm.custom_field_defs(tenant_id,id,data_type),
            CHECK(CASE WHEN data_type='date' THEN jsonb_typeof(value)='string'
                AND (value #>> '{}') ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$'
                AND pg_input_is_valid(value #>> '{}','date')
                ELSE jsonb_typeof(value)=data_type END)
        );
        ALTER TABLE crm.custom_field_defs ENABLE ROW LEVEL SECURITY;
        ALTER TABLE crm.custom_field_defs FORCE ROW LEVEL SECURITY;
        CREATE POLICY field_defs_tenant ON crm.custom_field_defs USING (tenant_id=NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
        ALTER TABLE crm.contact_field_values ENABLE ROW LEVEL SECURITY;
        ALTER TABLE crm.contact_field_values FORCE ROW LEVEL SECURITY;
        CREATE POLICY field_values_tenant ON crm.contact_field_values USING (tenant_id=NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
        REVOKE ALL ON crm.custom_field_defs,crm.contact_field_values FROM PUBLIC;
        ALTER TABLE audit.entries DROP CONSTRAINT entries_action_check;
        ALTER TABLE audit.entries ADD CONSTRAINT entries_action_check CHECK(action IN ({{PreviousActions}},'tag.created','custom_field.created'));

        CREATE FUNCTION platform.create_crm_definition(t uuid, iss text, sub text, correlation text,
            kind text, label text, field_key text, field_type text, field_required boolean)
        RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $f$
        DECLARE target uuid := gen_random_uuid(); action_name text;
        BEGIN
            IF t IS DISTINCT FROM NULLIF(current_setting('crm.tenant_id',true),'')::uuid
                OR NOT platform.lock_active_membership(iss,sub,t) OR NOT platform.lock_verified_staff(iss,sub)
                OR NOT EXISTS (SELECT FROM platform.memberships m WHERE m.tenant_id=t AND m.issuer=iss
                    AND m.subject=sub AND m.status='active' AND (m.role IN ('owner','admin') OR (kind='tags' AND m.role='agent'))) THEN
                RAISE insufficient_privilege;
            END IF;
            IF kind='tags' AND char_length(label) BETWEEN 1 AND 64 THEN
                INSERT INTO crm.tags(tenant_id,id,name,normalized_name) VALUES(t,target,label,lower(btrim(label)))
                    ON CONFLICT(tenant_id,normalized_name) DO NOTHING;
                IF NOT FOUND THEN RETURN NULL; END IF;
                action_name := 'tag.created';
            ELSIF kind='field-definitions' THEN
                INSERT INTO crm.custom_field_defs(tenant_id,id,key,display_name,data_type,required)
                    VALUES(t,target,field_key,label,field_type,field_required)
                    ON CONFLICT(tenant_id,key) DO NOTHING;
                IF NOT FOUND THEN RETURN NULL; END IF;
                action_name := 'custom_field.created';
            ELSE
                RAISE invalid_parameter_value;
            END IF;
            INSERT INTO audit.entries(id,tenant_id,actor_issuer,actor_subject,action,target_id,summary,correlation_id)
                VALUES(gen_random_uuid(),t,iss,sub,action_name,target,'CRM definition created',correlation);
            RETURN target;
        END $f$;
        REVOKE ALL ON FUNCTION platform.create_crm_definition(uuid,text,text,text,text,text,text,text,boolean) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($$"""
        LOCK TABLE crm.custom_field_defs,crm.contact_field_values,audit.entries IN ACCESS EXCLUSIVE MODE;
        DO $body$ DECLARE tenant_row record; BEGIN
            FOR tenant_row IN SELECT id FROM platform.tenants LOOP
                PERFORM set_config('crm.tenant_id',tenant_row.id::text,true);
                IF EXISTS(SELECT FROM crm.custom_field_defs) OR EXISTS(SELECT FROM crm.contact_field_values) THEN
                    RAISE EXCEPTION 'custom field data exists; back up and use a forward fix';
                END IF;
            END LOOP;
            IF EXISTS(SELECT FROM audit.entries WHERE action IN ('tag.created','custom_field.created')) THEN
                RAISE EXCEPTION 'definition audit data exists; back up and use a forward fix';
            END IF;
        END $body$;
        DROP FUNCTION platform.create_crm_definition(uuid,text,text,text,text,text,text,text,boolean);
        DROP TABLE crm.contact_field_values,crm.custom_field_defs;
        DROP FUNCTION crm.reject_field_definition_change();
        ALTER TABLE audit.entries DROP CONSTRAINT entries_action_check;
        ALTER TABLE audit.entries ADD CONSTRAINT entries_action_check CHECK(action IN ({{PreviousActions}}));
        """);
}
