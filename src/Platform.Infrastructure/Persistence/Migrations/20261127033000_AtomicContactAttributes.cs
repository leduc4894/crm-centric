using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261127033000_AtomicContactAttributes")]
public sealed class AtomicContactAttributes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Remove existing grants as well as the grants used on a fresh database.
        DO $acl$ DECLARE g record; BEGIN
            FOR g IN SELECT p.oid::regprocedure AS routine, r.rolname
                FROM pg_proc p CROSS JOIN LATERAL aclexplode(p.proacl) a
                JOIN pg_roles r ON r.oid=a.grantee
                WHERE p.pronamespace='platform'::regnamespace AND p.proname IN ('set_contact_tag','set_contact_field')
                    AND a.grantee<>p.proowner AND a.privilege_type='EXECUTE'
            LOOP
                EXECUTE format('REVOKE EXECUTE ON FUNCTION %s FROM %I CASCADE',g.routine,g.rolname);
            END LOOP;
        END $acl$;

        CREATE FUNCTION platform.mutate_contact_attribute(t uuid, contact uuid, attribute uuid, iss text, sub text,
            expected bigint, correlation text, kind text, raw text)
        RETURNS text LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $f$
        DECLARE current_version bigint; changed boolean; outcome text; event uuid := gen_random_uuid();
            occurred timestamptz := clock_timestamp(); event_type constant text := 'crm.contact.updated.v1';
        BEGIN
            IF t IS DISTINCT FROM nullif(current_setting('crm.tenant_id',true),'')::uuid
                OR NOT platform.lock_active_membership(iss,sub,t) OR NOT platform.lock_verified_staff(iss,sub)
                OR NOT EXISTS (SELECT FROM platform.memberships m WHERE m.tenant_id=t AND m.issuer=iss
                    AND m.subject=sub AND m.status='active' AND m.role IN ('owner','admin','agent')) THEN
                RAISE insufficient_privilege;
            END IF;
            SELECT version INTO current_version FROM crm.contacts
                WHERE tenant_id=t AND id=contact AND deleted_at IS NULL FOR UPDATE;
            IF NOT FOUND THEN RETURN 'missing'; END IF;
            IF expected IS DISTINCT FROM current_version THEN RETURN 'conflict'; END IF;
            IF kind IN ('attach','detach') THEN
                SELECT platform.set_contact_tag(t,contact,attribute,iss,sub,kind='attach') INTO changed;
                IF changed IS NULL THEN RETURN 'missing'; END IF;
                IF NOT changed THEN RETURN 'unchanged'; END IF;
            ELSIF kind='field' THEN
                outcome := platform.set_contact_field(t,contact,attribute,iss,sub,raw);
                IF outcome <> 'changed' THEN RETURN outcome; END IF;
            ELSE
                RAISE invalid_parameter_value;
            END IF;
            UPDATE crm.contacts SET version=version+1,updated_at=now()
                WHERE tenant_id=t AND id=contact AND version=expected AND deleted_at IS NULL
                RETURNING version INTO STRICT current_version;
            PERFORM platform.append_contact_audit(t,contact,iss,sub,'contact.updated',correlation);
            INSERT INTO outbox.events(tenant_id,id,contact_id,type,occurred_at,payload)
                VALUES(t,event,contact,event_type,occurred,jsonb_build_object('eventId',event,'tenantId',t,
                    'contactId',contact,'eventType',event_type,'occurredAt',occurred,
                    'payload',jsonb_build_object('version',current_version)));
            RETURN 'changed';
        END $f$;
        REVOKE ALL ON FUNCTION platform.mutate_contact_attribute(uuid,uuid,uuid,text,text,bigint,text,text,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION platform.mutate_contact_attribute(uuid,uuid,uuid,text,text,bigint,text,text,text);
        """);
}
