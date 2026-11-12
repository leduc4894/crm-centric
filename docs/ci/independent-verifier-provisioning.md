# Independent verifier and restore evidence — deployment proposal

Status: **reviewable proposal, not approved or provisioned** (2026-11-12).
Implements the existing [trust requirements](policy-transition-trust.md) and
[Plan 4 spec](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md).
This document authorizes no remote operation. Wave 0/P0/RT-* remain OPEN.

## Decision proposed for review

Use an independently administered verifier service and dedicated disposable
workers. The service owns Git event resolution, immutable source retrieval,
trusted policy configuration, evidence verification and the final check. It never
imports candidate Python modules, executes candidate workflows, runs MSBuild,
loads candidate extensions or consumes candidate artifacts as executable code.
A candidate-owned reusable workflow cannot supply this boundary. A base-controlled
workflow could be a later reviewed alternative, but must independently establish
its own identity, immutable dependencies and isolated execution; changing the
workflow invocation in a PR must not remove the required service check.

The existing `.github/workflows/wave0.yml` remains unchanged, including its
`CI_TRUST_NOT_PROVISIONED` failure. Removing that failure before independent
publication, negative tests and separately authorized enforcement would create a
false passing route. This proposal adds no environment switch or candidate file
that can enable trust.

## Deployment inputs (all explicit, none inferred from candidate data)

The provisioner must supply and review this external configuration before release:

| Configuration field | Required value / rule |
| --- | --- |
| `repository` | `leduc4894/crm-centric`, plus independently resolved immutable repository ID and installation ID |
| `events` | `pull_request` and non-forced `push` to `refs/heads/main`; no manual/merge-group support initially |
| `check_name` | `crm-centric/independent-wave0` bound to the dedicated installed GitHub App identity |
| `verifier_release` | reviewed verifier source full commit SHA plus OCI manifest SHA256; no mutable tag |
| `worker_release` | reviewed supervisor full commit SHA, immutable VM image digest and evaluator OCI manifest digests |
| `policy_inventory` | complete reviewed path inventory, per-file SHA256, version and expiry; candidate additions/deletions require review |
| `repository_baseline` | exact reviewed base commit/tree, merge method and bootstrap ADR; never candidate against itself |
| `sdk` | `10.0.403`, roll-forward disabled; reviewed SDK image `mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e` |
| `platform` | Linux amd64; exact worker image, kernel/runtime identity recorded with each run |
| `restore_profiles` | initially only independently reviewed `surface-net10-v1`, described below; no default profile |
| `trust_anchors` | separately controlled public Ed25519 keys, key IDs, purpose, validity and revocation generation |
| `signer` | non-exportable Ed25519 signing service/key reference; access only for independent controller, never workers |
| `check_publisher` | dedicated App installation credential accessible only by final publisher, with repository contents read and checks write; configuration verification must confirm effective scope |
| `evidence_store` | immutable object storage location, object digest/size limits and service authentication, seven-day normalized evidence retention; no candidate URL fetching |
| `limits` | 30-minute restore, 60-minute semantic job, 10-minute evidence lifetime after completion; retry creates new run/nonce |
| `network` | external supervisor fetches immutable Git objects and reviewed tool/package objects; workers deny external egress, controller/metadata/signing APIs and other runs, while permitting isolated job-local service networking; Surface restore process has no network |

Repository/installation IDs, release SHAs/digests, account/service names, trust
anchors, profile file hashes, bootstrap ADR and evidence-store endpoint are
**unassigned provisioning inputs**, not placeholders that may silently default.
No complete deployment manifest can be activated without them. Action/tool pins
must be copied from a separately reviewed release inventory and verified there;
this file alone does not establish their current operational suitability.

## Ownership and custody

Repository owner approves App installation, check requirement and bootstrap.
A separate trust administrator owns signing keys and verifier release promotion;
a policy reviewer approves exact transitions and restore profiles. Record actual
people/groups and separation of duties in the external deployment record. A
candidate author cannot approve their own transition or change these roles.

