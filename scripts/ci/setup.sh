#!/usr/bin/env bash
# GitHub Ubuntu 24.04 only. Provision exact Docker engine; no credential output.
set -euo pipefail
correlation=$(cat /proc/sys/kernel/random/uuid)
trap 'printf "{\"reasonToken\":\"CI_SETUP_FAILED\",\"correlationId\":\"%s\"}\\n" "$correlation"; exit 1' ERR
# Setup actions provision .NET/Node/Python at exact versions. npm is locked here.
npm install --global npm@11.19.1 >/dev/null 2>&1
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /tmp/ci-docker.asc
# The apt repository metadata and packages are verified using Docker's release key.
fingerprint=$(gpg --show-keys --with-colons /tmp/ci-docker.asc 2>/dev/null | awk -F: '$1=="fpr" {print $10; exit}')
[[ "$fingerprint" == 9DC858229FC7DD38854AE2D88D81803C0EBFCD88 ]]
sudo cp /tmp/ci-docker.asc /etc/apt/keyrings/docker.asc
printf '%s\n' 'deb [arch=amd64 signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu noble stable' | sudo tee /etc/apt/sources.list.d/docker.list >/dev/null
sudo apt-get update -qq >/dev/null 2>&1
sudo apt-get install -y --allow-downgrades docker-ce=5:29.9.3-1~ubuntu.24.04~noble docker-ce-cli=5:29.9.3-1~ubuntu.24.04~noble docker-compose-plugin=5.7.0-1~ubuntu.24.04~noble containerd.io=2.4.2-3~ubuntu.24.04~noble >/dev/null 2>&1
sudo systemctl restart docker
python -m pip install -r requirements-contracts.lock >/dev/null 2>&1
python -m pip check >/dev/null 2>&1
python -m scripts.ci.provision_contracts
if [[ "${2:-hardening}" != mvp || "${1:-}" == security ]]; then
  python -m scripts.ci.provision_scanners --output .local/ci-scanners
  python -m scripts.ci.provision_scanners --kind gitleaks --output .local/ci-scanners
fi
if [[ "${1:-}" == security ]]; then
  "${CI_SEMGREP_PYTHON:?}" -m venv .local/ci-scanners/semgrep >/dev/null 2>&1
  .local/ci-scanners/semgrep/bin/python -m pip install --require-hashes --only-binary=:all: -r tools/ci/semgrep-requirements.lock >/dev/null 2>&1
  .local/ci-scanners/semgrep/bin/python -m pip check >/dev/null 2>&1
fi
case "${1:-}" in
  contracts-sdk|web-browser)
    npm ci --ignore-scripts --no-audit --no-fund >/dev/null 2>&1
    npm exec --workspace apps/crm-web -- playwright install --with-deps chromium >/dev/null 2>&1
    ;;
esac
