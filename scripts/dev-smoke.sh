#!/usr/bin/env bash
set -euo pipefail
exec python3 "$(dirname -- "${BASH_SOURCE[0]}")/local_stack.py" smoke
