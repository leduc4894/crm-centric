using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261127035000_ExactFieldNumbers")]
public sealed class ExactFieldNumbers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE crm.contact_field_values ADD COLUMN number_raw text, ADD COLUMN number_key text,
            ADD CONSTRAINT field_number_representation CHECK (
                (number_raw IS NULL AND number_key IS NULL) OR
                (data_type='number' AND number_raw IS NOT NULL AND number_key IS NOT NULL));

        -- Add signed decimal integers without casting the potentially unbounded exponent to numeric.
        CREATE FUNCTION platform.add_decimal_integer(left_value text, right_value text)
        RETURNS text LANGUAGE plpgsql IMMUTABLE STRICT SET search_path=pg_catalog AS $f$
        DECLARE magnitude text; prefix text; tail bigint; adjustment bigint; zeros integer; nines integer;
            negative boolean; carry integer := 0;
        BEGIN
            negative:=left(left_value,1)='-';
            magnitude:=ltrim(ltrim(left_value,'+-'),'0');
            IF magnitude='' THEN magnitude:='0'; negative:=false; END IF;
            -- The offset is derived from lengths of a single text value and fits integer.
            adjustment:=right_value::integer;
            IF length(magnitude)<=12 THEN
                RETURN ((CASE WHEN negative THEN -1 ELSE 1 END)*magnitude::bigint + adjustment)::text;
            END IF;
            prefix:=left(magnitude,length(magnitude)-12);
            tail:=right(magnitude,12)::bigint + CASE WHEN negative THEN -adjustment ELSE adjustment END;
            IF tail<0 THEN tail:=tail+1000000000000; carry:=-1;
            ELSIF tail>=1000000000000 THEN tail:=tail-1000000000000; carry:=1; END IF;
            IF carry=1 THEN
                nines:=length(prefix)-length(rtrim(prefix,'9'));
                IF nines=length(prefix) THEN prefix:='1'||repeat('0',nines);
                ELSE prefix:=left(prefix,length(prefix)-nines-1)||
                    ((substr(prefix,length(prefix)-nines,1)::integer)+1)::text||repeat('0',nines); END IF;
            ELSIF carry=-1 THEN
                zeros:=length(prefix)-length(rtrim(prefix,'0'));
                prefix:=left(prefix,length(prefix)-zeros-1)||
                    ((substr(prefix,length(prefix)-zeros,1)::integer)-1)::text||repeat('9',zeros);
                prefix:=ltrim(prefix,'0');
            END IF;
            magnitude:=ltrim(prefix||lpad(tail::text,12,'0'),'0');
            IF magnitude='' THEN RETURN '0'; END IF;
            RETURN CASE WHEN negative THEN '-' ELSE '' END || magnitude;
        END $f$;
        REVOKE ALL ON FUNCTION platform.add_decimal_integer(text,text) FROM PUBLIC;

        CREATE FUNCTION platform.canonical_json_number(raw text)
        RETURNS text LANGUAGE plpgsql IMMUTABLE STRICT SET search_path=pg_catalog AS $f$
        DECLARE pieces text[]; mantissa text; fraction text; digits text; significant text;
            exponent text; offset_value integer; negative boolean;
        BEGIN
            pieces:=regexp_match(raw, '^(-?)(0|[1-9][0-9]*)(\.([0-9]+))?([eE]([+-]?[0-9]+))?$');
            IF pieces IS NULL THEN RAISE invalid_text_representation; END IF;
            negative:=pieces[1]='-'; fraction:=coalesce(pieces[4],'');
            mantissa:=pieces[2]||fraction; digits:=ltrim(mantissa,'0');
            IF digits='' THEN RETURN '0'; END IF;
            significant:=rtrim(digits,'0');
            offset_value:=length(digits)-length(significant)-length(fraction);
            exponent:=platform.add_decimal_integer(coalesce(pieces[6],'0'),offset_value::text);
            RETURN CASE WHEN negative THEN '-' ELSE '' END || significant || 'e' || exponent;
        END $f$;
        REVOKE ALL ON FUNCTION platform.canonical_json_number(text) FROM PUBLIC;
        CREATE OR REPLACE FUNCTION platform.set_contact_field(t uuid, contact uuid, field uuid, iss text, sub text, raw text)
        RETURNS text LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $f$
        DECLARE
            definition crm.custom_field_defs%ROWTYPE;
            incoming jsonb;
            escaped boolean := false;
            number_key text;
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
            IF definition.data_type='number' AND raw IS NOT NULL AND raw ~ '^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$' THEN
                number_key := platform.canonical_json_number(raw);
                INSERT INTO crm.contact_field_values AS v(tenant_id,contact_id,field_id,data_type,value,escaped_string,number_raw,number_key)
                VALUES(t,contact,field,'number','0'::jsonb,false,raw,number_key)
                ON CONFLICT(tenant_id,contact_id,field_id) DO UPDATE
                    SET value=excluded.value,escaped_string=false,number_raw=excluded.number_raw,number_key=excluded.number_key
                    WHERE COALESCE(v.number_key,platform.canonical_json_number(v.value::text)) IS DISTINCT FROM excluded.number_key;
                IF FOUND THEN RETURN 'changed'; END IF;
                RETURN 'unchanged';
            END IF;
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
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE crm.contact_field_values IN ACCESS EXCLUSIVE MODE;
        DO $body$ DECLARE tenant_row record; BEGIN
            FOR tenant_row IN SELECT id FROM platform.tenants LOOP
                PERFORM set_config('crm.tenant_id',tenant_row.id::text,true);
                IF EXISTS(SELECT FROM crm.contact_field_values WHERE number_raw IS NOT NULL) THEN
                    RAISE EXCEPTION 'exact field numbers exist; back up and use a forward fix';
                END IF;
            END LOOP;
        END $body$;
        CREATE OR REPLACE FUNCTION platform.set_contact_field(t uuid, contact uuid, field uuid, iss text, sub text, raw text)
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
        ALTER TABLE crm.contact_field_values DROP CONSTRAINT field_number_representation, DROP COLUMN number_raw, DROP COLUMN number_key;
        DROP FUNCTION platform.canonical_json_number(text);
        DROP FUNCTION platform.add_decimal_integer(text,text);
        """);
}
