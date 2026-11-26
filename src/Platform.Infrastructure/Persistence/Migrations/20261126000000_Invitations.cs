using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Invitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "entries_action_check",
                schema: "audit",
                table: "entries");

            migrationBuilder.CreateTable(
                name: "invites",
                schema: "platform",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    normalized_email = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("invites_pkey", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("invites_check", "accepted_at IS NULL OR revoked_at IS NULL");
                    table.CheckConstraint("invites_email_check", "char_length(email) BETWEEN 1 AND 320");
                    table.CheckConstraint("invites_expires_at_check", "expires_at=created_at + interval '24 hours'");
                    table.CheckConstraint("invites_normalized_email_check", "char_length(normalized_email) BETWEEN 1 AND 320");
                    table.CheckConstraint("invites_role_check", "role IN ('admin','agent','operator')");
                    table.CheckConstraint("invites_token_hash_check", "octet_length(token_hash)=32");
                    table.ForeignKey(
                        name: "invites_tenant_id_fkey",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "entries_action_check",
                schema: "audit",
                table: "entries",
                sql: "action IN ('tenant.created','membership.updated','invite.created','invite.revoked','invite.accepted')");

            migrationBuilder.CreateIndex(
                name: "invites_token_hash_key",
                schema: "platform",
                table: "invites",
                column: "token_hash",
                unique: true);
            migrationBuilder.Sql("""
                ALTER TABLE platform.invites ENABLE ROW LEVEL SECURITY;
                CREATE POLICY invite_tenant ON platform.invites USING (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
                REVOKE ALL ON platform.invites FROM PUBLIC;
                CREATE FUNCTION platform.invite_tenant_for_hash(p_hash bytea) RETURNS uuid
                  LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog AS $body$
                  SELECT i.tenant_id FROM platform.invites i WHERE i.token_hash=p_hash
                $body$;
                REVOKE ALL ON FUNCTION platform.invite_tenant_for_hash(bytea) FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE platform.invites, audit.entries IN ACCESS EXCLUSIVE MODE;
                DO $body$ BEGIN
                  IF EXISTS (SELECT 1 FROM platform.invites) OR EXISTS (SELECT 1 FROM audit.entries WHERE action IN ('invite.created','invite.revoked','invite.accepted')) THEN
                    RAISE EXCEPTION 'invite or audit data exists; back up and use a forward fix';
                  END IF;
                END $body$;
                DROP FUNCTION platform.invite_tenant_for_hash(bytea);
                """);
            migrationBuilder.DropTable(
                name: "invites",
                schema: "platform");

            migrationBuilder.DropCheckConstraint(
                name: "entries_action_check",
                schema: "audit",
                table: "entries");

            migrationBuilder.AddCheckConstraint(
                name: "entries_action_check",
                schema: "audit",
                table: "entries",
                sql: "action IN ('tenant.created','membership.updated')");
        }
    }
}
