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

root=$(cd "$(dirname "$0")/../.." && pwd) || exit 1
git_dir=$(git -C "$root" rev-parse --absolute-git-dir) || exit 1

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
  printf 'PLAN3_ARGUMENT_INVALID: %s\n' "$1" >&2
  usage >&2
  exit 2
}

while [ $# -gt 0 ]; do
  case "$1" in
    --sha) [ $# -ge 2 ] || argument_error "--sha requires a value"; sha="$2"; shift 2 ;;
    --base-ref) [ $# -ge 2 ] || argument_error "--base-ref requires a value"; base_ref="$2"; shift 2 ;;
    --target) [ $# -ge 2 ] || argument_error "--target requires a value"; target="$2"; shift 2 ;;
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
    printf 'CONTRACT_BASE_UNAVAILABLE: %s is not a commit in the local clone\n' "$revision" >&2
    exit 1
  fi
done
sha=$(git -C "$root" rev-parse "${sha}^{commit}")
base_sha=$(git -C "$root" rev-parse "${base_ref}^{commit}")

created_target=0
if [ -z "$target" ]; then
  target=$(mktemp -d "${TMPDIR:-/tmp}/plan3-export-XXXXXX") || exit 1
  created_target=1
else
  rm -rf "$target"
  mkdir -p "$target" || exit 1
  created_target=1
fi

cleanup() {
  if [ "$created_target" -eq 1 ] && [ "$keep" -eq 0 ]; then
    rm -rf "$target"
  fi
}
trap cleanup EXIT

git -C "$root" archive --format=tar "$sha" | tar -x -C "$target" || exit 1

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
for artifact in "$target"/.local/plan3-tools/*; do
  [ -f "$artifact" ] || continue
  printf 'local artifact: %s %s\n' "$(basename "$artifact")" "$(sha256sum < "$artifact" | cut -d' ' -f1)"
done

gate_args=(--base-ref "$base_ref")
[ "$with_browser" -eq 1 ] && gate_args+=(--with-browser)
( cd "$target" && bash "$target/scripts/check-plan3.sh" "${gate_args[@]}" )
status=$?
printf 'clean export aggregate: exit %s\n' "$status"
exit "$status"
