# Wave 2 Plan 2 — contact list query plan

Candidate application code `9a9a4e0`; PostgreSQL 18.6 x86_64, disposable isolated local Docker container; no other user data was accessed. This is a representative synthetic fixture, not a production migration or an exit benchmark. It recreates the columns, RLS predicates, composite keys and relevant indexes used by these reads. It omits unrelated migration objects, uses a non-owner, non-BYPASSRLS SELECT role, and inserts two tenants with 10,000 active contacts each, two tags, and 4,086 total contact-tag associations. The keyset selects the earlier half of one tenant, page limit 21; the tag condition matches approximately one in five contacts. PostgreSQL `ANALYZE` precedes the reads. Execution times describe one warm fixture run, not p95 or a service-level claim.

Both reads used `contacts_tenant_updated`, applying the tenant and tuple condition in the index scan. With the tag predicate, PostgreSQL used a nested-loop semi join and `contact_tags_pkey` for tenant/contact/tag probes (132 probes for 21 matches); `tags` was small enough to scan. The other existing index `contact_tags_tenant_tag_contact` was present but was not selected for this fixture. No new index was added: this observation establishes an available access path, not performance at the exit load; low-selectivity or other distributions need the Wave 2 benchmark and its actual query plans.

Reproduction SQL, including fixture and query shape (explicit constants represent bound filters; absent optional predicates are omitted):

```sql
\set ON_ERROR_STOP on
CREATE SCHEMA crm;
CREATE ROLE list_reader NOLOGIN NOSUPERUSER NOBYPASSRLS;
CREATE TABLE crm.contacts (tenant_id uuid NOT NULL,id uuid NOT NULL,display_name text NOT NULL,email text,phone text,version bigint NOT NULL DEFAULT 1,created_at timestamptz NOT NULL,updated_at timestamptz NOT NULL,deleted_at timestamptz,PRIMARY KEY(tenant_id,id));
CREATE INDEX contacts_tenant_updated ON crm.contacts (tenant_id,updated_at DESC,id DESC) WHERE deleted_at IS NULL;
CREATE TABLE crm.tags (tenant_id uuid NOT NULL,id uuid NOT NULL,name text NOT NULL,normalized_name text NOT NULL,PRIMARY KEY(tenant_id,id),UNIQUE(tenant_id,normalized_name));
CREATE TABLE crm.contact_tags (tenant_id uuid NOT NULL,contact_id uuid NOT NULL,tag_id uuid NOT NULL,PRIMARY KEY(tenant_id,contact_id,tag_id),FOREIGN KEY(tenant_id,contact_id) REFERENCES crm.contacts(tenant_id,id),FOREIGN KEY(tenant_id,tag_id) REFERENCES crm.tags(tenant_id,id));
CREATE INDEX contact_tags_tenant_tag_contact ON crm.contact_tags(tenant_id,tag_id,contact_id);
ALTER TABLE crm.contacts ENABLE ROW LEVEL SECURITY; ALTER TABLE crm.contacts FORCE ROW LEVEL SECURITY;
ALTER TABLE crm.tags ENABLE ROW LEVEL SECURITY; ALTER TABLE crm.tags FORCE ROW LEVEL SECURITY;
ALTER TABLE crm.contact_tags ENABLE ROW LEVEL SECURITY; ALTER TABLE crm.contact_tags FORCE ROW LEVEL SECURITY;
CREATE POLICY contacts_tenant ON crm.contacts USING (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
CREATE POLICY tags_tenant ON crm.tags USING (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
CREATE POLICY contact_tags_tenant ON crm.contact_tags USING (tenant_id = NULLIF(current_setting('crm.tenant_id',true),'')::uuid);
GRANT USAGE ON SCHEMA crm TO list_reader; GRANT SELECT ON ALL TABLES IN SCHEMA crm TO list_reader;
INSERT INTO crm.contacts SELECT md5('tenant'||t)::uuid, md5('contact'||t||'-'||n)::uuid, 'contact '||n, NULL, NULL,1,'2026-01-01'::timestamptz,'2026-01-01'::timestamptz + n * interval '1 second',NULL FROM generate_series(1,2) t CROSS JOIN generate_series(1,10000) n;
INSERT INTO crm.tags SELECT md5('tenant'||t)::uuid,md5('tag1')::uuid,'tag','tag' FROM generate_series(1,2) t;
INSERT INTO crm.contact_tags SELECT tenant_id,id,md5('tag1')::uuid FROM crm.contacts WHERE mod(('x'||substr(replace(id::text,'-',''),1,8))::bit(32)::bigint,5)=0;
ANALYZE crm.contacts; ANALYZE crm.tags; ANALYZE crm.contact_tags;
SELECT version(); SELECT 'contacts' AS relation,count(*) FROM crm.contacts UNION ALL SELECT 'tags',count(*) FROM crm.tags UNION ALL SELECT 'contact_tags',count(*) FROM crm.contact_tags;
SET ROLE list_reader;
SELECT set_config('crm.tenant_id',md5('tenant1')::uuid::text,false);
EXPLAIN (ANALYZE, BUFFERS) SELECT c.id,c.display_name,c.email,c.phone,c.version,c.created_at,c.updated_at FROM crm.contacts c WHERE c.tenant_id=md5('tenant1')::uuid AND c.deleted_at IS NULL AND (c.updated_at,c.id) < ('2026-01-01 01:23:20+00'::timestamptz,'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid) ORDER BY c.updated_at DESC,c.id DESC LIMIT 21;
EXPLAIN (ANALYZE, BUFFERS) SELECT c.id,c.display_name,c.email,c.phone,c.version,c.created_at,c.updated_at FROM crm.contacts c WHERE c.tenant_id=md5('tenant1')::uuid AND c.deleted_at IS NULL AND EXISTS (SELECT 1 FROM crm.contact_tags ct JOIN crm.tags t ON t.tenant_id=ct.tenant_id AND t.id=ct.tag_id WHERE ct.tenant_id=c.tenant_id AND ct.contact_id=c.id AND ct.tag_id=md5('tag1')::uuid) AND (c.updated_at,c.id) < ('2026-01-01 01:23:20+00'::timestamptz,'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid) ORDER BY c.updated_at DESC,c.id DESC LIMIT 21;
```

