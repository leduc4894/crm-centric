\set ON_ERROR_STOP on
-- Only run on a newly provisioned, disposable database named crm_bench_<id>.
-- psql variables: tenant_a, tenant_b, bench_confirm=ISOLATED_BENCHMARK_ONLY.
SELECT :'bench_confirm' = 'ISOLATED_BENCHMARK_ONLY' AS confirmed \gset
\if :confirmed
\else
  \echo 'BENCHMARK_CONFIRMATION_REQUIRED'
  SELECT 1 / 0;
\endif
BEGIN;
SELECT set_config('bench.tenant_a', :'tenant_a', true) AS ignored \gset
SELECT set_config('bench.tenant_b', :'tenant_b', true) AS ignored \gset
DO $guard$
DECLARE a uuid := current_setting('bench.tenant_a')::uuid;
        b uuid := current_setting('bench.tenant_b')::uuid;
BEGIN
  IF current_database() !~ '^crm_bench_[a-z0-9_]+$' OR a = b THEN
    RAISE EXCEPTION 'BENCHMARK_DATABASE_IDENTITY_INVALID';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM platform.tenants WHERE id=a)
     OR NOT EXISTS (SELECT 1 FROM platform.tenants WHERE id=b) THEN
    RAISE EXCEPTION 'BENCHMARK_TENANTS_MISSING';
  END IF;
END $guard$;
SELECT set_config('crm.tenant_id', current_setting('bench.tenant_a'), true) AS ignored \gset
INSERT INTO crm.contacts (tenant_id,id,display_name,email,created_at,updated_at)
SELECT current_setting('bench.tenant_a')::uuid,
       md5('crm-bench-v1:' || n::text)::uuid,
       'Bench segment-' || lpad((n % 100)::text,2,'0') || ' contact ' || n::text,
       'contact-' || n::text || '@benchmark.invalid',
       '2026-01-01T00:00:00Z'::timestamptz,
       '2026-01-01T00:00:00Z'::timestamptz + (n % 1000) * interval '1 second'
FROM generate_series(1,50000) AS n
ON CONFLICT (tenant_id,id) DO NOTHING;
DO $verify_a$
BEGIN
  IF (SELECT count(*) FROM crm.contacts WHERE tenant_id=current_setting('bench.tenant_a')::uuid AND deleted_at IS NULL) <> 50000
     OR (SELECT count(*) FROM crm.contacts c JOIN generate_series(1,50000) n
         ON c.id=md5('crm-bench-v1:' || n::text)::uuid
         WHERE c.tenant_id=current_setting('bench.tenant_a')::uuid AND c.deleted_at IS NULL
           AND c.display_name='Bench segment-' || lpad((n % 100)::text,2,'0') || ' contact ' || n::text
           AND c.email='contact-' || n::text || '@benchmark.invalid') <> 50000 THEN
    RAISE EXCEPTION 'BENCHMARK_DATASET_MISMATCH';
  END IF;
END $verify_a$;
INSERT INTO crm.tags (tenant_id,id,name,normalized_name)
SELECT current_setting('bench.tenant_a')::uuid, md5('crm-bench-v1:tag:' || n::text)::uuid,
       'Bench tag ' || n::text, 'bench tag ' || n::text
FROM generate_series(0,9) AS n
ON CONFLICT (tenant_id,id) DO NOTHING;
INSERT INTO crm.contact_tags (tenant_id,contact_id,tag_id)
SELECT current_setting('bench.tenant_a')::uuid, md5('crm-bench-v1:' || n::text)::uuid,
       md5('crm-bench-v1:tag:' || (n % 10)::text)::uuid
FROM generate_series(1,50000) AS n
ON CONFLICT (tenant_id,contact_id,tag_id) DO NOTHING;
DO $verify_tags$
BEGIN
  IF (SELECT count(*) FROM crm.tags WHERE tenant_id=current_setting('bench.tenant_a')::uuid) <> 10
     OR (SELECT count(*) FROM crm.contact_tags WHERE tenant_id=current_setting('bench.tenant_a')::uuid) <> 50000
     OR (SELECT count(*) FROM crm.contact_tags ct JOIN generate_series(1,50000) n
         ON ct.contact_id=md5('crm-bench-v1:' || n::text)::uuid
         AND ct.tag_id=md5('crm-bench-v1:tag:' || (n % 10)::text)::uuid
         WHERE ct.tenant_id=current_setting('bench.tenant_a')::uuid) <> 50000 THEN
    RAISE EXCEPTION 'BENCHMARK_TAGS_MISMATCH';
  END IF;
END $verify_tags$;
SELECT set_config('crm.tenant_id', current_setting('bench.tenant_b'), true) AS ignored \gset
INSERT INTO crm.contacts (tenant_id,id,display_name,email,created_at,updated_at)
VALUES (current_setting('bench.tenant_b')::uuid, md5('crm-bench-v1:tenant-b-marker')::uuid,
        'Bench tenant B marker', 'tenant-b-marker@benchmark.invalid',
        '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
ON CONFLICT (tenant_id,id) DO NOTHING;
DO $verify_b$
BEGIN
  IF (SELECT count(*) FROM crm.contacts WHERE tenant_id=current_setting('bench.tenant_b')::uuid AND deleted_at IS NULL) <> 1
     OR NOT EXISTS (SELECT 1 FROM crm.contacts WHERE tenant_id=current_setting('bench.tenant_b')::uuid
       AND id=md5('crm-bench-v1:tenant-b-marker')::uuid AND display_name='Bench tenant B marker'
       AND email='tenant-b-marker@benchmark.invalid' AND deleted_at IS NULL) THEN
    RAISE EXCEPTION 'BENCHMARK_MARKER_MISMATCH';
  END IF;
END $verify_b$;
ANALYZE crm.contacts;
ANALYZE crm.tags;
ANALYZE crm.contact_tags;
COMMIT;
