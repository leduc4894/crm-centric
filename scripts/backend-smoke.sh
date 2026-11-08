#!/usr/bin/env bash
set +x
set -euo pipefail
umask 077
api_pid=''
correlation_id=$(cat /proc/sys/kernel/random/uuid)
fail() { printf 'reasonToken=BACKEND_SMOKE_FAILED correlationId=%s\n' "$correlation_id" >&2; exit 1; }
cleanup() {
  if [[ -n "$api_pid" ]]; then
    kill "$api_pid" 2>/dev/null || true
    wait "$api_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT
trap fail ERR
trap 'exit 130' INT
trap 'exit 143' TERM
cd "$(dirname "${BASH_SOURCE[0]}")/.."
# Values are injected by the caller, never supplied as command arguments.
[[ -n "${SMOKE_MIGRATION_CONNECTION:-}" && -n "${SMOKE_APPLICATION_CONNECTION:-}" && -n "${Database__ApplicationRole:-}" ]] || fail
port=${SMOKE_PORT:-5080}
[[ "$port" =~ ^[0-9]{1,5}$ ]] && ((10#$port > 0 && 10#$port < 65536)) || fail
for prerequisite in dotnet curl timeout; do command -v "$prerequisite" >/dev/null || fail; done
# Build/locked restore are explicit prerequisites, not implicit dotnet run restores.
(
  export ConnectionStrings__Platform="$SMOKE_MIGRATION_CONNECTION"
  unset SMOKE_MIGRATION_CONNECTION SMOKE_APPLICATION_CONNECTION
  timeout --kill-after=5s 60s dotnet src/Platform.Migrator/bin/Release/net10.0/Platform.Migrator.dll
) >/dev/null 2>&1 || fail
(
  export ConnectionStrings__Platform="$SMOKE_APPLICATION_CONNECTION"
  export ASPNETCORE_URLS="http://127.0.0.1:$port"
  unset SMOKE_MIGRATION_CONNECTION SMOKE_APPLICATION_CONNECTION Database__ApplicationRole
  exec dotnet src/Platform.Api/bin/Release/net10.0/Platform.Api.dll
) >/dev/null 2>&1 &
api_pid=$!
unset SMOKE_MIGRATION_CONNECTION SMOKE_APPLICATION_CONNECTION Database__ApplicationRole
base="http://127.0.0.1:$port"
ready=false
deadline=$((SECONDS + 20))
while ((SECONDS < deadline)); do
  kill -0 "$api_pid" 2>/dev/null || fail
  live=$(curl --noproxy '*' --silent --fail --write-out '\n%{http_code}' --connect-timeout 1 --max-time 4 "$base/health/live" 2>/dev/null) || live=''
  health=$(curl --noproxy '*' --silent --fail --write-out '\n%{http_code}' --connect-timeout 1 --max-time 4 "$base/health/ready" 2>/dev/null) || health=''
  if [[ "$live" == $'{"status":"live"}\n200' && "$health" == $'{"status":"ready"}\n200' ]]; then ready=true; break; fi
  sleep 0.2
done
[[ "$ready" == true ]] || fail
kill -0 "$api_pid" 2>/dev/null || fail
printf 'SMOKE_PASS live=200 ready=200\n'
