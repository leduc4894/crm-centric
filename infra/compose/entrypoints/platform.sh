#!/usr/bin/env bash
set -euo pipefail
case "${1:-}" in
    api) service=crm-api; assembly=Platform.Api.dll ;;
    migrator) service=crm-migrate; assembly=Platform.Migrator.dll ;;
    *) exit 1 ;;
esac
shift
if [[ "$service" == crm-migrate && $# -eq 1 && "$1" == --local-bootstrap ]]; then service=crm-bootstrap; fi
fail() {
    local correlation
    IFS= read -r correlation < /proc/sys/kernel/random/uuid
    printf '{"service":"%s","eventName":"configuration_failed","reasonToken":"LOCAL_CONFIGURATION_INVALID","correlationId":"%s"}\n' "$service" "${correlation//-/}" >&2
    exit 1
}
read_secret() {
    local file=$1
    [[ $(id -u) != 0 && -f "$file" && ! -L "$file" && -r "$file" && -s "$file" ]] || fail
    [[ $(stat -c %a -- "$file" 2>/dev/null) == 600 && $(stat -c %u -- "$file" 2>/dev/null) == $(id -u) ]] || fail
    if ! value=$(cat -- "$file" 2>/dev/null); then fail; fi
    [[ -n "$value" ]] || fail
}
if [[ "$service" == crm-bootstrap ]]; then
    [[ -z ${LocalBootstrap__AdminConnectionString:-} && -z ${ConnectionStrings__Platform:-} ]] || fail
    [[ -z ${LocalBootstrap__MigratorSecretFile:-} && -z ${LocalBootstrap__ApplicationSecretFile:-} ]] || fail
    read_secret "${LOCAL_BOOTSTRAP_ADMIN_CONNECTION_FILE:-/run/secrets/crm_admin_connection}"
    export LocalBootstrap__AdminConnectionString="$value"
    migrator_file=${LOCAL_BOOTSTRAP_MIGRATOR_SECRET_FILE:-/run/secrets/crm_migrator_password}
    application_file=${LOCAL_BOOTSTRAP_APPLICATION_SECRET_FILE:-/run/secrets/crm_app_password}
    read_secret "$migrator_file"
    read_secret "$application_file"
    export LocalBootstrap__MigratorSecretFile="$migrator_file"
    export LocalBootstrap__ApplicationSecretFile="$application_file"
    unset value migrator_file application_file LOCAL_BOOTSTRAP_ADMIN_CONNECTION_FILE LOCAL_BOOTSTRAP_MIGRATOR_SECRET_FILE LOCAL_BOOTSTRAP_APPLICATION_SECRET_FILE
    exec dotnet "$assembly" --local-bootstrap
fi
# Do not allow .NET command-line configuration to replace the mounted file.
[[ $# -eq 0 ]] || fail
[[ -z ${LocalBootstrap__AdminConnectionString:-} ]] || fail
read_secret "${PLATFORM_CONNECTION_FILE:-/run/secrets/platform_connection}"
[[ -z ${ConnectionStrings__Platform:-} ]] || fail
export ConnectionStrings__Platform="$value"
unset value PLATFORM_CONNECTION_FILE
exec dotnet "$assembly"
