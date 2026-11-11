"""Availability probes for the prerequisites of the Plan 3 contract tests.

The Plan 3 contract tests exercise pinned local tooling that a CI checkout does
not provision: the pinned oasdiff binary and the pinned corvus.json.cli package
below the git-ignored ``.local/plan3-tools`` directory, an installed
``node_modules`` with a resolvable ``typescript`` module, and Docker with the
pinned .NET image.

Every probe answers with an ``(available, reason)`` pair. The reason names
exactly what is missing, so a skipped test states why it did not run instead of
vanishing silently from the report. The probes read the pinned versions and
digests from ``tools/contracts/toolchain.json`` and ``tools/contracts/oasdiff.json``;
no version and no digest is hardcoded here, so a bump of the pinned tooling
follows through the policy files.

The module name does not match the ``test*.py`` discovery pattern, so unittest
discovery does not collect it as a test module. It is imported as
``import prerequisites`` because ``tests/Contract`` has no ``__init__.py`` and
unittest discovery puts that directory on ``sys.path``.

A probe never raises: an unusable environment is reported as "not available"
with a reason, never as a test error.
"""

import functools
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
TOOLCHAIN = ROOT / 'tools/contracts/toolchain.json'
OASDIFF_POLICY = ROOT / 'tools/contracts/oasdiff.json'
TOOLS = ROOT / '.local/plan3-tools'


def _load(path):
    """Reads a policy file. A missing or broken policy is an empty policy."""
    try:
        return json.loads(path.read_text(encoding='utf-8'))
    except (OSError, ValueError):
        return {}


@functools.lru_cache(maxsize=None)
def _toolchain():
    return _load(TOOLCHAIN)


@functools.lru_cache(maxsize=None)
def _policy():
    return _load(OASDIFF_POLICY)


def _run(command, cwd=None, timeout=120):
    """Runs a probe command. A failure to start is a failure, never an error."""
    try:
        return subprocess.run(command, cwd=cwd, capture_output=True, text=True, timeout=timeout)
    except (OSError, subprocess.SubprocessError):
        return None


def _succeeded(command, cwd=None, timeout=120):
    proc = _run(command, cwd=cwd, timeout=timeout)
    return proc is not None and proc.returncode == 0


def oasdiff_available():
    """The pinned oasdiff binary exists and actually runs."""
    policy = _policy()
    version = policy.get('version', 'unknown')
    relative = policy.get('binaryPath', '')
    binary = (ROOT / relative) if relative else None
    if binary is None or not binary.is_file():
        return False, f'the pinned oasdiff {version} is missing from .local/plan3-tools'
    if not os.access(str(binary), os.X_OK):
        return False, f'the pinned oasdiff {version} in .local/plan3-tools is not executable'
    if not _succeeded([str(binary), '--version'], timeout=60):
        return False, f'the pinned oasdiff {version} in .local/plan3-tools does not run'
    return True, ''


def corvus_package_available():
    """The pinned corvus.json.cli package exists and matches the pinned digest."""
    config = _toolchain()
    version = config.get('corvusCli', 'unknown')
    package = TOOLS / f'corvus.json.cli.{version}.nupkg'
    if not package.is_file():
        return False, f'the pinned corvus.json.cli {version} package is missing from .local/plan3-tools'
    expected = config.get('corvusPackageSha256', '')
    if not expected:
        return False, 'the pinned corvus.json.cli SHA-256 is missing from tools/contracts/toolchain.json'
    try:
        digest = hashlib.sha256(package.read_bytes()).hexdigest()
    except OSError:
        return False, f'the pinned corvus.json.cli {version} package in .local/plan3-tools cannot be read'
    if digest != expected:
        return False, (
            f'the pinned corvus.json.cli {version} package in .local/plan3-tools '
            'does not match the pinned SHA-256'
        )
    return True, ''


def typescript_available():
    """A resolvable typescript module in node_modules, from the repository root."""
    node = shutil.which('node')
    if node is None:
        return False, 'the node toolchain is not on the PATH'
    if not _succeeded([node, '--version'], timeout=60):
        return False, 'the node toolchain is not usable'
    if not _succeeded([node, '-e', "require.resolve('typescript')"], cwd=str(ROOT), timeout=120):
        return False, 'typescript is not resolvable from node_modules'
    return True, ''


def docker_available():
    """A reachable docker daemon that holds the pinned .NET image."""
    docker = shutil.which('docker')
    if docker is None:
        return False, 'docker is not on the PATH'
    if not _succeeded([docker, 'info'], timeout=120):
        return False, 'the docker daemon is not reachable'
    image = _toolchain().get('dotnetImage', '')
    if not image:
        return False, 'the pinned .NET image is missing from tools/contracts/toolchain.json'
    if not _succeeded([docker, 'image', 'inspect', image], timeout=120):
        return False, f'the pinned .NET image {image} is missing from the local docker image store'
    return True, ''


def _combine(*pairs):
    """Joins the reasons of the pairs that are not available."""
    reasons = [reason for available, reason in pairs if not available]
    return '; '.join(reasons)


def _all(*pairs):
    """The availability of every pair, with the joined reasons of the missing ones."""
    return all(available for available, _ in pairs), _combine(*pairs)


OASDIFF = oasdiff_available()
CORVUS = corvus_package_available()
TYPESCRIPT = typescript_available()
DOCKER = docker_available()

# The surface comparator needs the typescript module; the .NET surface scan
# needs the docker daemon with the pinned .NET image.
SDK_SURFACE = _all(TYPESCRIPT, DOCKER)

# The packaged consumers need the installed node_modules (typescript and the
# compiler) and the docker daemon with the pinned .NET image. The warm npm cache
# and the NuGet flat container are deliberately not probed: a probe of a
# transient service would turn a real outage into a silent skip.
CONSUMERS = _all(TYPESCRIPT, DOCKER)
