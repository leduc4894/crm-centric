#!/usr/bin/env bash
# Clean exact-SHA export of this repository and aggregate check inside it.
#
# The export contains exactly the tracked content of one commit: it is produced
# with `git archive <sha>`, so it has no Git history of its own, no local tool
# artifacts and no build output. The trusted base revision is resolved through a
# read-only Git object context (`objects/info/alternates`) that points at the
# object store of the local clone. Nothing is fetched from a remote, and a
# revision that the local clone does not have fails closed instead of turning
# into an empty baseline.
#
# The pinned local tool artifacts under `.local/plan3-tools/` are not committed.
# They are copied into the export and their SHA-256 is printed; the tooling
# verifies the same digests itself, so a substituted artifact fails closed.
#
# Usage:
#   bash tests/Plan3/clean-export.sh --sha <sha> --base-ref <ref> \
#        [--target <dir>] [--with-browser] [--keep]
set -uo pipefail

# The correlation identifier of this run accompanies every closed error path.
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

# A closed error path of the export: a reason token, a safe detail and the
# correlation identifier of this run. No payload and no secret.
closed_error() {
  local reason="$1" detail="$2" code="${3:-1}"
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
git_dir=$(git -C "$root" rev-parse --absolute-git-dir) ||
  closed_error PLAN3_GIT_UNAVAILABLE "the local clone could not be resolved" 1

sha=""
base_ref=""
target=""
with_browser=0
keep=0

usage() {
  cat <<'EOF'
Usage: bash tests/Plan3/clean-export.sh --sha <sha> --base-ref <ref> \
         [--target <dir>] [--with-browser] [--keep]

  --sha <sha>        commit to export; it must exist in the local clone
  --base-ref <ref>   trusted base revision for the compatibility gate
  --target <dir>     export directory (default: a temporary directory)
  --with-browser     also run the browser suite against a test-owned stack
  --keep             keep the export directory after the run
EOF
}

argument_error() {
  usage >&2
  closed_error PLAN3_ARGUMENT_INVALID "$1" 2
}

mvp_review=0
while [ $# -gt 0 ]; do
  case "$1" in
    --sha) [ $# -ge 2 ] || argument_error "--sha requires a value"; sha="$2"; shift 2 ;;
    --base-ref) [ $# -ge 2 ] || argument_error "--base-ref requires a value"; base_ref="$2"; shift 2 ;;
    --target) [ $# -ge 2 ] || argument_error "--target requires a value"; target="$2"; shift 2 ;;
    --mvp-reviewed-policy) mvp_review=1; shift ;;
    --with-browser) with_browser=1; shift ;;
    --keep) keep=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) argument_error "unknown argument: $1" ;;
  esac
done
[ -n "$sha" ] || argument_error "--sha is required"
[ -n "$base_ref" ] || argument_error "--base-ref is required"

# The exported commit and the base revision must already exist in the local
# clone. A missing object is a closed error path, never an empty baseline.
for revision in "$sha" "$base_ref"; do
  if ! git -C "$root" cat-file -e "${revision}^{commit}" 2>/dev/null; then
    closed_error CONTRACT_BASE_UNAVAILABLE "$revision is not a commit in the local clone" 1
  fi
done
sha=$(git -C "$root" rev-parse "${sha}^{commit}")
base_sha=$(git -C "$root" rev-parse "${base_ref}^{commit}")

created_target=0
if [ -z "$target" ]; then
  target=$(mktemp -d "${TMPDIR:-/tmp}/plan3-export-XXXXXX") ||
    closed_error PLAN3_EXPORT_TARGET_INVALID "the export directory could not be created" 1
  created_target=1
else
  # The target must be a directory that is empty or does not exist. A mistyped
  # target must never delete an unrelated directory.
  if [ -e "$target" ]; then
    [ -d "$target" ] || closed_error PLAN3_EXPORT_TARGET_INVALID "the target is not a directory"
    resolved=$(cd "$target" && pwd -P) || closed_error PLAN3_EXPORT_TARGET_INVALID "the target cannot be resolved"
    case "$resolved" in
      /|"$HOME"|"$root")
        closed_error PLAN3_EXPORT_TARGET_INVALID "the target is a protected directory"
        ;;
    esac
    if [ -n "$(ls -A "$target" 2>/dev/null)" ]; then
      closed_error PLAN3_EXPORT_TARGET_INVALID "the target directory is not empty"
    fi
    rmdir "$target" 2>/dev/null || true
  fi
  mkdir -p "$target" || closed_error PLAN3_EXPORT_TARGET_INVALID "the target cannot be created"
  created_target=1
