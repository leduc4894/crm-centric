using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261127041000_AtomicContactNote")]
public sealed class AtomicContactNote : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION platform.create_contact_note(t uuid, contact uuid, iss text, sub text,
            expected bigint, correlation text, parts text[])
        RETURNS TABLE(entry_id uuid, occurred timestamptz, new_version bigint)
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $f$
        DECLARE note_bytes bytea; note_length bigint; event_id uuid := gen_random_uuid();
            event_type constant text := 'crm.contact.updated.v1';
        BEGIN
            IF t IS DISTINCT FROM nullif(current_setting('crm.tenant_id',true),'')::uuid
                OR NOT platform.lock_active_membership(iss,sub,t) OR NOT platform.lock_verified_staff(iss,sub)
                OR NOT EXISTS (SELECT FROM platform.memberships m WHERE m.tenant_id=t AND m.issuer=iss
                    AND m.subject=sub AND m.status='active' AND m.role IN ('owner','admin','agent')) THEN
                RAISE insufficient_privilege;
            END IF;
            -- Each separator represents a U+0000, which PostgreSQL text cannot store.
            SELECT sum(char_length(part)) + count(*) - 1,
                string_agg(convert_to(part,'UTF8'),decode('00','hex') ORDER BY ordinal)
                INTO note_length,note_bytes FROM unnest(parts) WITH ORDINALITY AS p(part,ordinal);
            IF note_length IS NULL OR note_length NOT BETWEEN 1 AND 1000
                OR array_position(parts,NULL) IS NOT NULL THEN RAISE invalid_parameter_value; END IF;
            UPDATE crm.contacts SET version=version+1,updated_at=now()
                WHERE tenant_id=t AND id=contact AND deleted_at IS NULL AND version=expected
                RETURNING version INTO new_version;
            IF NOT FOUND THEN RETURN; END IF;
            entry_id := gen_random_uuid();
            occurred := clock_timestamp();
            INSERT INTO crm.timeline_entries(tenant_id,id,contact_id,entry_kind,source,type,occurred_at,summary)
                VALUES(t,entry_id,contact,'note','crm','crm.note.created.v1',occurred,note_bytes);
            PERFORM platform.append_contact_audit(t,contact,iss,sub,'contact.updated',correlation);
            INSERT INTO outbox.events(tenant_id,id,contact_id,type,occurred_at,payload)
                VALUES(t,event_id,contact,event_type,occurred,jsonb_build_object('eventId',event_id,'tenantId',t,
                    'contactId',contact,'eventType',event_type,'occurredAt',occurred,
                    'payload',jsonb_build_object('version',new_version)));
            RETURN NEXT;
        END $f$;
        REVOKE ALL ON FUNCTION platform.create_contact_note(uuid,uuid,text,text,bigint,text,text[]) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION platform.create_contact_note(uuid,uuid,text,text,bigint,text,text[]);
        """);
}
