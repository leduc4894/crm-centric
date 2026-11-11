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
# directory; the source digest is compared before and after the run.
#
# Every closed error path of this script produces a reason token and the
# correlation identifier of the run. This includes the signal paths: an
# interrupted run reports PLAN3_INTERRUPTED and exits 130, a terminated run
# reports PLAN3_TERMINATED and exits 143, and a stack that the run could not
# stop reports PLAN3_STACK_TEARDOWN_FAILED. Every condition is reported
# exactly once, so the operator never sees the same envelope twice.
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
#                      A teardown that fails fails the step. Without the flag
#                      the step is recorded as "not run"; it is never silently
#                      skipped.
#
# Exit codes: 0 every step passed, 1 a step failed, 2 the arguments were
# invalid, 130 the run was interrupted (SIGINT), 143 the run was terminated
# (SIGTERM).
set -uo pipefail

# The correlation identifier of this run. It accompanies every closed error
# path of the aggregate itself.
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

# The stderr of the aggregate, saved before a step redirects it into its own
# log. A signal handler and the cleanup of an interrupted run must reach the
# operator, not the log of the step that happened to be running when the
# signal arrived.
exec 9>&2

# A closed error path of the aggregate: a reason token, a safe detail and the
# correlation identifier of this run. No payload, no secret. The envelope goes
# to the saved stderr, so that it reaches the operator even when the closed
# error path is reached while a step had redirected stderr into its own log.
closed_error() {
  local reason="$1" detail="$2" code="$3"
  printf '{"reason": "%s", "detail": "%s", "correlationId": "%s"}\n' \
    "$reason" "$detail" "$correlation" >&9
  exit "$code"
}

# A closed error path that a signal reached. The envelope goes to the saved
# stderr, because the step that was running when the signal arrived had
# redirected stderr into its own log. The exit code is the conventional one of
# the signal: 130 for INT and 143 for TERM.
signal_error() {
  local reason="$1" detail="$2" code="$3"
  printf '{"reason": "%s", "detail": "%s", "correlationId": "%s"}\n' \
    "$reason" "$detail" "$correlation" >&9
  exit "$code"
}

# The conditions that the run detected, with the reason token of each and a
# safe detail, and the flag that tells whether the condition was already
# reported as an envelope. A condition is reported exactly once: the step that
# detects it and the cleanup of the run report the same condition only once.
condition_reasons=()
condition_details=()
condition_reported=()

# Records a condition of the run. A condition that is already recorded is not
# recorded twice: the first report of a condition is the report of the run.
record_condition() {
  local reason="$1" detail="$2" index
  for index in "${!condition_reasons[@]}"; do
    if [ "${condition_reasons[$index]}" = "$reason" ]; then
      return 0
    fi
  done
  condition_reasons+=("$reason")
  condition_details+=("$detail")
  condition_reported+=("0")
}

# Tells whether a reason token is one of the recorded conditions.
condition_is_recorded() {
  local wanted="$1" index
  for index in "${!condition_reasons[@]}"; do
    if [ "${condition_reasons[$index]}" = "$wanted" ]; then
      return 0
    fi
  done
  return 1
}

# Reports the conditions that were recorded but not yet reported, each as an
# envelope with a reason token and the correlation identifier of the run. The
# envelopes go to the saved stderr, so that they reach the operator even when
# a step had redirected stderr into its own log.
report_conditions() {
  local index
  for index in "${!condition_reasons[@]}"; do
    if [ "${condition_reported[$index]}" = "0" ]; then
      printf '{"reason": "%s", "detail": "%s", "correlationId": "%s"}\n' \
        "${condition_reasons[$index]}" "${condition_details[$index]}" "$correlation" >&9
      condition_reported[$index]=1
    fi
  done
}

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd) ||
  closed_error PLAN3_WORKSPACE_UNAVAILABLE "the checkout could not be located" 1
cd "$root" || closed_error PLAN3_WORKSPACE_UNAVAILABLE "the checkout could not be entered" 1

usage() {
  cat <<'EOF'
Usage: bash scripts/check-plan3.sh --base-ref <ref> [--with-browser]

  --base-ref <ref>   required; Git revision already present in the local clone
  --with-browser     run the browser suite against the local Docker stack
EOF
}

