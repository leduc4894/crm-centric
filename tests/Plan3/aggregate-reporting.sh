#!/usr/bin/env bash
# Regression coverage for the reporting, error-envelope and teardown behaviour of
# the Plan 3 aggregate gate.
#
# The cases below are the independent review findings of Task 7:
#
#   I-1  every step that did not run because an earlier step failed must appear
#        in the summary as "not run: an earlier step failed", so the summary
#        always accounts for all ten steps;
#   I-2  every closed error path of the aggregate itself must carry a reason
#        token and a correlation identifier;
#   I-3  a failed stack teardown must fail the run, and an interrupted run must
#        not orphan the local stack;
#   M-4  the clean export must refuse a target directory that is not empty;
#   N-1  a stack that fails to start must be torn down, never orphaned;
#   N-2  every closed error path carries a reason token and a correlation id;
#   N-3  a file that is staged in the index but absent from HEAD is covered by
#        the source digest;
#   R-1  the signal paths of the aggregate (INT, TERM) and the failing teardown
#        of the interrupted cleanup carry a reason token and a correlation id
#        instead of closing silently;
#   N-R3-1  a teardown that fails in a run that was never interrupted is
#        reported exactly once, without wording that calls the run interrupted.
#   C-1  a CI-like checkout without the pinned local tooling and without an
#        installed node_modules keeps the contract suite green: the Plan 3
#        tests are skipped with a visible reason, the pre-existing tests pass;
#   C-2  a comparator that cannot load the typescript module closes with a
#        reason token and a correlation identifier instead of a stack trace.
#   S-1  a scratch directory that a script created itself is unlinked instead
#        of being moved into a recoverable trash by a recoverable-delete shim
#        on the PATH.
#
# The sandbox is a copy of the checkout with a read-only Git object context
# (objects/info/alternates) so that a base revision resolves without a fetch.
# The pinned local tool artifacts are copied in; they are not committed.
#
# Usage: bash tests/Plan3/aggregate-reporting.sh [--base-ref <ref>]
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
# The scratch of this harness is unlinked through an absolute rm binary, never
# through a PATH-resolved one: a recoverable-delete shim on the PATH would copy
# a multi-gigabyte scratch tree into the trash of the user instead of freeing
# it.
. "$root/scripts/lib/scratch-cleanup.sh"
base_ref="408ded6"
only=""
while [ $# -gt 0 ]; do
  case "$1" in
    --base-ref)
      [ $# -ge 2 ] || closed_error PLAN3_ARGUMENT_INVALID "--base-ref requires a value" 2
      base_ref="$2"; shift 2 ;;
    --only)
      [ $# -ge 2 ] || closed_error PLAN3_ARGUMENT_INVALID "--only requires a value" 2
      only="$2"; shift 2 ;;
    *) closed_error PLAN3_ARGUMENT_INVALID "unknown argument: $1" 2 ;;
  esac
done

work=$(mktemp -d) ||
  closed_error PLAN3_TEMP_DIRECTORY_UNAVAILABLE "a temporary directory could not be created" 1
trap 'scratch_remove "$work"' EXIT

failures=0
cases=0

gate_status=0
gate_output=""

ALL_STEPS="locked-install generated-check sdk-consumers compatibility frontend-typecheck frontend-lint frontend-test frontend-build frontend-leak-scan browser-e2e"

log() { printf '%s\n' "$*"; }

fail() {
  failures=$((failures + 1))
  log "FAIL  $1"
  shift
  for line in "$@"; do log "      $line"; done
}

pass() { log "ok    $1"; }

# The directory of the pinned oasdiff executable, taken from the policy that
# the tooling verifies itself. The version is never hardcoded here, so a bump
# of the pinned tool does not silently break the copy.
pinned_oasdiff_directory() {
  local relative candidate
  relative=$(python3 - "$root/tools/contracts/oasdiff.json" <<'PY' 2>/dev/null
import json
import os
import sys

try:
    policy = json.load(open(sys.argv[1]))
except Exception:
    sys.exit(1)
print(os.path.dirname(policy.get('binaryPath', '')))
PY
)
  case "$relative" in
    */oasdiff-*) printf '%s\n' "${relative##*/}" ;;
    *)
      for candidate in "$root"/.local/plan3-tools/oasdiff-*; do
        [ -d "$candidate" ] && { printf '%s\n' "${candidate##*/}"; return 0; }
      done
      printf 'oasdiff-missing\n'
      ;;
  esac
}

