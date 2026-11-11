#!/usr/bin/env bash
# The rm binary that unlinks a scratch directory of this repository.
#
# A scratch directory that a script created itself, minutes ago, must be
# unlinked. It must never be moved into a recoverable trash. Some agent
# runtimes put a recoverable-delete shim named `rm` on the PATH; on Linux that
# shim moves its target into the trash of the user (the XDG Trash spec)
# instead of unlinking it. A multi-gigabyte scratch tree is then copied rather
# than freed, and the disk of the host fills up.
#
# The helper therefore resolves the rm binary to an ABSOLUTE path — /bin/rm,
# then /usr/bin/rm — so that neither the PATH nor a shim on it can intercept a
# scratch deletion. Only a host without both absolute paths falls back to the
# rm of the PATH, and the fallback is resolved to an absolute path as well.
#
# The file prints nothing when it is sourced and writes nothing to stdout.
#
# Usage: . "$root/scripts/lib/scratch-cleanup.sh"

# SCRATCH_RM is the absolute path of the rm binary that unlinks. It is empty
# only on a host without any rm binary at all.
SCRATCH_RM="$(
  scratch_rm_resolved=""
  for scratch_rm_candidate in /bin/rm /usr/bin/rm; do
    if [ -x "$scratch_rm_candidate" ]; then
      scratch_rm_resolved="$scratch_rm_candidate"
      break
    fi
  done
  if [ -z "$scratch_rm_resolved" ]; then
    # The last resort on a host without /bin/rm and without /usr/bin/rm: the
    # rm of the PATH, resolved to an absolute path here as well, so that the
    # resolved binary is never a bare name. An empty entry of the PATH is
    # skipped: it would name the current directory, which is never a place to
    # take a binary from.
    scratch_rm_ifs_saved="$IFS"
    IFS=:
    set -f
    set -- ${PATH:-}
    set +f
    IFS="$scratch_rm_ifs_saved"
    for scratch_rm_directory in "$@"; do
      if [ -n "$scratch_rm_directory" ] && [ -x "$scratch_rm_directory/rm" ]; then
        scratch_rm_resolved="$scratch_rm_directory/rm"
        break
      fi
    done
  fi
  case "$scratch_rm_resolved" in
    /*) printf '%s' "$scratch_rm_resolved" ;;
  esac
  exit 0
)"

# Removes a scratch path with the resolved binary. The end-of-options separator
# keeps a target that starts with a dash from being read as an option.
scratch_remove() {
  if [ -z "${SCRATCH_RM:-}" ]; then
    printf 'scratch cleanup: no rm binary could be resolved\n' >&2
    return 1
  fi
  "$SCRATCH_RM" -rf -- "$@"
}