argument_error() {
  printf '{"reason": "PLAN3_ARGUMENT_INVALID", "detail": "%s", "correlationId": "%s"}\n' \
    "$1" "$correlation" >&9
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

logs=$(mktemp -d) ||
  closed_error PLAN3_TEMP_DIRECTORY_UNAVAILABLE "a temporary directory could not be created" 1
stack_started=0

# How the run ended. The wording of the cleanup must be accurate for a run
# that was interrupted or terminated and for a run that simply ended, so it
# never describes an ordinary exit as an interrupted one.
run_state="the run ended"

# The local stack is owned by this run. A run that ends before its stack was
# stopped must not orphan it.
cleanup() {
  local status=$?
  if [ "$stack_started" -eq 1 ]; then
    printf 'plan3 aggregate: stopping the local stack of this run (%s)\n' "$run_state" >&9
    if ! bash scripts/dev-down.sh >/dev/null 2>&1; then
      # A teardown that fails is a closed error path of its own: it is
      # reported as an envelope with a reason token and the correlation
      # identifier, not as a bare warning. A condition that the run already
      # reported is not reported a second time; the exit code of the run is
      # the one it had before the cleanup.
      record_condition PLAN3_STACK_TEARDOWN_FAILED \
        "the local stack of this run was not stopped cleanly"
      report_conditions
    fi
  fi
  rm -rf "$logs"
  return "$status"
}
trap cleanup EXIT
trap 'run_state="the run was interrupted"; signal_error PLAN3_INTERRUPTED "the run was interrupted" 130' INT
trap 'run_state="the run was terminated"; signal_error PLAN3_TERMINATED "the run was terminated" 143' TERM

step_names=()
step_commands=()
step_status=()
step_seconds=()
step_reasons=()

# Every step of the aggregate, in order.
ALL_STEPS="locked-install generated-check sdk-consumers compatibility frontend-typecheck frontend-lint frontend-test frontend-build frontend-leak-scan browser-e2e"

step_command() {
  case "$1" in
    locked-install) printf 'npm ci --ignore-scripts --no-audit --no-fund' ;;
    generated-check) printf 'python3 scripts/contracts/check.py' ;;
    sdk-consumers) printf 'bash tests/ClientConsumers/run.sh' ;;
    compatibility) printf 'python3 scripts/contracts/compatibility.py --base-ref %s' "$base_ref" ;;
    frontend-typecheck) printf 'npm run typecheck -w apps/crm-web' ;;
    frontend-lint) printf 'npm run lint -w apps/crm-web' ;;
    frontend-test) printf 'npm run test -w apps/crm-web' ;;
    frontend-build) printf 'npm run build -w apps/crm-web' ;;
    frontend-leak-scan) printf 'npm run scan:leaks -w apps/crm-web' ;;
    browser-e2e)
      if [ "$with_browser" -eq 1 ]; then
        printf 'bash scripts/dev-up.sh; bash scripts/dev-smoke.sh; npm run test:e2e -w apps/crm-web; bash scripts/dev-down.sh'
      else
        printf 'npm run test:e2e -w apps/crm-web'
      fi
      ;;
    *) printf '' ;;
  esac
}

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

# Defensive redaction of the common secret shapes in the echoed step output.
# The deeper property, that the driven tools never emit a secret, stays theirs.
redact() {
  sed -E \
    -e 's/(Bearer)[[:space:]]+[A-Za-z0-9._~+/=-]{8,}/\1 [redacted]/g' \
    -e 's/(Authorization:[[:space:]]*)[^[:space:]]+/\1[redacted]/gI' \
    -e 's/((password|passwd|secret|client_secret|api_key|apikey|access_token|refresh_token|id_token|private_key|connection_string)[[:space:]]*[:=][[:space:]]*"?)[^",[:space:]]+/\1[redacted]/gI'
}

# The reduced scope of the source digest, used only when the tracked file list
# cannot be read from Git.
fallback_scope() {
  {
    for path in docs/contracts sdks tools/contracts scripts/contracts src; do
      [ -d "$path" ] || continue
      find "$path" -type f -not -path '*/bin/*' -not -path '*/obj/*' -print
    done
    for path in package.json package-lock.json .npmrc .node-version compose.yaml; do
      [ -f "$path" ] && printf '%s\n' "$path"
    done
  } | LC_ALL=C sort -u
}