# Creates a sandbox copy of the checkout with a read-only Git object context.
make_sandbox() {
  local dir="$1"
  scratch_remove "$dir"
  mkdir -p "$dir"
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
    --exclude='./apps/crm-web/.leak-scan-build' \
    . | tar -C "$dir" -xf -
  mkdir -p "$dir/.git/objects/info" "$dir/.git/refs/heads"
  printf '[core]\n\trepositoryformatversion = 0\n\tbare = false\n' > "$dir/.git/config"
  git -C "$root" rev-parse HEAD > "$dir/.git/HEAD"
  printf '%s/objects\n' "$(git -C "$root" rev-parse --absolute-git-dir)" > "$dir/.git/objects/info/alternates"
  mkdir -p "$dir/.local/plan3-tools"
  cp "$root/.local/plan3-tools/corvus.json.cli.5.8.8.nupkg" "$dir/.local/plan3-tools/"
  cp -R "$root/.local/plan3-tools/$(pinned_oasdiff_directory)" "$dir/.local/plan3-tools/"
}

# Replaces the driven tools that are not under test with stubs, so that the
# cases stay fast and deterministic. The stack lifecycle is stubbed as well.
stub_driven_tools() {
  local dir="$1"
  printf '#!/usr/bin/env bash\nexit 0\n' > "$dir/tests/ClientConsumers/run.sh"
  chmod +x "$dir/tests/ClientConsumers/run.sh"
  cat > "$dir/scripts/contracts/compatibility.py" <<'PY'
import json
print(json.dumps({"reason": "CONTRACT_COMPATIBLE", "correlationId": "stub-correlation"}))
PY
  python3 - "$dir/apps/crm-web/package.json" <<'PY'
import json
import sys

path = sys.argv[1]
data = json.load(open(path))
for name in ('typecheck', 'lint', 'test', 'build', 'scan:leaks', 'test:e2e'):
    data['scripts'][name] = 'node -e "process.exit(0)"'
with open(path, 'w') as handle:
    json.dump(data, handle, indent=2)
    handle.write('\n')
PY
  printf '#!/usr/bin/env bash\nexit 0\n' > "$dir/scripts/dev-up.sh"
  printf '#!/usr/bin/env bash\nexit 0\n' > "$dir/scripts/dev-smoke.sh"
  chmod +x "$dir/scripts/dev-up.sh" "$dir/scripts/dev-smoke.sh"
}

# Runs the aggregate gate and captures its exit code and output.
run_gate() {
  local dir="$1"
  shift
  local out="$work/gate.out"
  ( cd "$dir" && bash "$dir/scripts/check-plan3.sh" "$@" ) > "$out" 2>&1
  gate_status=$?
  gate_output=$(cat "$out")
}

has_correlation() {
  printf '%s' "$1" | grep -Eq '"correlationId"[[:space:]]*:[[:space:]]*"[^"]{8,}"'
}

# Asserts that the output carries a JSON error envelope with the given reason
# token and a correlation identifier on the same line. A correlation identifier
# printed on its own, without an envelope, does not satisfy this.
has_envelope() {
  printf '%s' "$1" |
    grep -E "\"reason\"[[:space:]]*:[[:space:]]*\"$2\"" |
    grep -Eq "\"correlationId\"[[:space:]]*:[[:space:]]*\"[^\"]{8,}\""
}

# The correlation identifier of the run, as printed in its header.
run_correlation() {
  printf '%s' "$1" | sed -n 's/^plan3 aggregate: correlation //p' | head -n 1
}

# Asserts that the envelope of the given reason token carries the correlation
# identifier of this very run, and not an identifier of another run.
envelope_carries_the_run_correlation() {
  local output="$1" reason="$2" correlation="$3"
  [ -n "$correlation" ] || return 1
  printf '%s' "$output" |
    grep -E "\"reason\"[[:space:]]*:[[:space:]]*\"$reason\"" |
    grep -Eq "\"correlationId\"[[:space:]]*:[[:space:]]*\"$correlation\""
}

step_is_not_run() {
  printf '%s' "$gate_output" |
    grep -E "^$1[[:space:]]+not run[[:space:]]+-" |
    grep -q 'not run: an earlier step failed'
}

# A process that still runs. A zombie only waits to be reaped and is gone for
# the purpose of this harness.
process_is_running() {
  local pid="$1" state
  [ -n "$pid" ] || return 1
  state=$(ps -o stat= -p "$pid" 2>/dev/null | tr -d ' ')
  [ -n "$state" ] || return 1
  case "$state" in
    Z*) return 1 ;;
  esac
  return 0
}

# Waits until the process is gone, at most ten seconds.
wait_until_gone() {
  local pid="$1" waited=0
  while process_is_running "$pid" && [ "$waited" -lt 100 ]; do
    sleep 0.1
    waited=$((waited + 1))
  done
  ! process_is_running "$pid"
}

