#!/usr/bin/env bash
# Simulates the CI checkout of the contract suite and proves that it stays green.
#
# The repository's only live workflow (.github/workflows/contracts.yml) runs
#
#     python -m unittest discover -s tests/Contract -v
#
# on every push and pull request. This branch adds four test modules directly
# under tests/Contract, and those modules need prerequisites that the workflow
# does not provision: the git-ignored pinned tool artifacts below
# .local/plan3-tools, an installed node_modules, and Docker with the pinned
# .NET image.
#
# This harness copies the checkout into a scratch directory without .local and
# without node_modules, runs the very same command there and asserts that the
# run exits 0, that the Plan 3 tests are reported as skipped with a visible
# reason and that the pre-existing tests still pass. It is the regression
# protection for that interaction: without the prerequisite guard the simulated
# CI run fails, and the harness reports the failure.
#
# The harness needs neither docker, nor the network, nor the pinned .NET image.
#
# Usage: bash tests/Plan3/ci-environment.sh
set -uo pipefail

# The correlation identifier of this harness accompanies every closed error path.
new_correlation() {
  if [ -r /proc/sys/kernel/random/uuid ]; then
    cat /proc/sys/kernel/random/uuid
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c 'import uuid;print(uuid.uuid4())'
  else
    od -An -tx1 -N16 /dev/urandom 2>/dev/null | tr -d ' \n'
  fi
}
correlation=$(new_correlation)

# A closed error path of the harness: a reason token, a safe detail and the
# correlation identifier of this run. No payload and no secret.
closed_error() {
  local reason="$1" detail="$2" code="$3"
  printf '{"reason": "%s", "detail": "%s", "correlationId": "%s"}\n' \
    "$reason" "$detail" "$correlation" >&2
  exit "$code"
}

root=$(cd "$(dirname "$0")/../.." && pwd) ||
  closed_error PLAN3_WORKSPACE_UNAVAILABLE "the checkout could not be located" 1

command -v python3 >/dev/null 2>&1 ||
  closed_error PLAN3_PYTHON_UNAVAILABLE "python3 is not available" 1

# The interpreter that carries the locked contract dependencies. The scratch
# checkout has no virtualenv of its own, so the interpreter of the checkout is
# used; a CI checkout falls back to the system interpreter, which the workflow
# provisions with requirements-contracts.lock.
interpreter="python3"
if [ -x "$root/.venv/bin/python" ]; then
  interpreter="$root/.venv/bin/python"
fi

# The counts of the suite as this branch defines it: the 32 pre-existing tests
# plus the 55 tests of the four Plan 3 modules. A change of the suite must be a
# deliberate change of this harness, never a silent drift.
expected_total=87
expected_skipped=55
expected_passed=32

work=$(mktemp -d) ||
  closed_error PLAN3_TEMP_DIRECTORY_UNAVAILABLE "a temporary directory could not be created" 1
cleanup() { rm -rf "$work"; }
trap cleanup EXIT

log() { printf '%s\n' "$*"; }

fail() {
  log "FAIL  $1"
  shift
  for line in "$@"; do log "      $line"; done
}

# Copies the checkout into the scratch directory without the pinned local tool
# artifacts and without the installed node_modules, which is what a CI checkout
# of this branch looks like today.
scratch="$work/ci-checkout"
mkdir -p "$scratch" ||
  closed_error PLAN3_TEMP_DIRECTORY_UNAVAILABLE "the scratch checkout could not be created" 1
tar -C "$root" -cf - \
  --exclude='./.git' \
  --exclude='./.local' \
  --exclude='./.venv' \
  --exclude='./node_modules' \
  --exclude='*/node_modules' \
  --exclude='*/__pycache__' \
  --exclude='*.pyc' \
  --exclude='./apps/crm-web/dist' \
  --exclude='./apps/crm-web/test-results' \
  --exclude='./apps/crm-web/playwright-report' \
  . | tar -C "$scratch" -xf -

if [ -d "$scratch/.local" ] || [ -d "$scratch/node_modules" ]; then
  closed_error PLAN3_CI_SIMULATION_INVALID "the scratch checkout still carries the excluded artifacts" 1
fi

out="$work/contract.out"
# The node resolver must not find a typescript module outside the scratch
# checkout either, so the simulation is the same on every host.
( cd "$scratch" && NODE_PATH= "$interpreter" -m unittest discover -s tests/Contract -v ) > "$out" 2>&1
status=$?

skipped_count=$(grep -c "skipped '" "$out" || true)
passed_count=$(grep -cE '\.\.\. ok$' "$out" || true)
total_count=$(sed -n 's/^Ran \([0-9][0-9]*\) tests.*$/\1/p' "$out" | head -n 1)
total_count=${total_count:-0}

failures=0

if [ "$status" -ne 0 ]; then
  failures=$((failures + 1))
  fail "ci simulation" "the simulated CI run exited $status, expected 0" \
    "$(tail -n 20 "$out")"
fi

if [ "$total_count" != "$expected_total" ]; then
  failures=$((failures + 1))
  fail "ci simulation" "the simulated CI run reported $total_count tests, expected $expected_total" \
    "a test must never vanish from the report"
fi

if [ "$skipped_count" != "$expected_skipped" ]; then
  failures=$((failures + 1))
  fail "ci simulation" "$skipped_count tests were skipped, expected $expected_skipped" \
    "the Plan 3 tests must be skipped with a visible reason"
fi

if [ "$passed_count" != "$expected_passed" ]; then
  failures=$((failures + 1))
  fail "ci simulation" "$passed_count tests passed, expected $expected_passed" \
    "the pre-existing contract tests must keep passing"
fi

if grep -qE '^(FAIL|ERROR):' "$out"; then
  failures=$((failures + 1))
  fail "ci simulation" "the simulated CI run reported a failure or an error" \
    "$(grep -E '^(FAIL|ERROR):' "$out" | head -n 5)"
fi

# Every skipped test must carry a reason, and the reasons must name the missing
# prerequisites. A test that silently disappeared would break these counts.
for reason in \
  'the pinned oasdiff' \
  'the pinned corvus.json.cli'
do
  if ! grep -q "skipped '[^']*$reason" "$out"; then
    failures=$((failures + 1))
    fail "ci simulation" "no skipped test carries the reason '$reason'" \
      "$(grep -o "skipped '[^']*'" "$out" | sort -u | head -n 5)"
  fi
done

# The node prerequisite is reported either as an unresolvable typescript module
# or as an unusable node toolchain, depending on the host.
if ! grep -qE "skipped '[^']*(typescript is not resolvable from node_modules|the node toolchain is not (usable|on the PATH))" "$out"; then
  failures=$((failures + 1))
  fail "ci simulation" "no skipped test carries the reason of the missing node prerequisite" \
    "$(grep -o "skipped '[^']*'" "$out" | sort -u | head -n 5)"
fi

if grep -qE "skipped ''" "$out"; then
  failures=$((failures + 1))
  fail "ci simulation" "a skipped test carries no reason"
fi

log ""
if [ "$failures" -ne 0 ]; then
  log "ci simulation cases: $failures failed"
  closed_error PLAN3_CI_SIMULATION_FAILED "$failures assertions failed" 1
fi
log "ci simulation: exit 0, $total_count tests, $skipped_count skipped with a reason, $passed_count passed"
