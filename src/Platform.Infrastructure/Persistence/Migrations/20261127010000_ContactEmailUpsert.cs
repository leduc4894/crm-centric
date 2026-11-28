using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261127010000_ContactEmailUpsert")]
public sealed class ContactEmailUpsert : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Serialize the preflight with contact writes; FORCE RLS requires checking every tenant.
        LOCK TABLE crm.contacts IN ACCESS EXCLUSIVE MODE;
        DO $body$ DECLARE tenant_row record; BEGIN
          FOR tenant_row IN SELECT id FROM platform.tenants LOOP
            PERFORM set_config('crm.tenant_id',tenant_row.id::text,true);
            IF EXISTS (
              SELECT FROM crm.contacts
              WHERE deleted_at IS NULL AND email IS NOT NULL
              GROUP BY tenant_id, lower(btrim(email)) HAVING count(*) > 1
            ) THEN
              RAISE EXCEPTION 'CONTACT_EMAIL_DUPLICATES: resolve duplicate contacts before migration';
            END IF;
          END LOOP;
        END $body$;
        ALTER TABLE crm.contacts ADD COLUMN email_normalized text
          GENERATED ALWAYS AS (lower(btrim(email))) STORED;
        CREATE UNIQUE INDEX contacts_active_email_unique ON crm.contacts (tenant_id,email_normalized)
          WHERE deleted_at IS NULL AND email IS NOT NULL;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE crm.contacts IN ACCESS EXCLUSIVE MODE;
        DO $body$ DECLARE tenant_row record; BEGIN
          FOR tenant_row IN SELECT id FROM platform.tenants LOOP
            PERFORM set_config('crm.tenant_id',tenant_row.id::text,true);
            IF EXISTS (SELECT FROM crm.contacts) THEN
              RAISE EXCEPTION 'contact data exists; back up and use a forward fix';
            END IF;
          END LOOP;
        END $body$;
        DROP INDEX crm.contacts_active_email_unique;
        ALTER TABLE crm.contacts DROP COLUMN email_normalized;
        """);
}