case_argument_error() {
  log "case  argument error carries a reason token and a correlation id"
  cases=$((cases + 1))
  run_gate "$root"
  if [ "$gate_status" -ne 2 ]; then
    fail "argument error" "expected exit 2, got $gate_status"
    return
  fi
  if ! printf '%s' "$gate_output" | grep -q 'PLAN3_ARGUMENT_INVALID'; then
    fail "argument error" "the reason token PLAN3_ARGUMENT_INVALID is missing"
    return
  fi
  if ! has_correlation "$gate_output"; then
    fail "argument error" "no correlation id in: $(printf '%s' "$gate_output" | tail -n 3)"
    return
  fi
  pass "argument error (exit 2, PLAN3_ARGUMENT_INVALID, correlation id present)"
}

case_toolchain_mismatch() {
  log "case  toolchain mismatch carries a reason token and a correlation id"
  cases=$((cases + 1))
  local fake="$work/fakebin"
  mkdir -p "$fake"
  printf '#!/bin/sh\necho v26.13.0\n' > "$fake/node"
  chmod +x "$fake/node"
  local out="$work/gate.out"
  ( cd "$root" && PATH="$fake:$PATH" bash "$root/scripts/check-plan3.sh" --base-ref "$base_ref" ) > "$out" 2>&1
  gate_status=$?
  gate_output=$(cat "$out")
  if [ "$gate_status" -eq 0 ]; then
    fail "toolchain mismatch" "the gate exited 0 with an unsupported Node"
    return
  fi
  if ! printf '%s' "$gate_output" | grep -q 'PLAN3_TOOLCHAIN_MISMATCH'; then
    fail "toolchain mismatch" "the reason token PLAN3_TOOLCHAIN_MISMATCH is missing"
    return
  fi
  if ! has_correlation "$gate_output"; then
    fail "toolchain mismatch" "no correlation id in: $(printf '%s' "$gate_output" | tail -n 3)"
    return
  fi
  pass "toolchain mismatch (exit $gate_status, PLAN3_TOOLCHAIN_MISMATCH, correlation id present)"
}

case_steps_after_failure_are_recorded() {
  log "case  steps after an early failure are recorded as not run"
  cases=$((cases + 1))
  local dir="$work/early-failure"
  make_sandbox "$dir"
  printf '\n// tampered by the reporting regression\n' >> "$dir/sdks/typescript/crm/index.ts"
  run_gate "$dir" --base-ref "$base_ref"
  if [ "$gate_status" -eq 0 ]; then
    fail "early failure" "the gate exited 0"
    return
  fi
  local step
  for step in $ALL_STEPS; do
    if ! printf '%s' "$gate_output" | grep -Eq "^$step[[:space:]]"; then
      fail "early failure" "the step $step is missing from the summary"
      return
    fi
  done
  for step in sdk-consumers compatibility frontend-typecheck frontend-lint frontend-test frontend-build frontend-leak-scan browser-e2e; do
    if ! step_is_not_run "$step"; then
      fail "early failure" "the step $step is not recorded as not run after the earlier failure"
      return
    fi
  done
  pass "early failure (all ten steps recorded, eight of them not run)"
}

case_teardown_failure() {
  log "case  a failing stack teardown fails the run"
  cases=$((cases + 1))
  local dir="$work/teardown"
  make_sandbox "$dir"
  stub_driven_tools "$dir"
  printf '#!/usr/bin/env bash\nexit 1\n' > "$dir/scripts/dev-down.sh"
  chmod +x "$dir/scripts/dev-down.sh"
  run_gate "$dir" --base-ref "$base_ref" --with-browser
  if [ "$gate_status" -eq 0 ]; then
    fail "teardown failure" "the gate exited 0 although the teardown failed"
    return
  fi
  if printf '%s' "$gate_output" | grep -q 'plan3 aggregate: PASS'; then
    fail "teardown failure" "the gate reported PASS although the teardown failed"
    return
  fi
  if ! printf '%s' "$gate_output" | grep -q 'PLAN3_STACK_TEARDOWN_FAILED'; then
    fail "teardown failure" "the reason token PLAN3_STACK_TEARDOWN_FAILED is missing"
    return
  fi
  pass "teardown failure (exit $gate_status, PLAN3_STACK_TEARDOWN_FAILED, no PASS)"
}

