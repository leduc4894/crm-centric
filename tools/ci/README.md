# Complete CI evidence

`python3 -m scripts.ci.aggregate --identity identity.json --needs needs.json --reports report-*.json`
returns exit 0 only when all five explicitly required jobs have a platform result
of `success` and exactly one valid report for the expected run and revisions.
`needs.json` accepts GitHub's `toJSON(needs)` structure or a job/result mapping.
An absent/cancelled aggregator is **not** a successful aggregate: configure the
workflow with explicit `needs`, `always()`, and required-check governance.

The identity has `run_id`, `run_attempt` (strings), `candidate_sha`, `base_sha`,
and nullable `merge_sha`. Revision values come from the event proof in
`scripts/ci/revision.py`; run identity comes from the platform context.

Each report is a JSON object with `identity`, `job`, `command_id` (a safe command
label), `status` (`success` or `failure`), `elapsed_seconds`, nonempty `tools`
(version/digest mapping), `artifacts` (safe artifact inventory), and `suites`.
Each suite has `name` and `tests`, where every test contains a stable `id` and
`outcome` (`passed` or `failed`). Failed outcomes require report status `failure`.
No real-suite skip is accepted. Zero discovery, duplicate identities, missing
required identities and a count below the minimum all fail.

Alternatively a suite can have `name`, `format` and embedded `report` instead
of `tests`. Formats are `trx` (XML string), `vitest` (JSON object), or
`playwright` (JSON object). Native reports alone do not prove CI run provenance;
they must be embedded in the run-bound envelope. Playwright reports require native
`stats` with nonnegative integer expected/unexpected/skipped/flaky counts and case
status consistent with the actual result. Counts must reconcile exactly with
extracted cases; skipped and flaky evidence fails. Parsers retain test identities
and outcomes only, discarding native diagnostics. Never upload raw native
reports or input envelopes by default: tool output can contain sensitive data.
The aggregator emits only success, a safe reason token and a correlation ID.

## Coverage policy and trust

`coverage.json` retains the historical minima: Contract 87, Local 20,
non-Compose 70, Compose 14, Vitest 54 and browser 4. Python identities were
inventoried from checked-in classes/methods. Node 24.22.2 Vitest and Playwright
list commands and the pinned .NET 10.0.403 image's `--list-tests` confirmed the
other four inventories. .NET discovery used the existing Release build, so a
fresh build/discovery is still needed in the workflow. The security job's
minimum of one is an inventory placeholder: its scanner coverage and identities
must be established by the security task before the full gate can be accepted.
Independent baseline review remains pending; candidate output never rewrites it.

The workflow **must load the parser and this policy from its immutable trusted
checkout**, while running candidate tests in the unprivileged candidate checkout.
Running this parser from an untrusted candidate checkout does not protect the
policy from a candidate that changes code or lowers coverage. Provisioning that
trust and controlling reviewed transitions remain separate requirements; this
module does not claim to establish them. A bootstrap cannot be self-approved.

The Plan 3 missing-prerequisite simulation is a separate exception: its named
`simulation_skips` must match exactly, with a reviewed reason pattern for each
identity. Additional passing tests are allowed. This exception never applies to
accepted real-suite reports.

## Required-check migration and local rehearsal

`Wave 0` replaces `Contract baseline`; the old `contracts` check is retired by
removing its workflow. Proposed mandatory check is `wave0-required`, whose exact
needs are `backend`, `compose`, `contracts-sdk`, `web-browser`, and `security`.
Repository settings have not been changed. Existing protection requiring the old
`contracts` check needs a separately authorized migration after remote evidence;
removal alone does not prove enforcement. Security is deliberately blocked until
its implementation arrives. Independent trusted-policy provisioning remains OPEN:
this candidate-owned workflow/parser does not establish an independent trust root.

Actions are pinned to full upstream commit IDs. Setup provisions .NET 10.0.403,
Node 24.22.2, npm 11.19.1, Python 3.15.2, Docker 29.9.3, Compose 5.7.0, and
containerd 2.4.2-3 using signed Docker package metadata. Chromium comes from the
locked Playwright 1.65.0 package/browser revision. Existing oasdiff archive/binary
and Corvus package SHA-256 pins are verified during provisioning. Runner OS image
is recorded as mutable, not claimed immutable. No .NET/npm lock changes.

