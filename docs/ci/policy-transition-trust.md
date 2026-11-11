# Policy transition trust proposal

Status: local boundary implemented; operational transitions **BLOCKED**, provisioning **OPEN**. Governed by the [trust spec](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md#revision-và-compatibility-trust).

No independent required workflow, signing authority, key ownership, or repository ruleset has been provisioned or verified. A PR approval file is not authority. The current compatibility CLI still rejects all policy changes and has no approval/key CLI or environment option.

An owner must separately approve and provision verifier source pinned outside candidate control (or explicitly base-controlled), signing service/key ownership and rotation/revocation, attestation retrieval, required-check enforcement and immutable evaluator images. Do not give signing credentials to candidate jobs. Installing this module in a candidate workflow does not establish trust.

`verify_transition(attestation, trusted_key, revision, old_hashes, new_hashes)` requires the externally supplied capability's `verify(canonical_payload_bytes, signature)` to return literal True. Canonical JSON uses UTF-8, sorted keys, compact separators, no non-finite numbers. The adapter must implement a reviewed signing algorithm and independently pin the public trust anchor. No production signature algorithm/key backend is provisioned here; tests use only a synthetic HMAC fixture. No verifier is discovered from PR files, environment variables or candidate modules.

The same trusted adapter must supply old/new policy and base/candidate contract snapshots from validated immutable Git objects as tuples of `(relative_path, bytes)`, actual candidate tree and independently reviewed merge method. Attestation binds exact old/new policy hashes, file scope, base/head/candidate SHA, tree, event, merge method, identity, ADR, consumer evidence references, reviewer and expiry. Evidence validity is the signing authority's responsibility, not inferred from nonempty PR strings. Unknown fields and adapter failures close the gate with safe reason token/correlation ID.

First main push needs a push-specific attestation binding `before` as base and actual resulting commit/tree with reviewed merge method. PR approval cannot automatically authorize push. Changed squash/rebase output or advanced base requires new approval. Rollback needs a separately signed reverse transition.

`policy_identity_preflight` separates identity from semantic checks. Default Plan 3 callers supply no transition and fail closed on changed hashes. A trusted coordinator may provide only an exact verified revision/hash transition. Python data objects are an in-process interface, not a sandbox or unforgeable credential.

`evaluate_transition` belongs in a separate **unprivileged** phase. Independently pinned runners evaluate old and new immutable policy snapshots using identical base/candidate contract bytes. Both must report compatible. Either breaking/security/schema/SDK finding or tool failure fails the combined result, retaining both normalized result tokens and attestation identity. Never execute candidate evaluators in the privileged verifier. The verifier imports no candidate evaluator and performs no subprocess, network access, baseline replacement or contract rewriting.

Still OPEN: approved external signing adapter, Git object/snapshot provenance adapter, and orchestration/isolation wiring to execute the real Plan 3 semantic toolchain under each pinned policy image. Local tests exercise the interface; synthetic evaluators do not prove real dual-policy toolchain compatibility. Until this provisioning exists the normal gate has no passing transition route. No remote changes were authorized or performed.
