using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261128000000_ManifestRegistry")]
public sealed class ManifestRegistry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE platform.manifest_candidates (
            id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            module_id text NOT NULL,
            release_version text NOT NULL CHECK (release_version ~ '^(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*)){2}$'),
            manifest_bytes bytea NOT NULL,
            manifest_hash bytea GENERATED ALWAYS AS (sha256(manifest_bytes)) STORED,
            publisher_key_id text NOT NULL,
            trust_revision bigint NOT NULL,
            requested_origins text[] NOT NULL,
            proposed_scopes text[] NOT NULL,
            descriptor_bytes bytea[] NOT NULL,
            descriptor_hashes bytea[] NOT NULL,
            status text NOT NULL DEFAULT 'verified_candidate' CHECK (status='verified_candidate')
        );
        -- The contract does not bound the length of a numeric version component.
        CREATE UNIQUE INDEX manifest_release ON platform.manifest_candidates(module_id,sha256(decode(release_version,'escape')));
        CREATE INDEX manifest_latest ON platform.manifest_candidates(module_id,id DESC);
        CREATE TABLE platform.manifest_schemas (
            candidate_id bigint NOT NULL REFERENCES platform.manifest_candidates(id),
            ordinal integer NOT NULL,
            uri text NOT NULL,
            schema_version text NOT NULL,
            schema_bytes bytea NOT NULL,
            schema_hash bytea GENERATED ALWAYS AS (sha256(schema_bytes)) STORED,
            PRIMARY KEY(candidate_id,ordinal)
        );
        CREATE FUNCTION platform.register_manifest(body bytea, key_id text, revision bigint, origins text[],
            uris text[], versions text[], bodies bytea[], module text, release text, scopes text[], descriptors bytea[])
        RETURNS text LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $fn$
        DECLARE
            latest platform.manifest_candidates%ROWTYPE;
            candidate bigint;
            incoming_parts text[] := string_to_array(release,'.');
            latest_parts text[];
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended(module, 24873));
            SELECT * INTO latest FROM platform.manifest_candidates
              WHERE module_id=module ORDER BY id DESC LIMIT 1;
            IF FOUND THEN
                IF release=latest.release_version THEN
                    IF latest.manifest_bytes=body THEN RETURN 'already_registered'; END IF;
                    RETURN 'version_conflict';
                END IF;
                latest_parts := string_to_array(latest.release_version,'.');
                FOR i IN 1..3 LOOP
                    -- Compare canonical unsigned decimals without a fixed-width integer conversion.
                    IF (length(incoming_parts[i]),incoming_parts[i] COLLATE "C") <
                       (length(latest_parts[i]),latest_parts[i] COLLATE "C") THEN RETURN 'downgrade'; END IF;
                    EXIT WHEN incoming_parts[i] <> latest_parts[i];
                END LOOP;
            END IF;
            IF cardinality(uris) IS DISTINCT FROM cardinality(versions) OR
                cardinality(uris) IS DISTINCT FROM cardinality(bodies) THEN RAISE invalid_parameter_value; END IF;
            INSERT INTO platform.manifest_candidates(module_id,release_version,manifest_bytes,publisher_key_id,
                trust_revision,requested_origins,proposed_scopes,descriptor_bytes,descriptor_hashes)
            VALUES(module,release,body,key_id,revision,origins,scopes,descriptors,ARRAY(SELECT sha256(d) FROM unnest(descriptors) AS d))
            RETURNING id INTO candidate;
            INSERT INTO platform.manifest_schemas(candidate_id,ordinal,uri,schema_version,schema_bytes)
            SELECT candidate,i,uris[i],versions[i],bodies[i] FROM generate_subscripts(uris,1) AS i;
            RETURN 'inserted';
        END $fn$;
        REVOKE ALL ON FUNCTION platform.register_manifest(bytea,text,bigint,text[],text[],text[],bytea[],text,text,text[],bytea[]) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE platform.manifest_candidates, platform.manifest_schemas IN ACCESS EXCLUSIVE MODE;
        DO $guard$ BEGIN
            IF EXISTS (SELECT FROM platform.manifest_candidates) THEN
                RAISE EXCEPTION 'manifest_registry_contains_data';
            END IF;
        END $guard$;
        DROP FUNCTION platform.register_manifest(bytea,text,bigint,text[],text[],text[],bytea[],text,text,text[],bytea[]);
        DROP TABLE platform.manifest_schemas, platform.manifest_candidates;
        """);
}
