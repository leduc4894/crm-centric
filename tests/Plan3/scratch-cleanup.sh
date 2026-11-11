#!/usr/bin/env bash
# Regression coverage for the cleanup of the scratch directories of this slice.
#
# A scratch directory that a script created itself, minutes ago, must be
# unlinked. It must never be moved into a recoverable trash: some agent
# runtimes put a recoverable-delete shim named `rm` on the PATH, and on Linux
# that shim copies its target into the trash of the user (the XDG Trash spec)
# instead of unlinking it. A multi-gigabyte scratch tree is then copied rather
# than freed, and the disk of the host fills up.
#
# This harness proves that the Plan 3 harnesses unlink their scratch. It runs
# the fastest Plan 3 harness in a child shell whose PATH carries a fake rm
# first. The fake rm behaves like the recoverable-delete shim: it records that
# it was called and it moves its target into the trash of a controlled
# XDG_DATA_HOME instead of unlinking it. The child also runs with a controlled
# TMPDIR, so that its scratch lands inside the controlled directories of this
# harness. The trash of the user is never touched.
#
# The harness is self-contained: it resolves the rm binary that unlinks its
# own scratch by itself and does not use the helper under test, so that it can
# be run against a tree that does not have the helper at all.
#
# Usage: bash tests/Plan3/scratch-cleanup.sh
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

# The rm binary that unlinks the scratch of this harness. It is resolved here
# and not through the helper under test, so that the harness stays independent
# of the code it proves.
rm_binary=""
for rm_candidate in /bin/rm /usr/bin/rm; do
  if [ -x "$rm_candidate" ]; then
    rm_binary="$rm_candidate"
    break
  fi
done
[ -n "$rm_binary" ] ||
  closed_error PLAN3_RM_UNAVAILABLE "no rm binary could be resolved" 1

work=$(mktemp -d) ||
  closed_error PLAN3_TEMP_DIRECTORY_UNAVAILABLE "a temporary directory could not be created" 1
cleanup() { "$rm_binary" -rf -- "$work"; }
trap cleanup EXIT

log() { printf '%s\n' "$*"; }

fail() {
  failures=$((failures + 1))
  log "FAIL  $1"
  shift
  for line in "$@"; do log "      $line"; done
}

failures=0

# The controlled directories of the child. The TMPDIR receives the scratch of
# the child, the XDG_DATA_HOME receives everything that a recoverable delete
# would write into the trash. Both live inside the scratch of this harness, so
# that the trash of the user is never touched.
tmpdir="$work/tmp"
xdgdata="$work/xdg-data"
fakebin="$work/fakebin"
mkdir -p "$tmpdir" "$xdgdata" "$fakebin" ||
  closed_error PLAN3_TEMP_DIRECTORY_UNAVAILABLE "the controlled directories could not be created" 1

# The fake rm: it records that it was called and moves its target into the
# trash of the controlled XDG_DATA_HOME. It never unlinks anything, so a
# scratch that is deleted through it survives.
fake_rm_log="$work/fake-rm.log"
cat > "$fakebin/rm" <<'FAKE_RM'
#!/usr/bin/env bash
# A fake rm that behaves like a recoverable-delete shim: it records the call
# and moves the target into the trash instead of unlinking it.
set -u
if [ -n "${PLAN3_FAKE_RM_LOG:-}" ]; then
  printf '%s\n' "$*" >> "$PLAN3_FAKE_RM_LOG"
fi
target=""
for argument in "$@"; do
  case "$argument" in
    -*) continue ;;
    *) target="$argument"; break ;;
  esac
done
[ -n "$target" ] || exit 0
[ -e "$target" ] || exit 0
trash="${XDG_DATA_HOME:-$HOME/.local/share}/Trash/files"
mkdir -p "$trash" || exit 1
name="${target##*/}"
destination="$trash/$name"
index=1
while [ -e "$destination" ]; do
  destination="$trash/$name.$index"
  index=$((index + 1))
done
mv "$target" "$destination" || exit 1
exit 0
FAKE_RM
chmod +x "$fakebin/rm"

out="$work/child.out"
( cd "$root" && \
  TMPDIR="$tmpdir" \
  XDG_DATA_HOME="$xdgdata" \
  PATH="$fakebin:$PATH" \
  PLAN3_FAKE_RM_LOG="$fake_rm_log" \
  bash "$root/tests/Plan3/ci-environment.sh" ) > "$out" 2>&1
status=$?

if [ "$status" -ne 0 ]; then
  fail "scratch cleanup" "the child harness exited $status, expected 0" \
    "$(tail -n 10 "$out")"
fi

# The PATH-resolved rm must never be reached: the scratch of the child is
# unlinked through an absolute rm binary.
if [ -s "$fake_rm_log" ]; then
  fail "scratch cleanup" "the PATH-resolved rm was invoked" \
    "$(head -n 5 "$fake_rm_log")"
fi

# Nothing may reach the controlled data home: a recoverable delete would have
# written the scratch into the trash of the user instead of unlinking it.
if [ -n "$(ls -A "$xdgdata" 2>/dev/null)" ]; then
  detail="$(ls -A "$xdgdata/Trash/files" 2>/dev/null)"
  [ -n "$detail" ] || detail="$(ls -A "$xdgdata" 2>/dev/null)"
  fail "scratch cleanup" "the scratch was moved into a recoverable trash instead of being unlinked" \
    "$detail"
fi

# The scratch of the child must be gone, not left behind in the controlled
# temporary directory.
if [ -n "$(ls -A "$tmpdir" 2>/dev/null)" ]; then
  fail "scratch cleanup" "the controlled TMPDIR is not empty after the run" \
    "$(ls -A "$tmpdir" 2>/dev/null | head -n 5)"
fi

log ""
if [ "$failures" -ne 0 ]; then
  log "scratch cleanup cases: $failures failed"
  closed_error PLAN3_SCRATCH_CLEANUP_FAILED "$failures assertions failed" 1
fi
log "scratch cleanup: the scratch of the child was unlinked, the PATH-resolved rm was never invoked"
