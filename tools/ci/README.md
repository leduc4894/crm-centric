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
they must be embedded in the run-bound envelope. Parsers retain test identities
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
