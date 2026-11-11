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
Node 24.22.2, npm 11.19.1, Python 3.12.3, Docker 29.9.3, Compose 5.7.0, and
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
