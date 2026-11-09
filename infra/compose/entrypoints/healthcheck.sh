#!/usr/bin/env bash
set -euo pipefail
case "${1:-api}" in
    api) port=8080; paths=(/health/live /health/ready) ;;
    keycloak) port=9000; paths=(/health/ready) ;;
    *) exit 1 ;;
esac
# Tools are native to each pinned image. A single four-second budget bounds
# both API checks. URLs are fixed loopback endpoints of this very container.
exec timeout --signal=KILL 4 bash -c '
    port=$1; shift
    for path in "$@"; do
        exec 3<>/dev/tcp/127.0.0.1/"$port" || exit 1
        printf "GET %s HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" "$path" >&3
        IFS= read -r status <&3 || exit 1
        [[ "$status" == HTTP/1.?\ 200\ * ]] || exit 1
        exec 3<&-; exec 3>&-
    done
' health-probe "$port" "${paths[@]}" >/dev/null 2>&1