# A teardown that fails in a run that was never interrupted must be reported
# exactly once, with wording that does not describe the run as interrupted.
# The run stays fail-closed: a non-zero exit and no PASS.
case_ordinary_teardown_failure_is_reported_once() {
  log "case  a failing teardown of an ordinary run is reported once"
  cases=$((cases + 1))
  local dir="$work/ordinary-teardown-failure"
  make_sandbox "$dir"
  stub_driven_tools "$dir"
  printf '#!/usr/bin/env bash\nexit 1\n' > "$dir/scripts/dev-down.sh"
  chmod +x "$dir/scripts/dev-down.sh"
  run_gate "$dir" --base-ref "$base_ref" --with-browser
  if [ "$gate_status" -eq 0 ]; then
    fail "ordinary teardown failure" "the gate exited 0 although the teardown failed"
    return
  fi
  if printf '%s' "$gate_output" | grep -q 'plan3 aggregate: PASS'; then
    fail "ordinary teardown failure" "the gate reported PASS although the teardown failed"
    return
  fi
  local envelopes
  envelopes=$(printf '%s\n' "$gate_output" | grep -c '"reason": "PLAN3_STACK_TEARDOWN_FAILED"')
  if [ "$envelopes" -ne 1 ]; then
    fail "ordinary teardown failure" \
      "expected exactly one envelope PLAN3_STACK_TEARDOWN_FAILED, got $envelopes"
    return
  fi
  if ! envelope_carries_the_run_correlation "$gate_output" PLAN3_STACK_TEARDOWN_FAILED \
    "$(run_correlation "$gate_output")"; then
    fail "ordinary teardown failure" \
      "the envelope PLAN3_STACK_TEARDOWN_FAILED does not carry the correlation id of the run"
    return
  fi
  if printf '%s' "$gate_output" | grep -qi 'interrupted run'; then
    fail "ordinary teardown failure" \
      "the output describes a run that was never interrupted as the interrupted run"
    return
  fi
  pass "ordinary teardown failure (exit $gate_status, one PLAN3_STACK_TEARDOWN_FAILED envelope, no misleading wording, no PASS)"
}

case_interrupt_tears_the_stack_down() {
  log "case  an interrupted run tears the stack down"
  cases=$((cases + 1))
  local dir="$work/interrupt"
  make_sandbox "$dir"
  stub_driven_tools "$dir"
  printf '#!/usr/bin/env bash\nprintf "%%s" "$$" > "%s/.smoke-pid"\nsleep 30\n' "$dir" > "$dir/scripts/dev-smoke.sh"
  printf '#!/usr/bin/env bash\ntouch "%s/.teardown-marker"\nexit 0\n' "$dir" > "$dir/scripts/dev-down.sh"
  chmod +x "$dir/scripts/dev-smoke.sh" "$dir/scripts/dev-down.sh"
  local out="$work/interrupt.out"
  # A background job inherits SIGINT as ignored, so the disposition is reset
  # before the gate is executed; otherwise the interrupt would never reach it.
  setsid python3 -c 'import os, signal, sys; signal.signal(signal.SIGINT, signal.SIG_DFL); os.execvp(sys.argv[1], sys.argv[1:])' \
    bash "$dir/scripts/check-plan3.sh" --base-ref "$base_ref" --with-browser > "$out" 2>&1 &
  local pid=$!
  local waited=0
  while [ ! -f "$dir/.smoke-pid" ] && [ "$waited" -lt 300 ]; do
    sleep 0.2
    waited=$((waited + 1))
  done
  if [ ! -f "$dir/.smoke-pid" ]; then
    fail "interrupt" "the run never reached the stack smoke step"
    kill -TERM "$pid" 2>/dev/null
    wait "$pid" 2>/dev/null
    return
  fi
  local smoke_pid pgid
  smoke_pid=$(cat "$dir/.smoke-pid")
  pgid=$(ps -o pgid= -p "$smoke_pid" 2>/dev/null | tr -d ' ')
  if [ -n "$pgid" ]; then
    kill -INT -- "-$pgid" 2>/dev/null
  else
    kill -INT "$smoke_pid" 2>/dev/null
  fi
  wait "$pid" 2>/dev/null
  local status=$?
  if [ "$status" -eq 0 ]; then
    fail "interrupt" "the run was not interrupted (exit 0)"
    return
  fi
  if [ ! -f "$dir/.teardown-marker" ]; then
    fail "interrupt" "the stack teardown did not run after the interrupt (exit $status)"
    return
  fi
  pass "interrupt (exit $status, the stack teardown ran)"
}

