# Sourced by the official postgres entrypoint as postgres, on empty storage only.
if [[ ${LOCAL_DATABASE_SERVICE:-crm-db} == idp-db ]]; then
    idp_init_fail() {
        local correlation
        IFS= read -r correlation < /proc/sys/kernel/random/uuid
        printf '{"service":"idp-db","eventName":"bootstrap_failed","reasonToken":"LOCAL_BOOTSTRAP_FAILED","correlationId":"%s"}\n' "${correlation//-/}" >&2
        exit 1
    }
    [[ -n ${LOCAL_IDP_DB_PASSWORD:-} ]] || idp_init_fail
    # Standard-conforming SQL literal, never a psql variable/argv value. Both
    # server statement logging and psql diagnostics are suppressed for this SQL.
    idp_literal=${LOCAL_IDP_DB_PASSWORD//\'/\'\'}
    if ! psql --username "$POSTGRES_USER" --dbname postgres --set ON_ERROR_STOP=1 >/dev/null 2>&1 <<SQL
SET standard_conforming_strings = on;
CREATE ROLE keycloak LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '$idp_literal';
CREATE DATABASE keycloak OWNER keycloak;
REVOKE ALL ON DATABASE keycloak FROM PUBLIC;
SQL
    then idp_init_fail; fi
    if ! psql --username "$POSTGRES_USER" --dbname keycloak --set ON_ERROR_STOP=1 >/dev/null 2>&1 <<'SQL'
REVOKE ALL ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO keycloak;
SQL
    then idp_init_fail; fi
    unset LOCAL_IDP_DB_PASSWORD idp_literal
    unset -f idp_init_fail
fi