fi

cleanup() {
  if [ "$created_target" -eq 1 ] && [ "$keep" -eq 0 ]; then
    scratch_remove "$target"
  fi
}
trap cleanup EXIT

git -C "$root" archive --format=tar "$sha" | tar -x -C "$target" ||
  closed_error PLAN3_EXPORT_ARCHIVE_FAILED "the commit could not be exported" 1

# Read-only Git object context: the export can resolve revisions that the local
# clone already has, and it cannot create or change any object.
mkdir -p "$target/.git/objects/info" "$target/.git/refs/heads"
printf '[core]\n\trepositoryformatversion = 0\n\tbare = false\n' > "$target/.git/config"
printf '%s\n' "$sha" > "$target/.git/HEAD"
printf '%s/objects\n' "$git_dir" > "$target/.git/objects/info/alternates"

# The pinned local tool artifacts are not committed; copy them in unchanged.
mkdir -p "$target/.local/plan3-tools"
for artifact in "$root"/.local/plan3-tools/*; do
  [ -e "$artifact" ] || continue
  cp -R "$artifact" "$target/.local/plan3-tools/"
done

printf 'export sha: %s\n' "$sha"
printf 'base sha: %s\n' "$base_sha"
printf 'export directory: %s\n' "$target"
printf 'correlation: %s\n' "$correlation"
# The digests of the pinned local tool artifacts that the tooling verifies
# itself: the Corvus.Json.Cli package and the oasdiff executable.
for artifact in "$target"/.local/plan3-tools/*; do
  [ -f "$artifact" ] || continue
  printf 'local artifact: %s %s\n' "$(basename "$artifact")" \
    "$(sha256sum < "$artifact" | cut -d' ' -f1)"
done
# The pinned oasdiff executable is located through the policy that the tooling
# verifies itself, so a version bump follows the policy and this line cannot
# disappear silently.
oasdiff_policy="$target/tools/contracts/oasdiff.json"
[ -f "$oasdiff_policy" ] || oasdiff_policy="$root/tools/contracts/oasdiff.json"
oasdiff_relative=""
if [ -f "$oasdiff_policy" ]; then
  oasdiff_relative=$(python3 - "$oasdiff_policy" <<'PY' 2>/dev/null
import json
import sys

try:
    policy = json.load(open(sys.argv[1]))
except Exception:
    sys.exit(1)
print(policy.get('binaryPath', ''))
PY
)
fi
if [ -n "$oasdiff_relative" ]; then
  if [ ! -f "$target/$oasdiff_relative" ]; then
    closed_error PLAN3_EXPORT_ARTIFACT_MISSING \
      "the pinned oasdiff executable $oasdiff_relative is not in the export" 1
  fi
  printf 'local artifact: %s %s\n' "$oasdiff_relative" \
    "$(sha256sum < "$target/$oasdiff_relative" | cut -d' ' -f1)"
else
  # No policy pins an oasdiff executable: the line stays visible with the
  # discovered artifact instead of vanishing.
  oasdiff_discovered=""
  for candidate in "$target"/.local/plan3-tools/oasdiff-*/oasdiff; do
    [ -f "$candidate" ] && { oasdiff_discovered="$candidate"; break; }
  done
  if [ -n "$oasdiff_discovered" ]; then
    printf 'local artifact: %s %s\n' \
      ".local/plan3-tools/$(basename "$(dirname "$oasdiff_discovered")")/oasdiff" \
      "$(sha256sum < "$oasdiff_discovered" | cut -d' ' -f1)"
  else
    printf 'local artifact: oasdiff not pinned by this commit\n'
  fi
fi

gate_args=(--base-ref "$base_ref")
[ "$mvp_review" -eq 1 ] && gate_args+=(--mvp-reviewed-policy)
[ "$with_browser" -eq 1 ] && gate_args+=(--with-browser)
( cd "$target" && bash "$target/scripts/check-plan3.sh" "${gate_args[@]}" )
status=$?
printf 'clean export aggregate: exit %s\n' "$status"
exit "$status"