# Digest of the tracked, the staged and the untracked files of the worktree.
# Compared before and after the run to prove that no step changed the source
# tree. A file that is staged in the index but absent from HEAD belongs to
# the index list, not to the tracked list of HEAD and not to the untracked
# list: without the index list it would be invisible to the digest.
source_digest() {
  local tracked indexed untracked
  tracked=$(git -C "$root" ls-tree -r --name-only HEAD -- 2>/dev/null)
  if [ -z "$tracked" ]; then
    printf 'plan3 aggregate: WARNING the source digest uses the reduced fallback scope\n' >&2
    tracked=$(fallback_scope)
  fi
  if ! indexed=$(git -C "$root" ls-files --cached 2>/dev/null); then
    printf 'plan3 aggregate: WARNING the files of the index are not covered by the source digest\n' >&2
    indexed=""
  fi
  if ! untracked=$(git -C "$root" ls-files --others --exclude-standard 2>/dev/null); then
    printf 'plan3 aggregate: WARNING untracked files are not covered by the source digest\n' >&2
    untracked=""
  fi
  { printf '%s\n' "$tracked"; printf '%s\n' "$indexed"; printf '%s\n' "$untracked"; } |
    LC_ALL=C sort -u |
    while IFS= read -r file; do
      [ -n "$file" ] || continue
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
# here so that the run owns its lifecycle. A teardown that fails fails the
# step: a run must never report success while the stack is still up. The
# conditions that the step detects are recorded, not printed here: the run
# reports each of them exactly once, as an envelope with a reason token and
# the correlation identifier of the run.
browser_step() {
  local status=0
  # The stack may be partially started when dev-up.sh fails, so the run owns
  # the teardown from this point on: the flag is set before the start, never
  # after it.
  stack_started=1
  if ! bash scripts/dev-up.sh; then
    record_condition PLAN3_STACK_START_FAILED "step browser-e2e: the local stack did not start"
    status=1
    if ! bash scripts/dev-down.sh; then
      record_condition PLAN3_STACK_TEARDOWN_FAILED \
        "step browser-e2e: the stack that did not start cleanly was not stopped cleanly"
      stack_started=1
    else
      stack_started=0
    fi
    return "$status"
  fi
  bash scripts/dev-smoke.sh || status=$?
  npm run test:e2e -w apps/crm-web || status=$?
  if ! bash scripts/dev-down.sh; then
    record_condition PLAN3_STACK_TEARDOWN_FAILED \
      "step browser-e2e: the local stack was not stopped cleanly"
    stack_started=1
    status=1
  else
    stack_started=0
  fi
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
  # The conditions that this step records are the reason of the step; they are
  # reported by the run itself, once.
  local before=${#condition_reasons[@]}
  start=$(date +%s)
  run_named_step "$name" >"$logs/$name.log" 2>&1
  status=$?
  end=$(date +%s)
  if [ "${#condition_reasons[@]}" -gt "$before" ]; then
    reason="${condition_reasons[$before]}"
  else
    reason=$(extract_reason "$logs/$name.log")
  fi
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
  redact < "$logs/$name.log"
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
printf 'plan3 aggregate: correlation %s\n' "$correlation"

# Prerequisites. A missing or mismatched prerequisite fails closed instead of
# producing a misleading step failure.
prerequisite_failure=""
prerequisite_detail=""
if ! command -v node >/dev/null 2>&1; then
  prerequisite_failure="PLAN3_PREREQUISITE_MISSING"
  prerequisite_detail="node is not on the PATH"
else
  expected_node=$(tr -d '[:space:]' < .node-version)
  actual_node=$(node --version 2>/dev/null | tr -d 'v[:space:]')
  if [ "$expected_node" != "$actual_node" ]; then
    prerequisite_failure="PLAN3_TOOLCHAIN_MISMATCH"
    prerequisite_detail="node $actual_node, expected $expected_node"
  fi
fi
if [ -z "$prerequisite_failure" ] && ! command -v docker >/dev/null 2>&1; then
  prerequisite_failure="PLAN3_PREREQUISITE_MISSING"
  prerequisite_detail="docker is not on the PATH"
fi
if [ -z "$prerequisite_failure" ] && ! command -v python3 >/dev/null 2>&1; then
  prerequisite_failure="PLAN3_PREREQUISITE_MISSING"
  prerequisite_detail="python3 is not on the PATH"
fi
if [ -n "$prerequisite_failure" ]; then
  closed_error "$prerequisite_failure" "$prerequisite_detail" 1
fi

digest_before=$(source_digest)

failed_step=""
failed_reason=""
for name in $ALL_STEPS; do
  command=$(step_command "$name")
  if [ -n "$failed_step" ]; then
    record_not_run "$name" "$command" "not run: an earlier step failed"
    continue
  fi
  if [ "$name" = browser-e2e ] && [ "$with_browser" -eq 0 ]; then
    record_not_run "$name" "$command" \
      "not run: the browser suite needs the local Docker stack; pass --with-browser to run it"
    continue
  fi
  run_step "$name" "$command" || {
    failed_step="$name"
    failed_reason="${step_reasons[${#step_reasons[@]}-1]}"
  }
done

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
  # The conditions that the step detected are reported here, each exactly
  # once, as an envelope with a reason token and the correlation identifier of
  # the run. A reason that was already reported is not reported again.
  report_conditions
  if ! condition_is_recorded "$failed_reason"; then
    closed_error "$failed_reason" "step $failed_step failed" 1
  fi
  exit 1
fi
if [ "$mutated" -eq 1 ]; then
  printf 'plan3 aggregate: FAIL (PLAN3_SOURCE_MUTATED: a step modified a file of the worktree)\n'
  closed_error PLAN3_SOURCE_MUTATED "a step modified a tracked, staged or untracked file of the worktree" 1
fi
printf 'plan3 aggregate: PASS\n'
exit 0