Use different keys/purposes for policy approvals and execution attestations. The
controller signs only its own validated envelope after independently obtaining
worker exit status and immutable input/output measurements; it offers no general
"sign these PR bytes" API. Keys never enter worker files, environment, caches or
logs. The public verifier checks algorithm and purpose against its pinned key map,
not an envelope-selected arbitrary algorithm/key URL. Reject duplicate JSON keys,
unknown fields, noncanonical payloads, wrong type, expiry and revoked keys.

Rotation: provision a new public key in a reviewed verifier release, verify both
positive/negative fixtures, then switch the signer. Keep the old key only until
unexpired evidence drains. Revocation immediately invalidates that key and all
pending evidence; rerun with a new nonce. Unavailable signer/revocation state fails
closed with a reason token and correlation ID. No HMAC test key becomes production.

## Execution and evidence flow

1. Controller authenticates the event, resolves repository identity, fetches exact
   Git objects without hooks, replacement objects, lazy fetching or submodules,
   and validates the current `resolve_event` invariants independently. It records
   event delivery ID, run/attempt/nonce, base/head/candidate/merge SHA and tree.
   Re-read current PR/base identity before publishing; advancement cancels evidence.
2. Controller constructs read-only input bundles from those objects and its own
   policy inventory. Candidate jobs' summaries, approvals and success strings are
   untrusted inputs, never authoritative check results. A production independent
   five-job harness must actually run the required checks and validate their
   normalized outputs; candidate-produced artifacts alone cannot turn it green.
3. Run candidate builds/tests, old/new semantic evaluators and restore workers in
   separate disposable VMs with no repository secrets, signing/App credentials,
   host mounts, host Docker socket, shared cache or cross-run writable state.
   Any required Docker daemon is confined to that disposable worker VM; no daemon
   or elevated capabilities in the verifier host. Candidate access to a rootful
   daemon makes the entire worker VM untrusted, including its filesystem and
   in-VM agents. The trusted supervisor runs outside that VM, under a distinct
   host/service identity; its status channel and credentials are never mounted
   into the VM. Bound CPU/memory/disk/time and capture private diagnostics. Allow
   isolated job-local API/database/OIDC service connections needed by actual
   tests; deny external egress, cloud metadata, controller/signing APIs and
   cross-run traffic at a boundary the worker cannot reconfigure.
4. Trusted external supervisor measures input objects, starts pinned execution
   and captures completion through its own worker lifecycle boundary. Treat all
   worker outputs as hostile:
   bounded data-only parsing, no symlink traversal, no deserialization hooks or
   script execution. Supervisor authenticates a completion channel unavailable to
   sandbox code. A compromised worker VM must not manufacture supervisor status.
   External lifecycle completion alone does not prove test assertions ran: the
   independently pinned harness must also validate expected case inventories and
   trustworthy execution evidence. Untrusted in-VM agent messages or candidate
   JSON cannot be promoted to authoritative success. Surface proof runs separately
   with its reviewed profile, no candidate daemon and no candidate code execution.
5. For policy changes use the existing strict transition payload/dual evaluation
   contract. Signing authority validates actual ADR, reviewer and immutable
   consumer-evidence objects, not merely nonempty strings. The production trusted
   adapter and coordinator remain to be implemented; existing candidate CLI has
   no passing transition capability. First main push obtains a distinct signed
   transition bound to `before`, actual resulting tree/commit and merge method.
6. Final publisher verifies signatures, nonce, expiry, complete expected jobs,
   input closure, policy hashes and artifact digests before publishing the check
   on the exact event candidate. Any absent, failed, cancelled, skipped, stale or
   duplicate/conflicting evidence fails. Publish only normalized allowlisted
   fields; private raw reports are deleted, not uploaded.

## Surface empty-lock proof

