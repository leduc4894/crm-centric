using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261127031000_ContactTagAssignment")]
public sealed class ContactTagAssignment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION platform.set_contact_tag(t uuid, contact uuid, tag uuid, iss text, sub text, attach boolean)
        RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $f$
        BEGIN
            IF t IS DISTINCT FROM NULLIF(current_setting('crm.tenant_id',true),'')::uuid
                OR NOT platform.lock_active_membership(iss,sub,t) OR NOT platform.lock_verified_staff(iss,sub)
                OR NOT EXISTS (SELECT FROM platform.memberships m WHERE m.tenant_id=t AND m.issuer=iss
                    AND m.subject=sub AND m.status='active' AND m.role IN ('owner','admin','agent')) THEN
                RAISE insufficient_privilege;
            END IF;
            IF NOT EXISTS (SELECT FROM crm.contacts WHERE tenant_id=t AND id=contact AND deleted_at IS NULL FOR UPDATE)
                OR NOT EXISTS (SELECT FROM crm.tags WHERE tenant_id=t AND id=tag) THEN
                RETURN NULL;
            END IF;
            IF attach THEN
                INSERT INTO crm.contact_tags(tenant_id,contact_id,tag_id) VALUES(t,contact,tag)
                    ON CONFLICT(tenant_id,contact_id,tag_id) DO NOTHING;
            ELSE
                DELETE FROM crm.contact_tags WHERE tenant_id=t AND contact_id=contact AND tag_id=tag;
            END IF;
            RETURN FOUND;
        END $f$;
        REVOKE ALL ON FUNCTION platform.set_contact_tag(uuid,uuid,uuid,text,text,boolean) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION platform.set_contact_tag(uuid,uuid,uuid,text,text,boolean);
        """);
}