# Starts the aggregate gate in the sandbox, interrupts it with the given
# signal while the stack smoke step is running and waits for it to finish.
# The exit code and the output of the run are left in gate_status and
# gate_output.
run_interrupted_gate() {
  local dir="$1" signal="$2"
  local out="$work/interrupt-$signal.out"
  # A background job inherits SIGINT as ignored, so the disposition is reset
  # before the gate is executed; otherwise the interrupt would never reach it.
  setsid python3 -c 'import os, signal, sys; signal.signal(signal.SIGINT, signal.SIG_DFL); os.execvp(sys.argv[1], sys.argv[1:])' \
    bash "$dir/scripts/check-plan3.sh" --base-ref "$base_ref" --with-browser > "$out" 2>&1 &
  local pid=$!
  local waited=0
  while [ ! -f "$dir/.smoke-pid" ] && [ "$waited" -lt 300 ]; do
    sleep 0.2
    waited=$((waited + 1))
  done
  if [ ! -f "$dir/.smoke-pid" ]; then
    kill -TERM "$pid" 2>/dev/null
    wait "$pid" 2>/dev/null
    return 1
  fi
  local smoke_pid pgid
  smoke_pid=$(cat "$dir/.smoke-pid")
  pgid=$(ps -o pgid= -p "$smoke_pid" 2>/dev/null | tr -d ' ')
  if [ -n "$pgid" ]; then
    kill "-$signal" -- "-$pgid" 2>/dev/null
  else
    kill "-$signal" "$smoke_pid" 2>/dev/null
  fi
  wait "$pid" 2>/dev/null
  gate_status=$?
  gate_output=$(cat "$out")
  return 0
}

# Prepares a sandbox whose stack smoke step blocks, so that the run can be
# interrupted while the local stack is up.
make_interruptible_sandbox() {
  local dir="$1"
  make_sandbox "$dir"
  stub_driven_tools "$dir"
  printf '#!/usr/bin/env bash\nprintf "%%s" "$$" > "%s/.smoke-pid"\nsleep 30\n' "$dir" > "$dir/scripts/dev-smoke.sh"
  printf '#!/usr/bin/env bash\ntouch "%s/.teardown-marker"\nexit 0\n' "$dir" > "$dir/scripts/dev-down.sh"
  chmod +x "$dir/scripts/dev-smoke.sh" "$dir/scripts/dev-down.sh"
}

# The INT path is a closed error path: it must carry a reason token and the
# correlation identifier of the run, and it must still tear the stack down.
case_interrupt_emits_a_reason_envelope() {
  log "case  an interrupted run emits a reason token and a correlation id"
  cases=$((cases + 1))
  local dir="$work/interrupt-envelope"
  make_interruptible_sandbox "$dir"
  if ! run_interrupted_gate "$dir" INT; then
    fail "interrupt envelope" "the run never reached the stack smoke step"
    return
  fi
  if [ "$gate_status" -ne 130 ]; then
    fail "interrupt envelope" "expected exit 130, got $gate_status"
    return
  fi
  if ! has_envelope "$gate_output" PLAN3_INTERRUPTED; then
    fail "interrupt envelope" \
      "the envelope PLAN3_INTERRUPTED with a correlation id is missing: $(printf '%s' "$gate_output" | tail -n 3)"
    return
  fi
  if ! envelope_carries_the_run_correlation "$gate_output" PLAN3_INTERRUPTED \
    "$(run_correlation "$gate_output")"; then
    fail "interrupt envelope" \
      "the envelope PLAN3_INTERRUPTED does not carry the correlation id of the run"
    return
  fi
  if [ ! -f "$dir/.teardown-marker" ]; then
    fail "interrupt envelope" "the stack teardown did not run after the interrupt (exit $gate_status)"
    return
  fi
  pass "interrupt envelope (exit 130, PLAN3_INTERRUPTED, correlation id present, the stack teardown ran)"
}

# The TERM path is the symmetric closed error path of the INT path.
case_terminate_emits_a_reason_envelope() {
  log "case  a terminated run emits a reason token and a correlation id"
  cases=$((cases + 1))
  local dir="$work/terminate-envelope"
  make_interruptible_sandbox "$dir"
  if ! run_interrupted_gate "$dir" TERM; then
    fail "terminate envelope" "the run never reached the stack smoke step"
    return
  fi
  if [ "$gate_status" -ne 143 ]; then
    fail "terminate envelope" "expected exit 143, got $gate_status"
    return
  fi
  if ! has_envelope "$gate_output" PLAN3_TERMINATED; then
    fail "terminate envelope" \
      "the envelope PLAN3_TERMINATED with a correlation id is missing: $(printf '%s' "$gate_output" | tail -n 3)"
    return
  fi
  if ! envelope_carries_the_run_correlation "$gate_output" PLAN3_TERMINATED \
    "$(run_correlation "$gate_output")"; then
    fail "terminate envelope" \
      "the envelope PLAN3_TERMINATED does not carry the correlation id of the run"
    return
  fi
  if [ ! -f "$dir/.teardown-marker" ]; then
    fail "terminate envelope" "the stack teardown did not run after the terminate (exit $gate_status)"
    return
  fi
  pass "terminate envelope (exit 143, PLAN3_TERMINATED, correlation id present, the stack teardown ran)"
}