For an already prepared environment, create a revision JSON using
`python -m scripts.ci.revision --event-file <saved-event>` and run
`python -m scripts.ci.jobs <job> --revision <revision.json> --output <ignored-path>`
with `GITHUB_RUN_ID` and `GITHUB_RUN_ATTEMPT`. Rehearsals must use the actual
candidate HEAD; the runner rejects another candidate SHA. The browser runner
always invokes `check-plan3.sh --with-browser`, which owns stack teardown.
Python reports count real discovered tests and reject every skip. Native reports
are normalized to identity/outcome only; raw logs/reports/traces are never uploaded.
Safe summaries retain seven days. Setup errors and absent artifacts fail the gate;
aggregation also runs after artifact download failure and never accepts a skipped
or missing platform job.

The final `wave0-required` step deliberately fails with
`CI_TRUST_NOT_PROVISIONED`. Removing this blocker requires an independently
provisioned immutable verifier/parser/coverage checkout, externally authenticated
bootstrap policy identity, and review/transition verification under the Task2
protocol before any candidate-modifiable policy executes. A candidate file,
repository variable, candidate-provided digest, or this blocker itself does not
establish that trust. Remote evidence and separately authorized repository
required-check configuration must then confirm actual enforcement. The blocker
is visible technical incompleteness, not an accepted passing baseline.

Browser OS provisioning is a setup responsibility: the hosted contracts/browser
jobs run the locked Playwright `install --with-deps chromium` during setup.
An already prepared local job runner uses browser-only installation, then
actually launches and closes locked Chromium before any suite. Missing native
libraries fail that preflight; no dependency-install shim or browser-test skip
is accepted. Local library packages may be extracted into an ignored directory
without root privileges and exposed through `LD_LIBRARY_PATH`; record versions
and the successful actual launch separately from hosted setup evidence.

Job commands run in separate process sessions. Timeout or controlled cancellation
terminates that command's process group (TERM, grace, KILL), then reaps the parent.
Each job invocation supplies a fresh `CI_RESOURCE_OWNER` UUID to the Compose
manifest. Its exit handler selects containers (including stopped containers),
networks and volumes by this label, re-inspects exact ownership, then removes
only matching resources. Non-CI `local` labels are ineligible. This covers the
Compose manifest, not arbitrary resources created without that label; Testcontainers
keeps its own Ryuk lifecycle. Abrupt SIGKILL cannot run finalizers, so hosted runner
isolation and remote cancellation verification remain necessary.

## Task 5 scanner adapters (security job remains blocked)

`scripts/ci/scan.py` supplies `run_scanner(ScannerConfig, ScanInput, RunIdentity)`
and `run_repository(scanners, root, run)`. `run_safe.py` captures subprocess output
in mode-0700 temporary directories / mode-0600 files, clears inherited credentials
and scanner configuration, kills command-owned process groups on exit, and destroys
raw reports after parsing. Published summaries contain fixed status/reason fields,
run identity, opaque SHA-256 finding IDs/kinds/verdicts and counts. Do not upload
raw JSON/SARIF/logs, paths, matches, messages, traces or the ignored rehearsal files.
Summary retention remains seven days. A parser/process failure produces
`CI_SCAN_INCOMPLETE` and a correlation ID; it never reproduces scanner diagnostics.
This does not defend against candidate code modifying its workflow or verifier.

Tool versions/archive and executable checksums are recorded in `toolchain.json`.
Semgrep CE 1.185.0 uses the local `semgrep.yml` with C# SQL-command concatenation
and TypeScript dynamic-eval rules. This is limited intrafile syntactic coverage,
not a comprehensive rule pack or cross-file analysis. All committed `.cs`, `.ts`
and `.tsx`, including generated SDKs and test source, must appear in parsed-file
inventory. A private source-only staging tree neutralizes default and candidate
ignore files; `nosemgrep` comments do not suppress findings. Missing files or parse
errors fail. No registry auto-config, paid analyzer, or online provider credential
verification is used. Semgrep's wheel is LGPL-2.1-or-later; Gitleaks is MIT and
Trivy Apache-2.0. The scanner's own license is not a legal approval for project
redistribution.

Semgrep runs in a **separate supported Python 3.12 environment**: its pinned wheel
supports Python 3.10–3.14, whereas existing CI uses 3.15.2. The full resolved set
of 67 wheel versions/hashes is in `semgrep-requirements.lock`; installation must
use `pip install --require-hashes --only-binary=:all: -r` with that file in the
dedicated environment. This lock is Linux x86-64/glibc/Python-3.12 specific.
Hosted runtime provisioning and immutable runtime/launcher attestation remain OPEN.
The local real-suite launcher hash is measured at runtime because its shebang
contains the installation path; it is not an independent authenticity claim.
Gitleaks/Trivy archives were downloaded from the official releases, verified
against upstream SHA-256 asset digests, then extracted without executing installers.
No scanner or setup command has been wired to the hosted security placeholder yet.

