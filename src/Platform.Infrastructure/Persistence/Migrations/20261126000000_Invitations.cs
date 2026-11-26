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
                CREATE FUNCTION platform.require_invite_manager(t uuid, iss text, sub text) RETURNS void
                LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $f$
                BEGIN
                  IF t IS DISTINCT FROM nullif(current_setting('crm.tenant_id',true),'')::uuid
                      OR NOT platform.lock_active_membership(iss, sub, t)
                      OR NOT platform.lock_verified_staff(iss, sub)
                      OR NOT EXISTS (SELECT FROM platform.memberships m WHERE m.tenant_id=t AND m.issuer=iss
                                       AND m.subject=sub AND m.status='active' AND m.role IN ('owner','admin')) THEN
                    RAISE insufficient_privilege;
                  END IF;
                END $f$;
                CREATE FUNCTION platform.create_invite(t uuid, iss text, sub text, correlation text, digest bytea, original text, normalized text, invited_role text)
                RETURNS TABLE(id uuid,expires_at timestamptz) LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $f$
                DECLARE new_id uuid := gen_random_uuid(); created timestamptz;
                BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('crm.membership:' || t::text,0));
                  PERFORM platform.require_invite_manager(t,iss,sub);
                  created := clock_timestamp();
                  INSERT INTO platform.invites(tenant_id,id,token_hash,email,normalized_email,role,created_at,expires_at)
                    VALUES(t,new_id,digest,original,normalized,invited_role,created,created+interval '24 hours');
                  INSERT INTO audit.entries(id,tenant_id,actor_issuer,actor_subject,action,target_id,summary,correlation_id)
                    VALUES(gen_random_uuid(),t,iss,sub,'invite.created',new_id,'Invitation created',correlation);
                  RETURN QUERY SELECT new_id,created+interval '24 hours';
                END $f$;
                CREATE FUNCTION platform.list_invites(t uuid, iss text, sub text, anchor uuid, take_count integer)
                RETURNS TABLE(id uuid,email text,role text,expires_at timestamptz,status text)
                LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $f$
                BEGIN
                  PERFORM platform.require_invite_manager(t,iss,sub);
                  RETURN QUERY SELECT i.id,i.email,i.role,i.expires_at,
                    CASE WHEN i.accepted_at IS NOT NULL THEN 'accepted' WHEN i.revoked_at IS NOT NULL THEN 'revoked'
                         WHEN i.expires_at<=statement_timestamp() THEN 'expired' ELSE 'pending' END
                    FROM platform.invites i WHERE i.tenant_id=t AND (anchor IS NULL OR i.id>anchor)
                    ORDER BY i.id LIMIT least(101,greatest(1,take_count));
                END $f$;
                CREATE FUNCTION platform.revoke_invite(t uuid, iss text, sub text, correlation text, target uuid)
                RETURNS text LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $f$
                DECLARE row_data platform.invites%ROWTYPE; revoked timestamptz;
                BEGIN
                  PERFORM pg_advisory_xact_lock(hashtextextended('crm.membership:' || t::text,0));
                  PERFORM platform.require_invite_manager(t,iss,sub);
                  SELECT * INTO row_data FROM platform.invites i WHERE i.tenant_id=t AND i.id=target FOR UPDATE;
                  IF NOT FOUND THEN RETURN 'INVITE_NOT_FOUND'; END IF;
                  revoked := clock_timestamp();
                  IF row_data.accepted_at IS NOT NULL OR row_data.revoked_at IS NOT NULL OR row_data.expires_at<=revoked THEN
                    RETURN 'INVITE_STATE_CONFLICT';
                  END IF;
                  UPDATE platform.invites SET revoked_at=revoked WHERE tenant_id=t AND id=target;
                  INSERT INTO audit.entries(id,tenant_id,actor_issuer,actor_subject,action,target_id,summary,correlation_id)
                    VALUES(gen_random_uuid(),t,iss,sub,'invite.revoked',target,'Invitation revoked',correlation);
                  RETURN 'revoked';
                END $f$;
                REVOKE ALL ON FUNCTION platform.create_invite(uuid,text,text,text,bytea,text,text,text), platform.list_invites(uuid,text,text,uuid,integer), platform.revoke_invite(uuid,text,text,text,uuid), platform.require_invite_manager(uuid,text,text) FROM PUBLIC;
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
                DROP FUNCTION platform.create_invite(uuid,text,text,text,bytea,text,text,text), platform.list_invites(uuid,text,text,uuid,integer), platform.revoke_invite(uuid,text,text,text,uuid), platform.require_invite_manager(uuid,text,text);
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
