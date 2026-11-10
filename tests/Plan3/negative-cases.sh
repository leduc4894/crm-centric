#!/usr/bin/env bash
# Negative-case coverage for scripts/check-plan3.sh.
#
# The aggregate gate must fail closed. Every case below applies one fault to a
# sandbox copy of the checkout and asserts that the gate stops with the expected
# reason token and never reports a compatible or green result.
#
# The sandbox is a copy of the working tree with a read-only Git object context
# (objects/info/alternates) so that a base revision resolves without a fetch and
# without the sandbox carrying history of its own. The pinned local tool
# artifacts are copied in; they are not committed.
#
# Usage: bash tests/Plan3/negative-cases.sh [--base-ref <ref>]
set -uo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
base_ref="408ded6"
while [ $# -gt 0 ]; do
  case "$1" in
    --base-ref) base_ref="${2:-}"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

failures=0
cases=0

log() { printf '%s\n' "$*"; }

fail() {
  failures=$((failures + 1))
  log "FAIL  $1"
  shift
  for line in "$@"; do log "      $line"; done
}

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

# Runs the aggregate gate in the sandbox and reports its exit code and output.
run_gate() {
  local dir="$1"
  shift
  local out="$work/gate.out"
  ( cd "$dir" && bash "$dir/scripts/check-plan3.sh" "$@" ) > "$out" 2>&1
  gate_status=$?
  gate_output=$(cat "$out")
}

assert_fails_with() {
  local name="$1" expected="$2"
  cases=$((cases + 1))
  if [ "$gate_status" -eq 0 ]; then
    fail "$name" "the gate exited 0" "expected reason: $expected"
    return
  fi
  if ! printf '%s' "$gate_output" | grep -q "$expected"; then
    fail "$name" "the gate did not report $expected" "output: $(printf '%s' "$gate_output" | tail -n 5)"
    return
  fi
  if printf '%s' "$gate_output" | grep -q 'CONTRACT_COMPATIBLE'; then
    fail "$name" "the gate reported CONTRACT_COMPATIBLE for a faulty tree"
    return
  fi
  log "ok    $name (exit $gate_status, reason $expected)"
}

case_tampered_generated() {
  log "case  tampered generated output"
  local dir="$work/tampered"
  make_sandbox "$dir"
  printf '\n// tampered by the negative case\n' >> "$dir/sdks/typescript/crm/index.ts"
  run_gate "$dir" --base-ref "$base_ref"
  assert_fails_with "tampered generated output" "SDK_GENERATED_MISMATCH"
}

case_missing_base_ref() {
  log "case  missing base revision"
  local dir="$work/base"
  make_sandbox "$dir"
  run_gate "$dir" --base-ref "refs/heads/plan3-does-not-exist"
  assert_fails_with "missing base revision" "CONTRACT_BASE_UNAVAILABLE"
}

case_policy_change() {
  log "case  policy or waiver change"
  local dir="$work/policy"
  make_sandbox "$dir"
  python3 - "$dir/tools/contracts/oasdiff.json" <<'PY'
import json, sys
path = sys.argv[1]
policy = json.load(open(path))
policy['breakOn'] = 'ERR'
json.dump(policy, open(path, 'w'), indent=2, sort_keys=True)
open(path, 'a').write('\n')
PY
  run_gate "$dir" --base-ref "$base_ref"
  assert_fails_with "policy or waiver change" "CONTRACT_POLICY_REVIEW_REQUIRED"
}

case_tampered_generated
case_missing_base_ref
case_policy_change

log ""
if [ "$failures" -ne 0 ]; then
  log "negative cases: $failures of $cases failed"
  exit 1
fi
log "negative cases: $cases of $cases passed"