# A teardown that fails while the interrupted run cleans up must be surfaced
# as a failure envelope, not as a bare prose warning.
case_interrupted_teardown_failure_is_an_envelope() {
  log "case  a failing teardown of an interrupted run is a failure envelope"
  cases=$((cases + 1))
  local dir="$work/interrupt-teardown-failure"
  make_interruptible_sandbox "$dir"
  printf '#!/usr/bin/env bash\ntouch "%s/.teardown-marker"\nexit 1\n' "$dir" > "$dir/scripts/dev-down.sh"
  chmod +x "$dir/scripts/dev-down.sh"
  if ! run_interrupted_gate "$dir" INT; then
    fail "interrupted teardown failure" "the run never reached the stack smoke step"
    return
  fi
  if [ "$gate_status" -ne 130 ]; then
    fail "interrupted teardown failure" "expected exit 130, got $gate_status"
    return
  fi
  if ! has_envelope "$gate_output" PLAN3_STACK_TEARDOWN_FAILED; then
    fail "interrupted teardown failure" \
      "the envelope PLAN3_STACK_TEARDOWN_FAILED with a correlation id is missing: $(printf '%s' "$gate_output" | tail -n 3)"
    return
  fi
  if ! envelope_carries_the_run_correlation "$gate_output" PLAN3_STACK_TEARDOWN_FAILED \
    "$(run_correlation "$gate_output")"; then
    fail "interrupted teardown failure" \
      "the envelope PLAN3_STACK_TEARDOWN_FAILED does not carry the correlation id of the run"
    return
  fi
  if [ ! -f "$dir/.teardown-marker" ]; then
    fail "interrupted teardown failure" "the teardown of the interrupted run did not run at all"
    return
  fi
  pass "interrupted teardown failure (exit 130, PLAN3_STACK_TEARDOWN_FAILED, correlation id present)"
}

case_clean_export_refuses_a_used_target() {
  log "case  the clean export refuses a target directory that is not empty"
  cases=$((cases + 1))
  local target="$work/used-target"
  mkdir -p "$target"
  printf 'keep me\n' > "$target/keep.txt"
  local out="$work/export.out"
  ( cd "$root" && bash "$root/tests/Plan3/clean-export.sh" --sha HEAD --base-ref "$base_ref" --target "$target" ) > "$out" 2>&1
  local status=$?
  local output
  output=$(cat "$out")
  if [ "$status" -eq 0 ]; then
    fail "clean export target" "the export accepted a non-empty target directory"
    return
  fi
  if ! printf '%s' "$output" | grep -q 'PLAN3_EXPORT_TARGET_INVALID'; then
    fail "clean export target" "the reason token PLAN3_EXPORT_TARGET_INVALID is missing"
    return
  fi
  if [ ! -f "$target/keep.txt" ]; then
    fail "clean export target" "the export deleted the content of the refused target"
    return
  fi
  pass "clean export target (exit $status, PLAN3_EXPORT_TARGET_INVALID, content kept)"
}

case_failed_stack_start_tears_the_stack_down() {
  log "case  a failed stack start tears the started stack down"
  cases=$((cases + 1))
  local dir="$work/stack-start-failure"
  make_sandbox "$dir"
  stub_driven_tools "$dir"
  # The stub starts a long-lived process and then fails, like a dev-up.sh that
  # breaks after the containers are up.
  cat > "$dir/scripts/dev-up.sh" <<EOF
#!/usr/bin/env bash
sleep 300 &
printf '%s' "\$!" > "$dir/.stack-pid"
exit 1
EOF
  # The stub teardown stops that process and records that it ran.
  cat > "$dir/scripts/dev-down.sh" <<EOF
#!/usr/bin/env bash
if [ -f "$dir/.stack-pid" ]; then
  kill "\$(cat "$dir/.stack-pid")" 2>/dev/null
fi
touch "$dir/.teardown-marker"
exit 0
EOF
  chmod +x "$dir/scripts/dev-up.sh" "$dir/scripts/dev-down.sh"
  run_gate "$dir" --base-ref "$base_ref" --with-browser
  if [ "$gate_status" -eq 0 ]; then
    fail "failed stack start" "the gate exited 0 although the stack did not start"
    return
  fi
  if printf '%s' "$gate_output" | grep -q 'plan3 aggregate: PASS'; then
    fail "failed stack start" "the gate reported PASS although the stack did not start"
    return
  fi
  if ! printf '%s' "$gate_output" | grep -q 'PLAN3_STACK_START_FAILED'; then
    fail "failed stack start" "the reason token PLAN3_STACK_START_FAILED is missing"
    return
  fi
  local stack_pid
  stack_pid=$(cat "$dir/.stack-pid" 2>/dev/null)
  if [ -z "$stack_pid" ]; then
    fail "failed stack start" "the stub did not start a process"
    return
  fi
  if ! wait_until_gone "$stack_pid"; then
    fail "failed stack start" "the stack process $stack_pid is STILL RUNNING after the failed start"
    return
  fi
  if [ ! -f "$dir/.teardown-marker" ]; then
    fail "failed stack start" "the teardown did not run after the failed stack start"
    return
  fi
  pass "failed stack start (exit $gate_status, PLAN3_STACK_START_FAILED, the stack process is gone)"
}