Gitleaks scans an exact committed candidate archive and every commit reachable
in trusted `base..candidate`, including separate merge-parent diffs. Base must be
an ancestor, HEAD must match, a shallow checkout is rejected, and all reachable
objects must be readable. Archives reject symlinks, submodules, export-ignore loss,
unsafe members and oversize members. Explicit trusted config uses embedded default
rules, disables inline allow comments, and ignores candidate `.gitleaksignore`.
Every finding blocks, including unverified/offline/revoked. Test fixtures create
synthetic values in private temporary repositories; no real credentials are used.

Trivy 0.76.2 scans complete npm/NuGet/Python locks with npm development dependencies
included. Nonstandard pip lock names are copied to Trivy's supported
`requirements.txt` convention without resolving/installing candidate dependencies.
Expected lock targets and package name/version sets must exactly match scanner
inventory, including transitives. A real DB update command must succeed and the
DB's upstream `UpdatedAt` must be present, timezone-aware, not future-dated and
at most 24 hours old. `DownloadedAt` is never freshness evidence. Unknown severity
fails incomplete; HIGH/CRITICAL block. Every package needs an explicitly allowed
license; restricted, absent, or unrecognized license expressions require review.
The explicit allowed/denied/unknown policy is in `security-policy.json`. This policy
is conservative, not a legal interpretation of compound SPDX expressions.

`dependency_inputs` marks `dotnet-tools.json` and external `toolchain.json` as
`CI_TOOL_DEPENDENCY_COVERAGE`; their executable/transitive dependencies and licenses
are not proven by application lock scans. `run_repository` propagates every such
gap as incomplete. Unknown `.lock` formats also block. These gaps, source/image
SBOM production, container/image identity binding (Task 6), independent trust,
and hosted scanner provisioning remain OPEN. An application scan alone cannot
close this security job or the Wave 0 acceptance requirement.

`exceptions.json` is empty. The exception-policy adapter requires the exact finding
ID, owner, reason, future expiry, and an independently authenticated approval digest
bound to every field; changing any field invalidates that approval. Candidate JSON
or a matching digest alone is not authentication. No exception is applied by the
current runner, and no independent approvals have been provisioned.

