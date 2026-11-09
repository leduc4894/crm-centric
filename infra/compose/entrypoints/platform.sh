#!/usr/bin/env bash
set -euo pipefail
case "${1:-}" in
    api) service=crm-api; assembly=Platform.Api.dll ;;
    migrator) service=crm-migrate; assembly=Platform.Migrator.dll ;;
    *) exit 1 ;;
esac
shift
fail() {
    local correlation
    IFS= read -r correlation < /proc/sys/kernel/random/uuid
    printf '{"service":"%s","eventName":"configuration_failed","reasonToken":"LOCAL_CONFIGURATION_INVALID","correlationId":"%s"}\n' "$service" "${correlation//-/}" >&2
    exit 1
}
# Do not allow .NET command-line configuration to replace the mounted file.
[[ $# -eq 0 ]] || fail
file=${PLATFORM_CONNECTION_FILE:-/run/secrets/platform_connection}
[[ $(id -u) != 0 && -f "$file" && ! -L "$file" && -r "$file" && -s "$file" ]] || fail
[[ $(stat -c %a -- "$file" 2>/dev/null) == 600 && $(stat -c %u -- "$file" 2>/dev/null) == $(id -u) ]] || fail
[[ -z ${ConnectionStrings__Platform:-} ]] || fail
if ! connection=$(cat -- "$file" 2>/dev/null); then fail; fi
[[ -n "$connection" ]] || fail
export ConnectionStrings__Platform="$connection"
unset connection PLATFORM_CONNECTION_FILE
exec dotnet "$assembly"