An isolated execution of arbitrary candidate MSBuild is insufficient: it can
forge assets or execute restore targets. A successful exit and empty assets from
that execution must never alone authorize an empty target. The initial supported
profile is deliberately restricted to the real Surface project and its fully
reviewed restore inputs; arbitrary .NET projects remain unsupported.

The independent `surface-net10-v1` profile contains exact bytes/hashes for
`tools/contracts/surface/dotnet/Surface.csproj`, `Directory.Build.props`,
`Directory.Packages.props`, `global.json`, the eventual checked-in Surface
`packages.lock.json`, and every other effective imported/configuration input.
Review and enumerate parent-directory NuGet/MSBuild configuration, SDK imports,
SDK resolver/extension paths and environment. Host configuration is excluded by
a clean pinned image, empty home and environment allowlist. Repository candidate
content is never copied into that home or installed as a task/SDK. The profile
includes the complete permitted tree structure and absence constraints for new
`Directory.Build.*`, `Directory.Packages.props`, NuGet configuration, response
files and custom imports at every effective ancestor. Reject symlinks, traversal,
external imports, custom tasks, project references or dynamic inputs outside the
reviewed closure. Do not infer this closure from a simple XML search.

A separately reviewed trusted profile builder reconstructs the approved restore
workspace from the profile bytes, compares it byte-for-byte with the candidate's
complete effective input closure, and invokes the pinned SDK itself. Profile
selection is external and bound to exact hashes, never chosen by a candidate
property. Any new imported PackageReference, changed props, SDK, lock, TFM or
configuration mismatches the profile and requires independent re-review. No
candidate MSBuild target or task executes in this proof path. If review cannot
establish that property, no proof is issued. Normal untrusted build/restore runs
still execute separately and must pass; this proof does not replace them.

Proposed initial restore command, run by the trusted profile worker in the
reconstructed tree, for each reviewed configuration `Debug` and `Release`:

```text
dotnet restore tools/contracts/surface/dotnet/Surface.csproj --locked-mode --force-evaluate --no-cache --configfile /trusted/nuget-offline.config -p:Configuration=<configuration> -p:TargetFramework=net10.0 -p:NuGetAudit=false -p:RestorePackagesPath=/scratch/packages -p:MSBuildProjectExtensionsPath=/scratch/obj/<configuration>/
```

`/trusted/nuget-offline.config` has cleared sources; no package feed credentials.
Audit is handled by the mandatory scanner; disabling restore-time network audit
is not vulnerability clearance. These exact flags and configurations require a
real pinned-SDK spike before acceptance; command success is not claimed here.
Require process exit zero, unchanged reviewed lock bytes, exact `net10.0` assets
and dependency group, no package libraries or project references, and no missing
assets. SDK/framework reference packs are separately inventoried as pinned SDK
contents; zero NuGet packages does not mean zero software dependencies. Failure or
unexpected SDK implicit package resolution blocks instead of broadening the profile.

A restore envelope binds schema version, purpose `nuget-empty-restore`, repository
ID, all revision fields/tree, run/attempt/nonce, profile/version/hash, full input
closure `(path, SHA256)` including negative constraints, lock SHA256, SDK and
worker digests, command/property/configuration matrix, zero exit codes, assets
hashes, validated TFM groups, empty package inventory, producer release identity,
start/completion/expiry and signing key ID. Ed25519 signs domain-separated
canonical JSON bytes; approved domain prefix is `crm-centric:restore-empty:v1\n`.
This is a new reviewed evidence contract, not an extension silently accepted by
`verify_transition`'s strict existing schema.

The independent scanner adapter accepts such proof only via authenticated
controller capability, not PR JSON or an environment variable. Keep two disjoint
sets: nonempty targets require actual scanner-observed package coverage; empty
targets require exact valid restore envelopes. An empty-target Trivy omission is
permitted only for that exact authenticated target. Any nonempty observation for
an asserted empty target, extra/missing lock, duplicate evidence or changed input
fails. Never add a fake package or dummy SBOM section. Surface proof does not fix
CorvusProbe NU1008, unknown licenses or tool dependency closure.

