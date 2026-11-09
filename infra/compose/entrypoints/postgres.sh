#!/usr/bin/env bash
set -euo pipefail
fail() {
    local correlation
    IFS= read -r correlation < /proc/sys/kernel/random/uuid
    printf '{"service":"%s","eventName":"configuration_failed","reasonToken":"LOCAL_CONFIGURATION_INVALID","correlationId":"%s"}\n' "$service" "${correlation//-/}" >&2
    exit 1
}
service=${LOCAL_DATABASE_SERVICE:-crm-db}
case "$service" in crm-db|idp-db) ;; *) service=crm-db; fail ;; esac
file=${POSTGRES_PASSWORD_FILE:-}
[[ -n "$file" && -f "$file" && ! -L "$file" && -r "$file" && -s "$file" ]] || fail
[[ $(stat -c %a -- "$file" 2>/dev/null) == 600 && -z ${POSTGRES_PASSWORD:-} ]] || fail
# The pinned official entrypoint reads POSTGRES_PASSWORD_FILE once as root,
# unsets *_FILE, and retains its canonical initdb and gosu postgres handoff.
# Never read it again after dropping to UID 999, which cannot open host 0600.
# Credentials appear in CREATE ROLE SQL only during the administrative bootstrap.
# Keep vendor startup/error logs operational without recording those statements.
if [[ ${1:-} == postgres ]]; then
    exec /usr/local/bin/docker-entrypoint.sh "$@" -c log_statement=none -c log_min_error_statement=panic -c log_min_duration_statement=-1 -c log_min_duration_sample=-1 -c log_transaction_sample_rate=0 -c log_duration=off
fi
exec /usr/local/bin/docker-entrypoint.sh "$@"