case_staged_file_mutation_is_detected() {
  log "case  a staged file that a step rewrites is detected"
  cases=$((cases + 1))
  local dir="$work/staged-mutation"
  make_sandbox "$dir"
  stub_driven_tools "$dir"
  # A file that is staged in the index but absent from HEAD: it is in neither
  # the tracked list of HEAD nor in the untracked list.
  printf 'probe\n' > "$dir/plan3-staged-probe.txt"
  if ! git -C "$dir" add plan3-staged-probe.txt >/dev/null 2>&1; then
    fail "staged mutation" "the probe file could not be staged in the sandbox"
    return
  fi
  # A step rewrites the staged file while the run is in progress.
  cat > "$dir/tests/ClientConsumers/run.sh" <<EOF
#!/usr/bin/env bash
printf 'rewritten by a step\n' >> "$dir/plan3-staged-probe.txt"
exit 0
EOF
  chmod +x "$dir/tests/ClientConsumers/run.sh"
  run_gate "$dir" --base-ref "$base_ref"
  if [ "$gate_status" -eq 0 ]; then
    fail "staged mutation" "the gate exited 0 although a staged file was rewritten"
    return
  fi
  if printf '%s' "$gate_output" | grep -q 'plan3 aggregate: PASS'; then
    fail "staged mutation" "the gate reported PASS although a staged file was rewritten"
    return
  fi
  if ! printf '%s' "$gate_output" | grep -q 'PLAN3_SOURCE_MUTATED'; then
    fail "staged mutation" "the reason token PLAN3_SOURCE_MUTATED is missing"
    return
  fi
  pass "staged mutation (exit $gate_status, PLAN3_SOURCE_MUTATED, no PASS)"
}

case_harness_argument_envelopes() {
  log "case  the harness argument errors carry a reason token and a correlation id"
  cases=$((cases + 1))
  local out="$work/harness-argument.out"
  local status output
  ( cd "$root" && bash "$root/tests/Plan3/negative-cases.sh" --not-a-real-argument ) > "$out" 2>&1
  status=$?
  output=$(cat "$out")
  if [ "$status" -ne 2 ]; then
    fail "harness argument error" "negative cases: expected exit 2, got $status"
    return
  fi
  if ! printf '%s' "$output" | grep -q 'PLAN3_ARGUMENT_INVALID'; then
    fail "harness argument error" "negative cases: the reason token PLAN3_ARGUMENT_INVALID is missing"
    return
  fi
  if ! has_correlation "$output"; then
    fail "harness argument error" "negative cases: no correlation id in: $(printf '%s' "$output" | tail -n 3)"
    return
  fi
  ( cd "$root" && bash "$root/tests/Plan3/clean-export.sh" --sha ) > "$out" 2>&1
  status=$?
  output=$(cat "$out")
  if [ "$status" -ne 2 ]; then
    fail "harness argument error" "clean export: expected exit 2, got $status"
    return
  fi
  if ! printf '%s' "$output" | grep -q 'PLAN3_ARGUMENT_INVALID'; then
    fail "harness argument error" "clean export: the reason token PLAN3_ARGUMENT_INVALID is missing"
    return
  fi
  if ! has_correlation "$output"; then
    fail "harness argument error" "clean export: no correlation id in: $(printf '%s' "$output" | tail -n 3)"
    return
  fi
  pass "harness argument error (exit 2, PLAN3_ARGUMENT_INVALID, correlation id present)"
}

# The contract suite is the only suite the live CI workflow of the repository
# runs. This case simulates a CI checkout of it: without the pinned local tool
# artifacts below .local/plan3-tools and without an installed node_modules, the
# Plan 3 tests must be skipped with a visible reason and the pre-existing tests
# must keep passing, so that the workflow stays green.
case_ci_checkout_keeps_the_contract_suite_green() {
  log "case  a CI-like checkout keeps the contract suite green"
  cases=$((cases + 1))
  local out="$work/ci-environment.out"
  ( cd "$root" && bash "$root/tests/Plan3/ci-environment.sh" ) > "$out" 2>&1
  local status=$?
  local output
  output=$(cat "$out")
  if [ "$status" -ne 0 ]; then
    fail "ci checkout" "the simulated CI run exited $status, expected 0" \
      "$(printf '%s' "$output" | tail -n 10)"
    return
  fi
  if ! printf '%s' "$output" | grep -q '87 tests, 55 skipped with a reason, 32 passed'; then
    fail "ci checkout" "the simulated CI run did not report the expected counts" \
      "$(printf '%s' "$output" | tail -n 5)"
    return
  fi
  pass "ci checkout (exit 0, 87 tests, 55 skipped with a reason, 32 passed)"
}

