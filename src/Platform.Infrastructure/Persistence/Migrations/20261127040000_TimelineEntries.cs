using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261127040000_TimelineEntries")]
public sealed class TimelineEntries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE crm.timeline_entries (
            tenant_id uuid NOT NULL,
            id uuid NOT NULL,
            contact_id uuid NOT NULL,
            inbox_id uuid,
            entry_kind text NOT NULL,
            source text NOT NULL,
            type text NOT NULL,
            occurred_at timestamptz NOT NULL,
            summary bytea NOT NULL CHECK (octet_length(summary) <= 4000),
            PRIMARY KEY (tenant_id,id),
            FOREIGN KEY (tenant_id,contact_id) REFERENCES crm.contacts(tenant_id,id) ON DELETE RESTRICT
        );
        COMMENT ON COLUMN crm.timeline_entries.summary IS 'UTF-8 bytes, including U+0000. Writers enforce the contract Unicode scalar limit.';
        CREATE INDEX timeline_contact_order ON crm.timeline_entries(tenant_id,contact_id,occurred_at DESC,id DESC);
        CREATE UNIQUE INDEX timeline_inbox_kind ON crm.timeline_entries(tenant_id,contact_id,inbox_id,entry_kind) WHERE inbox_id IS NOT NULL;
        ALTER TABLE crm.timeline_entries ENABLE ROW LEVEL SECURITY;
        ALTER TABLE crm.timeline_entries FORCE ROW LEVEL SECURITY;
        CREATE POLICY timeline_tenant ON crm.timeline_entries
            USING (tenant_id=NULLIF(current_setting('crm.tenant_id',true),'')::uuid)
            WITH CHECK (tenant_id=NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
        REVOKE ALL ON crm.timeline_entries FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE crm.timeline_entries IN ACCESS EXCLUSIVE MODE;
        DO $body$ DECLARE tenant_row record; BEGIN
            FOR tenant_row IN SELECT id FROM platform.tenants LOOP
                PERFORM set_config('crm.tenant_id',tenant_row.id::text,true);
                IF EXISTS(SELECT FROM crm.timeline_entries) THEN
                    RAISE EXCEPTION 'timeline data exists; back up and use a forward fix';
                END IF;
            END LOOP;
        END $body$;
        DROP TABLE crm.timeline_entries;
        """);
}
