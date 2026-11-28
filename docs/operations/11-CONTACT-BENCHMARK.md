# Contact list benchmark

Plan: [Wave 2 Plan 9](../superpowers/plans/2026-11-27-wave-2-plan-9-exit.md). This document describes the harness, recorded measurements and remaining reproducibility gaps. Use an isolated, disposable environment and a clean checkout of the exact candidate. Do not run the seed against an existing application database.

## Dataset

Apply the application's migrations to a newly created, disposable PostgreSQL database named `crm_bench_<lowercase-id>`. Create two distinct synthetic tenants. Run `scripts/benchmarks/seed_contacts.sql` with psql variables `tenant_a`, `tenant_b`, and `bench_confirm=ISOLATED_BENCHMARK_ONLY`. The script checks the database name, tenant existence and distinctness, uses a transaction and fails if the expected dataset cannot be verified. It creates 50,000 active tenant-A contacts, 10 tags and one tag per contact, with deterministic IDs derived from `crm-bench-v1`; names cover 100 equal segments and `segment-07` has 500 contacts. Synthetic emails are unique, and update timestamps span 1,000 seconds. Tenant B contains one marker. It runs ANALYZE; rerunning it must leave the same dataset. The integration test covers reruns and guards using disposable PostgreSQL.

The seed uses a privileged provisioning connection. The measured API must use the real restricted application role and tenant policies; never measure via SQL or an RLS-bypassing API role. Verify dataset counts and marker using an appropriate tenant-scoped connection, and verify that tenant-A users cannot read tenant B. Destroy only resources whose disposable identity and ownership are confirmed.

## Workload and evidence

Provision a real IdP and API from the candidate, and 50 distinct active authenticated identities bound to tenant A. Their tokens must be accepted by the API's normal issuer/signature and tenant checks. The private input JSON for `scripts/benchmarks/contact_list.py` contains `api_origin`, `tenant_id`, `other_tenant_id`, `other_marker_id`, `allowed_ids` (all expected synthetic tenant-A contact UUIDs), `search_term`, `issuer`, `candidate_sha`, and `users` (50 objects with `subject` and `token`). Construct this file through a secure local process, keep it outside tracked files, restrict permissions, and remove it securely after use. Do not print credentials, raw responses, connection strings, or identity data in logs or artifacts. The CLI expects an unused report path; it never overwrites an existing report.

Run both scenarios separately using `python3 scripts/benchmarks/contact_list.py --input <private-file> --output <new-report-file> --scenario default` and `--scenario search`. The search should use `segment-07`; validate its cardinality before measuring. Requests use separate bearer credentials, 50 closed-loop workers, one request at a time per worker, 60 seconds warmup and 300 seconds measurement, and a 10-second timeout. Each Python worker prepares and reuses its own HTTP opener before timing requests; proxy and redirect handling are disabled. The HTTP transport issues a new connection per request. Capture this topology when comparing results. Returned pages must contain expected tenant-A IDs and valid shapes; search results must match the requested term. Preflight requires successful access for every identity and rejects successful unauthorized or cross-tenant access. Reports contain aggregates, not payloads.

Record **before** running: UTC time and exact candidate SHA; API build and runtime; IdP version; PostgreSQL version, configuration and storage; CPU model and allocation; RAM; OS and container resource limits; network topology and RTT; client host; relevant lockfiles; dataset cardinality; and cache/warmup state. Record API and database CPU/memory utilization and applicable query plans. Associate each scenario's report with this sanitized environment manifest. Keep failed or invalid reports. A valid pass requires all 50 workers, full measurement, no request errors or invalid bodies, and nearest-rank p95 strictly under 400 ms; failed and incomplete measurements are not passes.

## Remaining work

The original measurement omitted network RTT and client CPU. A later run records both and publishes its provisioning scripts below; independent repeatability review remains open. Hosted CI covered the benchmark unit tests, and the recorded candidate measurements below are separate runtime evidence. Do not treat a unit or seed integration test as benchmark evidence.

## Recorded run — 2026-11-28

Candidate `f5a34ed75fedf83a4651742e13e6aed5d0f8fb14` was measured on a
five-vCPU Linux host using the real isolated stack; see the
[sanitized reports, environment, resource summary and query plans](../evidence/wave2-plan9-benchmark/)
and [exit verification](../engineering/16-WAVE-2-EXIT-VERIFICATION.md).
The default scenario passed with p95 105.3 ms and no errors; the separate
search scenario was 1979.5 ms and is recorded as a diagnostic failure. Earlier
failed measurements caused by per-request client opener setup remain preserved.
The private input and provisioner are environment-local, not a complete
published provisioning workflow; recreate and verify them for a new candidate.