## Bootstrap, migration and rollback

1. Approve this proposal and exact external deployment record; independently
   review verifier/profile source and pin releases. Do not generate keys yet as
   part of local remediation. Verify the historical baseline has the semantic API;
   older baselines require an explicit bootstrap ADR, not runtime rewriting.
2. With separate authorization provision keys/service/App, isolated workers and
   storage; run on a non-enforcing test installation using synthetic keys/data.
   Then run shadow checks against actual immutable PR and main events. The current
   repository required job remains deliberately blocked throughout shadow mode.
3. Only after the negative matrix below passes, separately authorize installation
   and check enforcement bound to App identity. Verify candidate removal/renaming
   of local workflows cannot satisfy the independent check. Keep merge queue off
   until event-specific provenance and tests exist. Record actual remote rules.
4. Obtain distinct PR and first-main evidence, including bootstrap/reverse
   transitions as needed. Only a separately reviewed migration commit may replace
   the local `CI_TRUST_NOT_PROVISIONED` step with independently authenticated
   receipt verification; no automatic removal merely because a service exists.
5. On outage or rollback leave checks failing/pending and stop promotion. Revoke
   bad evidence/key, preserve normalized audit records and redeploy a previously
   reviewed immutable release. Policy rollback requires signed reverse transition;
   do not restore a permissive fallback, delete enforcement or rewrite history.

## Implementation plan and required verification

All items below are **planned, not completed**. One coding agent, RED → GREEN →
refactor, small commits, actual verification/handoff at each boundary.

- [ ] In separately controlled verifier source add strict configuration and
  Ed25519 evidence contracts. RED: missing inputs, duplicate/unknown fields,
  wrong-purpose/key/repository/signature, revoked/expired and replay envelopes.
  Implement bounded parser/key adapter; pin crypto dependencies and lockfiles.
- [ ] Add immutable object/event coordinator and isolated supervisor transport.
  RED: wrong merge parents/base/tree, forced push, stale PR, missing object,
  malicious archive path/symlink, forged worker status and callback replay.
  Prove required job-local services can communicate while external egress,
  metadata, controller/signing APIs and other runs are unreachable. Give a
  synthetic hostile worker rootful Docker access and prove it cannot alter the
  external supervisor or forge authenticated completion; in-VM agent success
  strings alone must fail evidence verification.
  Implement no candidate execution in controller; test with sentinel secrets.
- [ ] Add reviewed Surface profile builder and pinned-SDK real worker tests.
  RED: unchanged empty lock plus changed imported PackageReference, new ancestor
  props/config, missing lock, fake assets, SDK/config mismatch and failed restore
  that still writes lock/assets. GREEN requires real valid empty restore under
  both configurations and independently measured closure. Unknown profiles fail.
- [ ] Extend `scripts/ci/scan.py`/authenticated external adapter and `tests/CI/test_scan.py`
  only after that producer exists. RED: missing/stale/duplicate/wrong-SHA proof,
  nonempty target with empty proof, swapped lock, absent TFM, extra target and
  Trivy omission without proof. Preserve real nonempty scanner/security fixtures.
- [ ] Build independent five-job coordinator and existing transition adapter;
  exercise real old/new semantic toolchain in unprivileged workers. RED: forged
  candidate job JSON, missing/skipped/cancelled job, differing policy results,
  forged/expired transition, wrong push/squash/rebase output and revoked approval.
- [ ] Execute hosted shadow/negative enforcement tests under separate remote
  authorization: fork gets no secrets, candidate workflow edits cannot bypass,
  worker cannot access signing/check APIs, outage leaves blocked, advancement
  invalidates evidence, rotation/revocation and reverse-transition recovery.
  Independently review actual App/ruleset/check provenance before migration.

Local documentation validation is not any of these tests. Restore drills for
application data, remote DemoModule and other P0 acceptance remain separate.