Focused wrapper verification: `python -m unittest discover -s tests/CI -p test_scan.py -v`.
Actual scanner positives: `CI_SCANNER_ROOT=<private-directory> python -m unittest
discover -s tests/Security -v`; the directory contains `bin/gitleaks`, `bin/trivy`,
`semgrep/bin/semgrep` and `trivy-cache`. Missing prerequisites fail; no skips.
Official sources checked: [Semgrep language support](https://semgrep.dev/docs/supported-languages),
[Semgrep release](https://github.com/semgrep/semgrep/releases/tag/v1.185.0),
[Gitleaks usage and config](https://github.com/gitleaks/gitleaks),
[Trivy language coverage](https://trivy.dev/latest/docs/coverage/language/),
[Trivy DB](https://github.com/aquasecurity/trivy-db),
[Gitleaks release](https://github.com/gitleaks/gitleaks/releases/tag/v8.30.1),
[Trivy release](https://github.com/aquasecurity/trivy/releases/tag/v0.76.2).

Archive path inventory alone is insufficient: each extracted regular file also
must match its committed Git blob object ID. In particular, `.gitattributes`
`export-subst` changes are rejected as incomplete before any scanner sees changed
bytes. Dependency manifests are discovered separately from supported locks. A
pnpm YAML lock, an unsupported ecosystem manifest, or an independent package.json
without a matching lock contributes a blocking coverage gap even alongside a
fully scanned npm/NuGet/Python lock. npm workspace manifests must match the ancestor
lock's exact package entry and dependency declarations. Unsupported projects and
unpinned Python inputs require coverage; unrelated application locks never imply it.

## Image evidence (Task 6)

`ImageIdentity(job, runId, imageId, platform, manifestDigest)` records the Docker
config ID read from a started container's `Image` field and verifies it against
`docker image inspect <config-ID>`. Local IDs never become registry digests;
`manifestDigest` is null because RepoDigests can identify a multiarch index and
no platform descriptor chain is verified here. Raw inspect/history are not artifacts.

Compose and browser job runners set a fresh private evidence directory. The local
stack readiness path observes the started API, completed migrator, PostgreSQL and
Keycloak containers before returning to service tests, scans those exact immutable
IDs using Trivy's Docker-only image source, and caches only validated evidence for
that job/run/image. Both the job and normalized-summary parser require all four
roles, matching tested/scanned identities, fresh databases, nonempty inventories,
and a recomputed SBOM hash. Missing scanner, missing roles, unknown severities,
findings or mismatches close the gate. Workflow scanner path is explicitly
`.local/ci-scanners`; `setup.sh` now provisions it with the reviewed release/archive/executable
hashes and a bounded HTTPS download. Actual hosted execution remains unverified;
network/version/hash failures close setup. A local rehearsal may instead supply `CI_SCANNER_ROOT=.local/task5`.

Trivy verifies executable checksum/version, successfully updates both vulnerability
and Java DBs, and checks each upstream `UpdatedAt` against the existing 24h maximum
before and after scanning. `DownloadedAt` is never a freshness substitute. Java
DB publishing cadence can exceed this maximum; that is a blocker to review, not an
automatically granted age exemption. Only normalized findings and a reduced
CycloneDX 1.6 inventory are written: package name/version, tool version, and image
identity properties. Unversioned application grouping entries are omitted, but
versioned package inventory must remain nonempty. Descriptions, arbitrary properties,
paths, URLs, matches and raw scanner metadata are not copied. Runtime secret-file
values are compared privately against the normalized artifact before writing it.

No OCI archives are shared or uploaded and no registry image is published. Existing
artifact upload remains restricted to normalized job JSON (including validated
image evidence on successful Compose/browser jobs), with 7-day retention.

**Limits:** This is candidate-owned verification, not independent policy authority.
Backend `ContainerImages.Docker` run commands retain CID files long enough to
observe actual started config IDs, then honor requested auto-removal. Both PostgreSQL
Testcontainers fixtures call the same evidence helper after start; direct Compose
run commands also route through it. CI disables Ryuk and labels test containers
for the existing owner-scoped cleanup. Evidence scans serialize within each job
to avoid concurrent DB/cache updates. No full current-SHA service or remote run
has passed. Source inventories and provisioning details follow below.
Historical locally available runtime-image scans found real blocking vulnerabilities;
PostgreSQL also has unknown-severity findings and remains incomplete. Security tool
coverage, source/dependency findings, independent trust and acceptance drills remain
open. Never describe wrapper tests or nonempty SBOMs as passing image security.

### Task 6 review round 1

`python -m scripts.ci.provision_scanners --output .local/ci-scanners` is called
by hosted setup. It accepts only the locked Trivy tarball hash and executable
hash, extracts only a regular-file `trivy` member, installs atomically and verifies
exact version. HTTPS redirects and download size/time are bounded; raw curl
output remains private. `--archive <path>` supports an already-downloaded reviewed
release with the same mandatory hashes (not a trust/checksum bypass). Local real
archive installation passed; local fresh network retrieval failed closed, so
hosted download availability is still unproven.

The security job runs `scripts.ci.sbom` against the exact committed candidate
archive, publishes only `.local/ci-sbom/source.json` as a separate `source-sbom`
artifact, and exits nonzero on discovered coverage gaps. It has ecosystem-aware
package URLs, exact run/revision properties, nonempty package inventory and a
canonical hash. This is package inventory, not vulnerability/license approval.
Unsupported tool/unlocked inputs remain explicit incomplete coverage. Security
scanner completeness and independent workflow trust remain blocked; no local
candidate-side implementation can grant the independent authority.

### Task 6 review round 2: artifact secret boundary

Source package names/versions are untrusted strings even when they match a package
syntax. Before a source SBOM is uploadable, the publisher now runs pinned Gitleaks
on original supported lock bytes staged under neutral private filenames, then on
the serialized normalized record. Neutral names are necessary because Gitleaks
normally omits lockfile paths; original bytes are necessary because package-name
canonicalization and purl escaping can hide a token from the later artifact scan.
Known fixture sentinels are also checked recursively. Any finding, missing tool,
invalid output or failed input removes/withholds the exact artifact path, including
stale output from a previous attempt. Coverage-incomplete inventories receive the
same secret checks; incomplete is never an exemption for publication.

Hosted setup provisions the pinned Gitleaks release alongside Trivy. The source
step explicitly supplies that scanner root. A successful secret check is pattern
coverage plus any supplied known sentinels, not proof of general secret absence
or independent policy authority. Existing security/trust blockers remain.

Direct Docker evidence now freezes known fixture values before launching the
container: bind-mounted /run/secrets file contents, sensitive environment values,
and passwords parsed from connection strings. These values travel only over the
private helper stdin and reach the existing recursive sentinel guard; they do
not enter argv, logs or reports. Testcontainers already supplied their generated
passwords. Secretless commands keep an empty known-value set.
