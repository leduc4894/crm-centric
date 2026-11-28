using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261127032000_ContactFieldValue")]
public sealed class ContactFieldValue : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- PostgreSQL text cannot materialize U+0000. Mark the exceptional string
        -- representation so that existing ordinary JSONB strings keep their meaning.
        ALTER TABLE crm.contact_field_values ADD COLUMN escaped_string boolean NOT NULL DEFAULT false
            CHECK(NOT escaped_string OR data_type='string');
        CREATE FUNCTION platform.set_contact_field(t uuid, contact uuid, field uuid, iss text, sub text, raw text)
        RETURNS text LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $f$
        DECLARE
            definition crm.custom_field_defs%ROWTYPE;
            incoming jsonb;
            escaped boolean := false;
        BEGIN
            IF t IS DISTINCT FROM NULLIF(current_setting('crm.tenant_id',true),'')::uuid
                OR NOT platform.lock_active_membership(iss,sub,t) OR NOT platform.lock_verified_staff(iss,sub)
                OR NOT EXISTS (SELECT FROM platform.memberships m WHERE m.tenant_id=t AND m.issuer=iss
                    AND m.subject=sub AND m.status='active' AND m.role IN ('owner','admin','agent')) THEN
                RAISE insufficient_privilege;
            END IF;
            IF NOT EXISTS (SELECT FROM crm.contacts WHERE tenant_id=t AND id=contact AND deleted_at IS NULL FOR UPDATE) THEN
                RETURN 'missing';
            END IF;
            SELECT d.* INTO definition FROM crm.custom_field_defs d WHERE d.tenant_id=t AND d.id=field;
            IF NOT FOUND THEN RETURN 'missing'; END IF;
            BEGIN
                incoming := raw::jsonb;
            EXCEPTION
                WHEN numeric_value_out_of_range THEN RETURN 'unrepresentable';
                WHEN untranslatable_character THEN
                    IF definition.data_type<>'string' OR json_typeof(raw::json)<>'string' THEN RETURN 'invalid'; END IF;
                    incoming := to_jsonb(raw);
                    escaped := true;
                WHEN invalid_text_representation THEN RETURN 'invalid';
            END;
            IF incoming IS NULL THEN RETURN 'invalid'; END IF;
            IF incoming='null'::jsonb THEN
                IF definition.required THEN RETURN 'invalid'; END IF;
                DELETE FROM crm.contact_field_values WHERE tenant_id=t AND contact_id=contact AND field_id=field;
            ELSE
                IF definition.data_type='date' THEN
                    IF jsonb_typeof(incoming)<>'string' OR (incoming #>> '{}') !~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$'
                        OR NOT pg_input_is_valid(incoming #>> '{}','date') THEN RETURN 'invalid'; END IF;
                ELSIF jsonb_typeof(incoming)<>definition.data_type THEN RETURN 'invalid';
                END IF;
                INSERT INTO crm.contact_field_values AS v(tenant_id,contact_id,field_id,data_type,value,escaped_string)
                VALUES(t,contact,field,definition.data_type,incoming,escaped)
                ON CONFLICT(tenant_id,contact_id,field_id) DO UPDATE SET value=excluded.value, escaped_string=excluded.escaped_string
                    WHERE (v.value,v.escaped_string) IS DISTINCT FROM (excluded.value,excluded.escaped_string);
            END IF;
            IF FOUND THEN RETURN 'changed'; END IF;
            RETURN 'unchanged';
        END $f$;
        REVOKE ALL ON FUNCTION platform.set_contact_field(uuid,uuid,uuid,text,text,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE crm.contact_field_values IN ACCESS EXCLUSIVE MODE;
        DO $body$ DECLARE tenant_row record; BEGIN
            FOR tenant_row IN SELECT id FROM platform.tenants LOOP
                PERFORM set_config('crm.tenant_id',tenant_row.id::text,true);
                IF EXISTS(SELECT FROM crm.contact_field_values WHERE escaped_string) THEN
                    RAISE EXCEPTION 'escaped field strings exist; back up and use a forward fix';
                END IF;
            END LOOP;
        END $body$;
        ALTER TABLE crm.contact_field_values DROP COLUMN escaped_string;
        DROP FUNCTION platform.set_contact_field(uuid,uuid,uuid,text,text,text);
        """);
}
