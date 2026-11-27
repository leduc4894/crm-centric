using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261127034000_DefinitionNames")]
public sealed class DefinitionNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- PostgreSQL text excludes U+0000. A sequence of text segments preserves its
        -- exact positions (including leading, trailing and consecutive occurrences).
        ALTER TABLE crm.tags ADD COLUMN name_parts text[], ADD COLUMN normalized_parts text[],
            ALTER COLUMN normalized_name DROP NOT NULL,
            ADD CHECK ((normalized_name IS NOT NULL AND name_parts IS NULL AND normalized_parts IS NULL)
                OR (normalized_name IS NULL AND name_parts IS NOT NULL AND normalized_parts IS NOT NULL));
        CREATE UNIQUE INDEX tags_normalized_parts ON crm.tags(tenant_id,normalized_parts) WHERE normalized_parts IS NOT NULL;
        ALTER TABLE crm.custom_field_defs ADD COLUMN display_parts text[];

        CREATE FUNCTION platform.create_crm_definition(t uuid, iss text, sub text, correlation text,
            kind text, parts text[], field_key text, field_type text, field_required boolean)
        RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $f$
        DECLARE target uuid := gen_random_uuid(); action_name text; label text; normalized text[];
        BEGIN
            IF array_ndims(parts) IS DISTINCT FROM 1 OR array_lower(parts,1)<>1
                OR array_position(parts,NULL) IS NOT NULL THEN RAISE invalid_parameter_value; END IF;
            IF cardinality(parts)=1 THEN
                RETURN platform.create_crm_definition(t,iss,sub,correlation,kind,parts[1],field_key,field_type,field_required);
            END IF;
            IF t IS DISTINCT FROM NULLIF(current_setting('crm.tenant_id',true),'')::uuid
                OR NOT platform.lock_active_membership(iss,sub,t) OR NOT platform.lock_verified_staff(iss,sub)
                OR NOT EXISTS (SELECT FROM platform.memberships m WHERE m.tenant_id=t AND m.issuer=iss
                    AND m.subject=sub AND m.status='active' AND (m.role IN ('owner','admin') OR (kind='tags' AND m.role='agent'))) THEN
                RAISE insufficient_privilege;
            END IF;
            label := array_to_string(parts,chr(1));
            IF kind='tags' AND char_length(label) BETWEEN 1 AND 64 THEN
                -- Trim only the outer edges. Spaces adjacent to an embedded NUL are significant.
                SELECT array_agg(lower(CASE WHEN i=1 THEN ltrim(piece)
                    WHEN i=cardinality(parts) THEN rtrim(piece) ELSE piece END) ORDER BY i)
                    INTO normalized FROM unnest(parts) WITH ORDINALITY AS p(piece,i);
                INSERT INTO crm.tags(tenant_id,id,name,name_parts,normalized_parts) VALUES(t,target,label,parts,normalized)
                    ON CONFLICT DO NOTHING;
                IF NOT FOUND THEN RETURN NULL; END IF;
                action_name := 'tag.created';
            ELSIF kind='field-definitions' THEN
                INSERT INTO crm.custom_field_defs(tenant_id,id,key,display_name,display_parts,data_type,required)
                    VALUES(t,target,field_key,label,parts,field_type,field_required)
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
        REVOKE ALL ON FUNCTION platform.create_crm_definition(uuid,text,text,text,text,text[],text,text,boolean) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE crm.tags,crm.custom_field_defs IN ACCESS EXCLUSIVE MODE;
        DO $body$ DECLARE tenant_row record; BEGIN
            FOR tenant_row IN SELECT id FROM platform.tenants LOOP
                PERFORM set_config('crm.tenant_id',tenant_row.id::text,true);
                IF EXISTS(SELECT FROM crm.tags WHERE name_parts IS NOT NULL) OR
                    EXISTS(SELECT FROM crm.custom_field_defs WHERE display_parts IS NOT NULL) THEN
                    RAISE EXCEPTION 'embedded NUL names exist; back up and use a forward fix';
                END IF;
            END LOOP;
        END $body$;
        DROP FUNCTION platform.create_crm_definition(uuid,text,text,text,text,text[],text,text,boolean);
        ALTER TABLE crm.tags DROP COLUMN name_parts,DROP COLUMN normalized_parts,ALTER COLUMN normalized_name SET NOT NULL;
        ALTER TABLE crm.custom_field_defs DROP COLUMN display_parts;
        """);
}
