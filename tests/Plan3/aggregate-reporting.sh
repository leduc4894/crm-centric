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
#   M-4  the clean export must refuse a target directory that is not empty.
#
# The sandbox is a copy of the checkout with a read-only Git object context
# (objects/info/alternates) so that a base revision resolves without a fetch.
# The pinned local tool artifacts are copied in; they are not committed.
#
# Usage: bash tests/Plan3/aggregate-reporting.sh [--base-ref <ref>]
set -uo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
base_ref="408ded6"
only=""
while [ $# -gt 0 ]; do
  case "$1" in
    --base-ref) base_ref="${2:-}"; shift 2 ;;
    --only) only="${2:-}"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

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

# Creates a sandbox copy of the checkout with a read-only Git object context.
make_sandbox() {
  local dir="$1"
  rm -rf "$dir"
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
  cp -R "$root/.local/plan3-tools/oasdiff-1.37.0" "$dir/.local/plan3-tools/"
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

step_is_not_run() {
  printf '%s' "$gate_output" |
    grep -E "^$1[[:space:]]+not run[[:space:]]+-" |
    grep -q 'not run: an earlier step failed'
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

run_case() {
  local name="$1"
  [ -z "$only" ] || [ "$only" = "$name" ] || return 0
  "case_$name"
}

run_case argument_error
run_case toolchain_mismatch
run_case steps_after_failure_are_recorded
run_case teardown_failure
run_case interrupt_tears_the_stack_down
run_case clean_export_refuses_a_used_target

log ""
if [ "$failures" -ne 0 ]; then
  log "aggregate reporting cases: $failures of $cases failed"
  exit 1
fi
log "aggregate reporting cases: $cases of $cases passed"
