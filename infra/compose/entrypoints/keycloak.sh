#!/usr/bin/env bash
set -euo pipefail
fail() {
    local correlation
    IFS= read -r correlation < /proc/sys/kernel/random/uuid
    printf '{"service":"keycloak","eventName":"configuration_failed","reasonToken":"LOCAL_CONFIGURATION_INVALID","correlationId":"%s"}\n' "${correlation//-/}" >&2
    exit 1
}
[[ $(id -u) != 0 && -z ${KC_DB_PASSWORD:-} && -z ${KC_BOOTSTRAP_ADMIN_PASSWORD:-} ]] || fail
# This image is an explicit loopback-only local profile, with one canonical URL.
KC_HOSTNAME=${KC_HOSTNAME:-http://localhost:8080}
[[ "$KC_HOSTNAME" =~ ^http://localhost:([1-9][0-9]{0,4})$ ]] || fail
[[ ${BASH_REMATCH[1]} -le 65535 ]] || fail
[[ ${KC_DB_USERNAME:-keycloak} == keycloak ]] || fail
[[ $# == 1 && $1 == start-dev || $# == 2 && $1 == start-dev && $2 == --import-realm ]] || fail
export KC_HOSTNAME
read_secret() {
    local file=$1
    [[ -f "$file" && ! -L "$file" && -r "$file" && -s "$file" ]] || return 1
    [[ $(stat -c %a -- "$file" 2>/dev/null) == 600 && $(stat -c %u -- "$file" 2>/dev/null) == $(id -u) ]] || return 1
    cat -- "$file" 2>/dev/null
}
if ! KC_DB_PASSWORD=$(read_secret "${KC_DB_PASSWORD_FILE:-/run/secrets/idp_db_password}"); then fail; fi
if ! KC_BOOTSTRAP_ADMIN_PASSWORD=$(read_secret "${KC_BOOTSTRAP_ADMIN_PASSWORD_FILE:-/run/secrets/idp_admin_password}"); then fail; fi
[[ -n "$KC_DB_PASSWORD" && -n "$KC_BOOTSTRAP_ADMIN_PASSWORD" ]] || fail
export KC_DB_PASSWORD KC_BOOTSTRAP_ADMIN_PASSWORD
unset KC_DB_PASSWORD_FILE KC_BOOTSTRAP_ADMIN_PASSWORD_FILE
exec /opt/keycloak/bin/kc.sh "$@"
