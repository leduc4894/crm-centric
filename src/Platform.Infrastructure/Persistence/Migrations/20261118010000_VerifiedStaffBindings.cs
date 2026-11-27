using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261118010000_VerifiedStaffBindings")]
public sealed class VerifiedStaffBindings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE platform.staff_bindings (
          issuer text NOT NULL CHECK (char_length(issuer) <= 2048 AND char_length(btrim(issuer)) > 0),
          subject text NOT NULL CHECK (char_length(subject) <= 256 AND char_length(btrim(subject)) > 0),
          status text NOT NULL CHECK (status IN ('active','revoked')),
          verification_reference text NOT NULL CHECK (char_length(verification_reference) <= 200 AND char_length(btrim(verification_reference)) > 0),
          PRIMARY KEY (issuer, subject)
        );
        REVOKE ALL ON platform.staff_bindings FROM PUBLIC;
        CREATE TABLE audit.staff_binding_changes (
          id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
          issuer text NOT NULL, subject text NOT NULL,
          old_status text, new_status text NOT NULL,
          verification_reference text NOT NULL,
          operator_name text NOT NULL, changed_at timestamptz NOT NULL DEFAULT now()
        );
        REVOKE ALL ON audit.staff_binding_changes FROM PUBLIC;
        CREATE FUNCTION platform.audit_staff_binding_change() RETURNS trigger
          LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $body$
        BEGIN
          IF TG_OP = 'UPDATE' AND (NEW.issuer, NEW.subject) IS DISTINCT FROM (OLD.issuer, OLD.subject) THEN
            RAISE EXCEPTION 'staff identity is immutable' USING ERRCODE='22023';
          END IF;
          INSERT INTO audit.staff_binding_changes(issuer,subject,old_status,new_status,verification_reference,operator_name)
            VALUES(NEW.issuer,NEW.subject,CASE WHEN TG_OP='INSERT' THEN NULL ELSE OLD.status END,
              NEW.status,NEW.verification_reference,session_user);
          RETURN NEW;
        END $body$;
        REVOKE ALL ON FUNCTION platform.audit_staff_binding_change() FROM PUBLIC;
        CREATE TRIGGER staff_binding_audit AFTER INSERT OR UPDATE ON platform.staff_bindings
          FOR EACH ROW EXECUTE FUNCTION platform.audit_staff_binding_change();
        CREATE FUNCTION platform.lock_verified_staff(p_issuer text,p_subject text) RETURNS boolean
          LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $body$
        BEGIN
          PERFORM 1 FROM platform.staff_bindings b
            WHERE b.issuer=p_issuer AND b.subject=p_subject AND b.status='active' FOR SHARE;
          RETURN FOUND;
        END $body$;
        REVOKE ALL ON FUNCTION platform.lock_verified_staff(text,text) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $body$ BEGIN
          IF EXISTS (SELECT 1 FROM audit.staff_binding_changes) OR EXISTS (SELECT 1 FROM platform.staff_bindings) THEN
            RAISE EXCEPTION 'staff binding audit exists; back up and use a forward fix';
          END IF;
        END $body$;
        DROP FUNCTION platform.lock_verified_staff(text,text);
        DROP TABLE platform.staff_bindings;
        DROP FUNCTION platform.audit_staff_binding_change();
        DROP TABLE audit.staff_binding_changes;
        """);
}