# A comparator that cannot load the typescript module must close with the
# ordinary envelope: a reason token, a correlation identifier and no raw stack
# trace on stderr.
case_missing_typescript_is_a_closed_error() {
  log "case  a comparator without typescript closes with a reason token and a correlation id"
  cases=$((cases + 1))
  local dir="$work/no-typescript"
  mkdir -p "$dir"
  cp "$root/tools/contracts/surface/compare-typescript.cjs" "$dir/"
  printf 'export interface Thing { id: string }\n' > "$dir/base.d.ts"
  cp "$dir/base.d.ts" "$dir/candidate.d.ts"
  local out="$work/no-typescript.out"
  local err="$work/no-typescript.err"
  # The comparator runs outside the checkout, so the typescript module of the
  # repository is not resolvable for it.
  ( cd "$dir" && node --no-global-search-paths "$dir/compare-typescript.cjs" \
    "$dir/base.d.ts" "$dir/candidate.d.ts" ) > "$out" 2> "$err"
  local status=$?
  if [ "$status" -eq 0 ]; then
    fail "missing typescript" "the comparator exited 0 although it cannot load typescript"
    return
  fi
  if [ -s "$err" ]; then
    fail "missing typescript" "the comparator printed on stderr: $(head -n 3 "$err")"
    return
  fi
  if ! grep -Eq '"error"[[:space:]]*:[[:space:]]*"SDK_SURFACE_UNAVAILABLE"' "$out"; then
    fail "missing typescript" "the reason token SDK_SURFACE_UNAVAILABLE is missing" \
      "output: $(cat "$out")"
    return
  fi
  if ! grep -Eq '"correlationId"[[:space:]]*:[[:space:]]*"[^"]{8,}"' "$out"; then
    fail "missing typescript" "no correlation id in: $(cat "$out")"
    return
  fi
  pass "missing typescript (exit $status, SDK_SURFACE_UNAVAILABLE, correlation id present, no stack trace)"
}

# A scratch directory that a script created itself must be unlinked, never moved
# into a recoverable trash. A recoverable-delete shim on the PATH would copy a
# multi-gigabyte scratch tree into the trash of the user instead of freeing it.
# The harness of the case runs the fastest Plan 3 harness in a child shell
# whose PATH carries a fake rm first, and asserts that the fake rm was never
# invoked, that the trash of the controlled data home stayed empty and that the
# scratch of the child is gone.
case_scratch_cleanup_unlinks_the_scratch() {
  log "case  a scratch directory is unlinked instead of being moved to a trash"
  cases=$((cases + 1))
  local out="$work/scratch-cleanup.out"
  ( cd "$root" && bash "$root/tests/Plan3/scratch-cleanup.sh" ) > "$out" 2>&1
  local status=$?
  local output
  output=$(cat "$out")
  if [ "$status" -ne 0 ]; then
    fail "scratch cleanup" "the scratch cleanup harness exited $status, expected 0" \
      "$(printf '%s' "$output" | tail -n 10)"
    return
  fi
  if ! printf '%s' "$output" | grep -q 'the PATH-resolved rm was never invoked'; then
    fail "scratch cleanup" "the scratch cleanup harness did not report its expected result" \
      "$(printf '%s' "$output" | tail -n 5)"
    return
  fi
  pass "scratch cleanup (exit 0, the scratch was unlinked, the PATH-resolved rm was never invoked)"
}

run_case() {
  local name="$1"
  [ -z "$only" ] || [ "$only" = "$name" ] || return 0
  "case_$name"
}

run_case argument_error
run_case toolchain_mismatch
run_case steps_after_failure_are_recorded
run_case teardown_failure
run_case ordinary_teardown_failure_is_reported_once
run_case interrupt_tears_the_stack_down
run_case interrupt_emits_a_reason_envelope
run_case terminate_emits_a_reason_envelope
run_case interrupted_teardown_failure_is_an_envelope
run_case clean_export_refuses_a_used_target
run_case failed_stack_start_tears_the_stack_down
run_case staged_file_mutation_is_detected
run_case harness_argument_envelopes
run_case ci_checkout_keeps_the_contract_suite_green
run_case missing_typescript_is_a_closed_error
run_case scratch_cleanup_unlinks_the_scratch

log ""
if [ "$failures" -ne 0 ]; then
  log "aggregate reporting cases: $failures of $cases failed"
  closed_error PLAN3_REGRESSION_CASES_FAILED "$failures of $cases cases failed" 1
fi
log "aggregate reporting cases: $cases of $cases passed"