## Latest candidate — 2026-11-28

On head `fcccd09ba006b53b4960e3613f0ff1f856da6e42` (same tree as GitHub tested
merge `6a73157c2d8353ca1ce55edb60d2a5986db699b3`), the default workload
returned p95 108.02 ms from 242,305 valid responses, with zero errors. The
separate search diagnostic returned p95 1861.61 ms from 12,129 valid responses,
with zero errors. The [reports and sanitized environment](../evidence/wave2-plan9-benchmark/fcccd09/)
retain both results. Measured network RTT and client CPU saturation were not
recorded, and local provisioning scripts remain environment-local; account for
these gaps before treating this as fully reproducible exit evidence.

## Later benchmark validation — 2026-11-28

The first page must contain exactly 20 distinct contacts and a nonempty cursor;
incomplete HTTP responses are classified in the aggregate report. On candidate
`1e78b7860dd7d5620556ea5c27becda7afde556d`, the default scenario passed with
p95 112.82 ms and 232,626 valid responses; search was 1643.72 ms and remains a
diagnostic failure. Both had zero errors. See the [reports and environment](../evidence/wave2-plan9-benchmark/1e78b78/).
The earlier candidate's harness did not enforce full pages, so its results do
not independently prove that every response contained 20 contacts.

## Reproduce the 1e78b78 default measurement

The [rerun evidence](../evidence/wave2-plan9-benchmark/1e78b78-rerun/)
includes the exact local provisioning, manifest, network and monitoring scripts
used for the rerun, without generated credentials or private input. They target
`.local/plan9-exit-runtime` in a clean checkout of the measured commit. Inspect
them before use. They create an isolated stack and synthetic principals; never
point them at an existing database or identity provider. The commands below
must run as the normal, non-root development user with Docker available.

From a clean checkout of `1e78b7860dd7d5620556ea5c27becda7afde556d`, copy the
five `*.py.txt` evidence files into `.local/plan9-exit-runtime/`, removing only
the final `.txt` suffix. Create the private runtime metadata and cursor key:

```sh
python3 - <<'PY'
import base64, json, os, secrets, socket, uuid
from pathlib import Path
p = Path.cwd() / '.local/plan9-exit-runtime'
p.mkdir(parents=True, exist_ok=True, mode=0o700)
def port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]
a, b = port(), port()
while a == b: b = port()
(p / 'metadata.json').write_text(json.dumps({
    'project': 'crm-bench-' + uuid.uuid4().hex, 'api_port': a, 'idp_port': b}))
(p / 'cursor-key').write_text(base64.b64encode(secrets.token_bytes(32)).decode())
for name in ('metadata.json', 'cursor-key'): os.chmod(p / name, 0o600)
PY
```

Start the stack using `scripts/dev-up.sh` with environment variables
`LOCAL_STACK_PROJECT`, `LOCAL_STACK_STATE_DIR` (absolute path to the runtime's
`state` directory), `LOCAL_API_PORT` and `LOCAL_IDP_PORT` taken from that
metadata. Redirect command output to a private local log; do not print generated
credentials. Run the copied `provision.py`, then `manifest.py` and `network.py`
before the workload. The scripts verify the disposable project label, dataset,
restricted application role and isolated identity provider. The network script
uses an already installed Alpine image for an ICMP probe from the API network
namespace to the database; its reported probe is not application latency.

Start `monitor.py` and `run-measured.py` from the clean checkout. The latter
runs `scripts/benchmarks/contact_list.py --input .local/plan9-exit-runtime/input.json
--output .local/plan9-exit-runtime/default-report.json --scenario default` and
samples the client process CPU. The monitor samples container CPU and memory;
stop it after the workload. Retain only sanitized reports and summaries. Verify
that the project label is the generated `crm-bench-<32 hex>` value, remove only
its extra `-bench-api` container, run `scripts/dev-down.sh` with the same
`LOCAL_STACK_*` variables, and remove only volumes and images bearing that
project identity. Remove its private runtime state and input after collection.

The documented rerun on `1e78b78` recorded p95 110.36 ms from 235,048 valid
responses and zero errors. Its network probes preceded the run; the measured
client CPU covers warmup and measurement. See the rerun evidence above.
