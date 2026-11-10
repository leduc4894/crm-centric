#!/usr/bin/env bash
# Plan 3 aggregate check.
#
# Runs the Plan 3 public entrypoints in a fixed order and fails closed on the
# first failing step:
#
#   locked install -> generated check -> SDK build/pack/consumers ->
#   compatibility gate -> frontend typecheck/lint/test/build/leak scan ->
#   browser suite
#
# The script never fetches, resets, rebases or rebaselines Git and never
# modifies a tracked file, the lockfile, the generated SDKs or the baseline.
# Every step writes only to a temporary directory or to an ignored build output
# directory; the tracked-file digest is compared before and after the run.
#
# Usage:
#   bash scripts/check-plan3.sh --base-ref <ref> [--with-browser]
#
#   --base-ref <ref>   Required. A Git revision that already exists in the local
#                      clone. It is passed verbatim to
#                      scripts/contracts/compatibility.py, which resolves the
#                      merge base with HEAD and never fetches a remote ref.
#                      There is no default: a missing or empty value fails
#                      closed with PLAN3_ARGUMENT_INVALID.
#   --with-browser     Opt in to the browser suite. The suite needs the local
#                      Docker stack, so the script starts it and stops it again.
#                      Without the flag the step is recorded as "not run"; it is
#                      never silently skipped.
#
# Exit codes: 0 every step passed, 1 a step failed, 2 the arguments were invalid.
set -uo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd) || exit 1
cd "$root" || exit 1

usage() {
  cat <<'EOF'
Usage: bash scripts/check-plan3.sh --base-ref <ref> [--with-browser]

  --base-ref <ref>   required; Git revision already present in the local clone
  --with-browser     run the browser suite against the local Docker stack
EOF
}

argument_error() {
  printf 'PLAN3_ARGUMENT_INVALID: %s\n' "$1" >&2
  usage >&2
  exit 2
}

base_ref=""
with_browser=0
while [ $# -gt 0 ]; do
  case "$1" in
    --base-ref)
      [ $# -ge 2 ] || argument_error "--base-ref requires a value"
      base_ref="$2"
      shift 2
      ;;
    --base-ref=*)
      base_ref="${1#--base-ref=}"
      shift
      ;;
    --with-browser)
      with_browser=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      argument_error "unknown argument: $1"
      ;;
  esac
done
[ -n "$base_ref" ] || argument_error "--base-ref is required"

logs=$(mktemp -d) || exit 1
trap 'rm -rf "$logs"' EXIT

step_names=()
step_commands=()
step_status=()
step_seconds=()
step_reasons=()

# Reads the reason token out of a step log. The contract tooling prints a safe
# JSON report on its last line; anything else falls back to the step default.
extract_reason() {
  python3 - "$1" <<'PY' 2>/dev/null
import json
import sys

text = open(sys.argv[1], encoding='utf-8', errors='replace').read()
for line in reversed(text.splitlines()):
    line = line.strip()
    if not line.startswith('{'):
        continue
    try:
        data = json.loads(line)
    except ValueError:
        continue
    if isinstance(data, dict) and isinstance(data.get('reason'), str):
        print(data['reason'])
        break
PY
}

default_reason() {
  case "$1" in
    locked-install) printf 'PLAN3_LOCKED_INSTALL_FAILED' ;;
    generated-check) printf 'PLAN3_GENERATED_CHECK_FAILED' ;;
    sdk-consumers) printf 'PLAN3_SDK_CONSUMERS_FAILED' ;;
    compatibility) printf 'PLAN3_COMPATIBILITY_FAILED' ;;
    browser-e2e) printf 'PLAN3_BROWSER_STEP_FAILED' ;;
    frontend-*) printf 'PLAN3_FRONTEND_STEP_FAILED' ;;
    *) printf 'PLAN3_STEP_FAILED' ;;
  esac
}

# Digest of every tracked file in the worktree. Compared before and after the
# run to prove that no step mutated the source tree.
source_digest() {
  {
    if git -C "$root" ls-tree -r --name-only HEAD -- >/dev/null 2>&1; then
      git -C "$root" ls-tree -r --name-only HEAD -- | LC_ALL=C sort
    else
      find docs/contracts sdks tools/contracts scripts/contracts src -type f \
        -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null | LC_ALL=C sort
    fi
  } | while IFS= read -r file; do
    path="$root/$file"
    if [ -L "$path" ]; then
      printf '%s link %s\n' "$file" "$(readlink "$path")"
    elif [ -f "$path" ]; then
      printf '%s ' "$file"
      sha256sum < "$path" | cut -d' ' -f1
    else
      printf '%s missing\n' "$file"
    fi
  done | sha256sum | cut -d' ' -f1
}

# The browser suite needs the local stack. The stack is started and stopped
# here so that the run owns its lifecycle.
browser_step() {
  local status=0
  if ! bash scripts/dev-up.sh; then
    bash scripts/dev-down.sh ||
      printf 'warning: the local stack was not stopped cleanly\n'
    return 1
  fi
  bash scripts/dev-smoke.sh || status=$?
  npm run test:e2e -w apps/crm-web || status=$?
  bash scripts/dev-down.sh ||
    printf 'warning: the local stack was not stopped cleanly\n'
  return "$status"
}

run_named_step() {
  case "$1" in
    locked-install) npm ci --ignore-scripts --no-audit --no-fund ;;
    generated-check) python3 scripts/contracts/check.py ;;
    sdk-consumers) bash tests/ClientConsumers/run.sh ;;
    compatibility) python3 scripts/contracts/compatibility.py --base-ref "$base_ref" ;;
    frontend-typecheck) npm run typecheck -w apps/crm-web ;;
    frontend-lint) npm run lint -w apps/crm-web ;;
    frontend-test) npm run test -w apps/crm-web ;;
    frontend-build) npm run build -w apps/crm-web ;;
    frontend-leak-scan) npm run scan:leaks -w apps/crm-web ;;
    browser-e2e) browser_step ;;
    *) return 1 ;;
  esac
}