Observed output, including actual buffers and row counts:

```text
                                                          version
----------------------------------------------------------------------------------------------------------------------------
 PostgreSQL 18.6 (Debian 18.6-1.pgdg12+2) on x86_64-pc-linux-gnu, compiled by gcc (Debian 12.2.0-14+deb12u1) 12.2.0, 64-bit
(1 row)

   relation   | count
--------------+-------
 contacts     | 20000
 tags         |     2
 contact_tags |  4086
(3 rows)

SET
              set_config
--------------------------------------
 febe0277-53c1-e6ce-9acd-bbd9c80a8407
(1 row)

                                                                                                        QUERY PLAN
--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
 Limit  (cost=0.41..5.82 rows=21 width=116) (actual time=0.023..0.044 rows=21.00 loops=1)
   Buffers: shared hit=5
   ->  Result  (cost=0.41..1288.05 rows=4999 width=116) (actual time=0.021..0.039 rows=21.00 loops=1)
         One-Time Filter: ((NULLIF(current_setting('crm.tenant_id'::text, true), ''::text))::uuid = 'febe0277-53c1-e6ce-9acd-bbd9c80a8407'::uuid)
         Buffers: shared hit=5
         ->  Index Scan using contacts_tenant_updated on contacts c  (cost=0.41..1288.05 rows=4999 width=116) (actual time=0.013..0.025 rows=21.00 loops=1)
               Index Cond: ((tenant_id = 'febe0277-53c1-e6ce-9acd-bbd9c80a8407'::uuid) AND (ROW(updated_at, id) < ROW('2026-01-01 01:23:20+00'::timestamp with time zone, 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid)))
               Index Searches: 1
               Buffers: shared hit=5
 Planning:
   Buffers: shared hit=41 read=4
 Planning Time: 0.395 ms
 Execution Time: 0.088 ms
(13 rows)

                                                                                                           QUERY PLAN
--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
 Limit  (cost=0.70..353.19 rows=21 width=116) (actual time=0.063..0.530 rows=21.00 loops=1)
   Buffers: shared hit=314
   ->  Result  (cost=0.70..8393.21 rows=500 width=116) (actual time=0.062..0.525 rows=21.00 loops=1)
         One-Time Filter: ((NULLIF(current_setting('crm.tenant_id'::text, true), ''::text))::uuid = 'febe0277-53c1-e6ce-9acd-bbd9c80a8407'::uuid)
         Buffers: shared hit=314
         ->  Nested Loop Semi Join  (cost=0.70..8393.21 rows=500 width=116) (actual time=0.056..0.514 rows=21.00 loops=1)
               Buffers: shared hit=314
               ->  Index Scan using contacts_tenant_updated on contacts c  (cost=0.41..1288.05 rows=4999 width=132) (actual time=0.012..0.058 rows=132.00 loops=1)
                     Index Cond: ((tenant_id = 'febe0277-53c1-e6ce-9acd-bbd9c80a8407'::uuid) AND (ROW(updated_at, id) < ROW('2026-01-01 01:23:20+00'::timestamp with time zone, 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid)))
                     Index Searches: 1
                     Buffers: shared hit=8
               ->  Nested Loop  (cost=0.28..1.41 rows=1 width=32) (actual time=0.003..0.003 rows=0.16 loops=132)
                     Buffers: shared hit=306
                     ->  Index Only Scan using contact_tags_pkey on contact_tags ct  (cost=0.28..0.37 rows=1 width=48) (actual time=0.003..0.003 rows=0.16 loops=132)
                           Index Cond: ((tenant_id = 'febe0277-53c1-e6ce-9acd-bbd9c80a8407'::uuid) AND (contact_id = c.id) AND (tag_id = 'e9bae3ce-1d7a-c00b-0b1a-a2fbddc50cfb'::uuid))
                           Heap Fetches: 21
                           Index Searches: 132
                           Buffers: shared hit=285
                     ->  Seq Scan on tags t  (cost=0.00..1.03 rows=1 width=32) (actual time=0.001..0.001 rows=1.00 loops=21)
                           Filter: ((tenant_id = 'febe0277-53c1-e6ce-9acd-bbd9c80a8407'::uuid) AND (id = 'e9bae3ce-1d7a-c00b-0b1a-a2fbddc50cfb'::uuid))
                           Buffers: shared hit=21
 Planning:
   Buffers: shared hit=58
 Planning Time: 0.626 ms
 Execution Time: 0.591 ms
(25 rows)

```

The application does not provide snapshot pagination: updates can move rows across the anchor between requests. Signed cursors remain tenant/filter-bound, authorize each request independently, and key provisioning and rotation follow the [cursor runbook](../operations/08-CONTACT-CURSOR-KEY.md). Scope and evidence limits are in the [handoff](../handoff/2026-11-27-wave-2-branch-handoff.md).
