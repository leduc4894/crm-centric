using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261127020000_ContactTagsFoundation")]
public sealed class ContactTagsFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE crm.tags (
          tenant_id uuid NOT NULL REFERENCES platform.tenants(id) ON DELETE RESTRICT,
          id uuid NOT NULL,
          name text NOT NULL,
          normalized_name text NOT NULL,
          PRIMARY KEY (tenant_id,id),
          UNIQUE (tenant_id,normalized_name)
        );
        CREATE TABLE crm.contact_tags (
          tenant_id uuid NOT NULL,
          contact_id uuid NOT NULL,
          tag_id uuid NOT NULL,
          PRIMARY KEY (tenant_id,contact_id,tag_id),
          FOREIGN KEY (tenant_id,contact_id) REFERENCES crm.contacts(tenant_id,id) ON DELETE RESTRICT,
          FOREIGN KEY (tenant_id,tag_id) REFERENCES crm.tags(tenant_id,id) ON DELETE RESTRICT
        );
        CREATE INDEX contact_tags_tenant_tag_contact ON crm.contact_tags (tenant_id,tag_id,contact_id);
        ALTER TABLE crm.tags ENABLE ROW LEVEL SECURITY;
        ALTER TABLE crm.tags FORCE ROW LEVEL SECURITY;
        CREATE POLICY tags_tenant ON crm.tags USING (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid)
          WITH CHECK (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
        ALTER TABLE crm.contact_tags ENABLE ROW LEVEL SECURITY;
        ALTER TABLE crm.contact_tags FORCE ROW LEVEL SECURITY;
        CREATE POLICY contact_tags_tenant ON crm.contact_tags USING (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid)
          WITH CHECK (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
        REVOKE ALL ON crm.tags, crm.contact_tags FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE crm.tags, crm.contact_tags IN ACCESS EXCLUSIVE MODE;
        DO $body$ DECLARE tenant_row record; BEGIN
          FOR tenant_row IN SELECT id FROM platform.tenants LOOP
            PERFORM set_config('crm.tenant_id',tenant_row.id::text,true);
            IF EXISTS (SELECT FROM crm.tags) OR EXISTS (SELECT FROM crm.contact_tags) THEN
              RAISE EXCEPTION 'tag data exists; back up and use a forward fix';
            END IF;
          END LOOP;
        END $body$;
        DROP TABLE crm.contact_tags;
        DROP TABLE crm.tags;
        """);
}