run_step() {
  local name="$1" command="$2"
  local start end status reason
  start=$(date +%s)
  run_named_step "$name" >"$logs/$name.log" 2>&1
  status=$?
  end=$(date +%s)
  reason=$(extract_reason "$logs/$name.log")
  if [ "$status" -ne 0 ] && [ -z "$reason" ]; then
    reason=$(default_reason "$name")
  fi
  [ -n "$reason" ] || reason="-"
  step_names+=("$name")
  step_commands+=("$command")
  step_status+=("$status")
  step_seconds+=("$((end - start))")
  step_reasons+=("$reason")
  printf '\n===== %s: %s (exit %s, %ss) =====\n' "$name" "$command" "$status" "$((end - start))"
  cat "$logs/$name.log"
  return "$status"
}

record_not_run() {
  local name="$1" command="$2" reason="$3"
  step_names+=("$name")
  step_commands+=("$command")
  step_status+=("not run")
  step_seconds+=("-")
  step_reasons+=("$reason")
}

printf 'plan3 aggregate: base ref %s\n' "$base_ref"

# Prerequisites. A missing or mismatched prerequisite fails closed instead of
# producing a misleading step failure.
prerequisite_failure=""
command -v node >/dev/null 2>&1 || prerequisite_failure="PLAN3_PREREQUISITE_MISSING: node"
if [ -z "$prerequisite_failure" ]; then
  expected_node=$(tr -d '[:space:]' < .node-version)
  actual_node=$(node --version 2>/dev/null | tr -d 'v[:space:]')
  [ "$expected_node" = "$actual_node" ] ||
    prerequisite_failure="PLAN3_TOOLCHAIN_MISMATCH: node $actual_node, expected $expected_node"
fi
if [ -z "$prerequisite_failure" ]; then
  command -v docker >/dev/null 2>&1 ||
    prerequisite_failure="PLAN3_PREREQUISITE_MISSING: docker"
fi
if [ -z "$prerequisite_failure" ]; then
  command -v python3 >/dev/null 2>&1 ||
    prerequisite_failure="PLAN3_PREREQUISITE_MISSING: python3"
fi
if [ -n "$prerequisite_failure" ]; then
  printf '%s\n' "$prerequisite_failure" >&2
  exit 1
fi

digest_before=$(source_digest)

failed_step=""
failed_reason=""
run_next() {
  [ -z "$failed_step" ] || return 1
  run_step "$1" "$2" || {
    failed_step="$1"
    failed_reason="${step_reasons[${#step_reasons[@]}-1]}"
  }
}

run_next locked-install "npm ci --ignore-scripts --no-audit --no-fund"
run_next generated-check "python3 scripts/contracts/check.py"
run_next sdk-consumers "bash tests/ClientConsumers/run.sh"
run_next compatibility "python3 scripts/contracts/compatibility.py --base-ref $base_ref"
run_next frontend-typecheck "npm run typecheck -w apps/crm-web"
run_next frontend-lint "npm run lint -w apps/crm-web"
run_next frontend-test "npm run test -w apps/crm-web"
run_next frontend-build "npm run build -w apps/crm-web"
run_next frontend-leak-scan "npm run scan:leaks -w apps/crm-web"

if [ "$with_browser" -eq 1 ]; then
  if [ -z "$failed_step" ]; then
    run_next browser-e2e "bash scripts/dev-up.sh; bash scripts/dev-smoke.sh; npm run test:e2e -w apps/crm-web; bash scripts/dev-down.sh"
  else
    record_not_run browser-e2e "npm run test:e2e -w apps/crm-web" \
      "not run: an earlier step failed"
  fi
elif [ -n "$failed_step" ]; then
  record_not_run browser-e2e "npm run test:e2e -w apps/crm-web" \
    "not run: an earlier step failed"
else
  record_not_run browser-e2e "npm run test:e2e -w apps/crm-web" \
    "not run: the browser suite needs the local Docker stack; pass --with-browser to run it"
fi

digest_after=$(source_digest)
mutated=0
[ "$digest_before" = "$digest_after" ] || mutated=1

printf '\n===== plan3 aggregate summary =====\n'
printf '%-20s %-8s %-9s %s\n' 'step' 'exit' 'seconds' 'reason'
for index in "${!step_names[@]}"; do
  printf '%-20s %-8s %-9s %s\n' "${step_names[$index]}" "${step_status[$index]}" \
    "${step_seconds[$index]}" "${step_reasons[$index]}"
done
printf '\ncommands:\n'
for index in "${!step_names[@]}"; do
  printf '  %-20s %s\n' "${step_names[$index]}" "${step_commands[$index]}"
done
printf 'source digest: %s\n' "$digest_after"
if [ "$mutated" -eq 1 ]; then
  printf 'source digest: CHANGED during the run (expected %s)\n' "$digest_before"
else
  printf 'source digest: unchanged during the run\n'
fi

if [ -n "$failed_step" ]; then
  printf 'plan3 aggregate: FAIL at %s (%s)\n' "$failed_step" "$failed_reason"
  exit 1
fi
if [ "$mutated" -eq 1 ]; then
  printf 'plan3 aggregate: FAIL (PLAN3_SOURCE_MUTATED: a step modified a tracked file)\n'
  exit 1
fi
printf 'plan3 aggregate: PASS\n'
exit 0
