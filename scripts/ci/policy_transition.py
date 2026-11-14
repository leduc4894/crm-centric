"""Trusted-workflow API, deliberately unavailable from candidate CLI arguments.

The caller must provision trusted_key (signature verifier and Git snapshots) from
an independent pinned workflow. This module neither discovers keys nor imports
candidate evaluators. Semantic evaluation belongs in a separate unprivileged job.
"""
from dataclasses import dataclass
import hashlib
import json
import os
import subprocess
from pathlib import PurePosixPath
import re
import time
import uuid


class TransitionError(Exception):
    def __init__(self, reason_token='CI_POLICY_TRANSITION_INVALID'):
        self.reason_token = reason_token
        self.correlation_id = str(uuid.uuid4())
        super().__init__(reason_token)


@dataclass(frozen=True)
class CompatibilityResult:
    reason: str
    evaluations: tuple = ()
    attestation_identity: str | None = None
    correlation_id: str | None = None
    old_policy_hashes: tuple = ()
    new_policy_hashes: tuple = ()
    base_contract_hashes: tuple = ()
    candidate_contract_hashes: tuple = ()


@dataclass(frozen=True)
class VerifiedTransition:
    identity: str
    revision: object
    old_policy: tuple
    new_policy: tuple
    base_contracts: tuple
    candidate_contracts: tuple
    old_hashes: tuple
    new_hashes: tuple
    expires_at: int


def _snapshot(items):
    result = tuple(sorted(items))
    names = set()
    for name, content in result:
        path = PurePosixPath(name)
        if (not isinstance(content, bytes) or path.is_absolute() or '..' in path.parts
                or str(path) != name or not name or name in names):
            raise TransitionError()
        names.add(name)
    if not result:
        raise TransitionError()
    return result


def _hashes(snapshot):
    return {name: hashlib.sha256(content).hexdigest() for name, content in snapshot}


@dataclass(frozen=True)
class GitSnapshotCapability:
    """Locally read immutable objects; caller still provisions verifier and scope.

    No worktree reads, lazy fetch, replace objects or candidate script execution.
    Construction is a trusted-coordinator operation, not proof of independent CI.
    """
    verify: object
    candidate_tree: str
    merge_method: str
    old_policy: tuple
    new_policy: tuple
    base_contracts: tuple
    candidate_contracts: tuple

    @classmethod
    def from_git(cls, repo, revision, verifier, merge_method, policy_paths):
        try:
            if not callable(verifier) or merge_method not in ('merge', 'squash', 'rebase'):
                raise TransitionError()
            paths = set(policy_paths)
            if not paths:
                raise TransitionError()
            for path in paths:
                _snapshot(((path, b''),))
            def git(*args):
                proc = subprocess.run(['git', '--no-replace-objects', *args], cwd=repo,
                                      capture_output=True, timeout=15,
                                      env={**os.environ, 'GIT_NO_LAZY_FETCH': '1',
                                           'GIT_TERMINAL_PROMPT': '0'})
                if proc.returncode:
                    raise TransitionError()
                return proc.stdout
            def snapshot(commit):
                if not isinstance(commit, str) or not re.fullmatch('[0-9a-f]{40}', commit):
                    raise TransitionError()
                if git('cat-file', '-t', commit).strip() != b'commit':
                    raise TransitionError()
                policy, contracts = [], []
                for entry in git('ls-tree', '-r', '-z', commit).split(b'\0'):
                    if not entry:
                        continue
                    meta, name = entry.split(b'\t', 1)
                    name = name.decode('utf-8')
                    if name not in paths and not name.startswith('docs/contracts/'):
                        continue
                    mode, kind, oid = meta.split()
                    if mode not in (b'100644', b'100755') or kind != b'blob':
                        raise TransitionError()
                    content = git('cat-file', 'blob', oid.decode('ascii'))
                    if name in paths:
                        policy.append((name, content))
                    if name.startswith('docs/contracts/'):
                        contracts.append((name, content))
                return tuple(sorted(policy)), _snapshot(contracts)
            old_policy, base_contracts = snapshot(revision.base_sha)
            new_policy, candidate_contracts = snapshot(revision.candidate_sha)
            if {name for name, _ in old_policy + new_policy} != paths:
                raise TransitionError()
            return cls(verifier, git('rev-parse', '--verify', revision.candidate_sha + '^{tree}').decode('ascii').strip(),
                       merge_method, old_policy, new_policy, base_contracts, candidate_contracts)
        except TransitionError:
            raise
        except Exception:
            raise TransitionError() from None


def verify_transition(attestation, trusted_key, revision, old_hashes, new_hashes):
    """Verify exact signed approval using an externally supplied trust capability.

    trusted_key.verify(canonical_payload_bytes, signature) must return literal
    True. The same capability supplies read-only snapshots from validated Git
    objects, actual candidate_tree and reviewed merge_method, never PR settings.
    No environment variables or candidate approval files enable this boundary.
    """
    if trusted_key is None:
        raise TransitionError('CI_POLICY_TRANSITION_BLOCKED')
    try:
        if set(attestation) != {'payload', 'signature'}:
            raise TransitionError()
        payload = attestation['payload']
        raw = json.dumps(payload, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()
        if trusted_key.verify(raw, attestation['signature']) is not True:
            raise TransitionError()
        expected = {'version', 'identity', 'base_sha', 'head_sha', 'candidate_sha',
                    'candidate_tree', 'merge_method', 'event', 'old_hashes', 'new_hashes',
                    'files', 'adr', 'consumer_tests', 'reviewer', 'expires_at'}
        if set(payload) != expected or payload['version'] != 1:
            raise TransitionError()
        for field in ('identity', 'adr', 'reviewer'):
            if not isinstance(payload[field], str) or not payload[field].strip():
                raise TransitionError()
        tests = payload['consumer_tests']
        if not isinstance(tests, list) or not tests or any(not isinstance(x, str) or not x.strip() for x in tests):
            raise TransitionError()
        expiry = payload['expires_at']
        if type(expiry) is not int or expiry <= time.time():
            raise TransitionError()
        for field in ('base_sha', 'head_sha', 'candidate_sha'):
            if payload[field] != getattr(revision, field):
                raise TransitionError()
        tree = trusted_key.candidate_tree
        if not isinstance(tree, str) or not re.fullmatch('[0-9a-f]{40}', tree) or tree == '0'*40:
            raise TransitionError()
        if payload['candidate_tree'] != tree:
            raise TransitionError()
        if (payload['merge_method'] != trusted_key.merge_method
                or payload['merge_method'] not in ('merge', 'squash', 'rebase')
                or payload['event'] != ('pull_request' if revision.merge_sha else 'push')):
            raise TransitionError()
        old_policy, new_policy = _snapshot(trusted_key.old_policy), _snapshot(trusted_key.new_policy)
        if (payload['old_hashes'] != old_hashes or payload['new_hashes'] != new_hashes
                or _hashes(old_policy) != old_hashes or _hashes(new_policy) != new_hashes
                or payload['files'] != sorted(set(old_hashes) | set(new_hashes))):
            raise TransitionError()
        return VerifiedTransition(payload['identity'], revision, old_policy, new_policy,
                                  _snapshot(trusted_key.base_contracts),
                                  _snapshot(trusted_key.candidate_contracts),
                                  tuple(sorted(old_hashes.items())), tuple(sorted(new_hashes.items())), expiry)
    except TransitionError:
        raise
    except Exception:
        # Verifier adapters may fail with arbitrary diagnostics. Never expose them.
        raise TransitionError() from None


def evaluate_transition(transition, old_evaluator, new_evaluator):
    """Unprivileged phase: both immutable policies evaluate identical contracts.

    Evaluators are independently pinned runners, never arbitrary PR callbacks in
    the privileged verifier. Any error or non-compatible result closes the gate.
    """
    if not isinstance(transition, VerifiedTransition) or transition.expires_at <= time.time():
        raise TransitionError()
    outcomes = []
    for policy, evaluator in ((transition.old_policy, old_evaluator), (transition.new_policy, new_evaluator)):
        try:
            outcome = evaluator(policy, transition.base_contracts, transition.candidate_contracts)
            reason = outcome.reason if isinstance(outcome, CompatibilityResult) else 'CONTRACT_TOOL_ERROR'
            if reason not in {'CONTRACT_COMPATIBLE', 'CONTRACT_BREAKING', 'SECURITY_REVIEW_REQUIRED',
                              'SCHEMA_COMPATIBILITY_REVIEW_REQUIRED', 'SDK_SURFACE_INCOMPATIBLE'}:
                reason = 'CONTRACT_TOOL_ERROR'
        except Exception:
            reason = 'CONTRACT_TOOL_ERROR'
        outcomes.append(reason)
    reason = 'CONTRACT_COMPATIBLE' if all(x == 'CONTRACT_COMPATIBLE' for x in outcomes) else 'CI_POLICY_TRANSITION_INCOMPATIBLE'
    return CompatibilityResult(reason, tuple(outcomes), transition.identity, str(uuid.uuid4()),
                               transition.old_hashes, transition.new_hashes,
                               tuple(sorted(_hashes(transition.base_contracts).items())),
                               tuple(sorted(_hashes(transition.candidate_contracts).items())))


@dataclass(frozen=True)
class Plan3SnapshotEvaluator:
    """Actual Plan 3 semantic runner, for the unprivileged evaluation phase only.

    The baseline generator/consumer policy is fixed across both evaluations.
    Each candidate policy is materialized with the same real contract pair.
    No verifier calls this runner; source scripts execute only in this separate
    unprivileged phase. tool_root supplies already provisioned checksum-pinned
    assets and is never inferred from approval files.
    """
    baseline_policy: tuple
    tool_root: object
    base_sha: str

    def __call__(self, policy, base_contracts, candidate_contracts):
        from pathlib import Path
        import shutil
        import tempfile
        import sys
        try:
            with tempfile.TemporaryDirectory(prefix='crm-transition-semantic-') as tmp:
                root = Path(tmp)
                def materialize(destination, files):
                    for name, content in _snapshot(files):
                        target = destination / name
                        target.parent.mkdir(parents=True, exist_ok=True)
                        target.write_bytes(content)
                old_source, source, candidate = root / 'old-policy', root / 'policy', root / 'candidate'
                materialize(old_source, self.baseline_policy)
                materialize(source, policy)
                materialize(candidate, candidate_contracts)
                materialize(root / 'base-contracts', base_contracts)
                if any(not name.startswith('docs/contracts/') for name, _ in base_contracts + candidate_contracts):
                    raise TransitionError()
                config = json.loads((source / 'tools/contracts/oasdiff.json').read_bytes())
                relative = config['binaryPath']
                _snapshot(((relative, b''),))
                binary = Path(self.tool_root) / relative
                if not binary.is_file() or hashlib.sha256(binary.read_bytes()).hexdigest() != config['binarySha256']:
                    raise TransitionError()
                if config['breakOn'] not in ('INFO', 'WARN', 'ERR') or not re.fullmatch(r'[^\s]+@sha256:[0-9a-f]{64}', config['dockerImage']):
                    raise TransitionError()
                # The SDK routines verify the package against each policy's pin.
                asset = Path(self.tool_root) / '.local/plan3-tools/corvus.json.cli.5.8.8.nupkg'
                target = source / '.local/plan3-tools' / asset.name
                target.parent.mkdir(parents=True)
                shutil.copyfile(asset, target)
                if not re.fullmatch('[0-9a-f]{40}', self.base_sha):
                    raise TransitionError()
                # Execute the selected immutable policy module in a fresh process;
                # never import candidate evaluator code into the verifier process.
                environment = {name: os.environ[name] for name in
                               ('PATH', 'HOME', 'TMPDIR', 'LD_LIBRARY_PATH', 'NPM_CONFIG_CACHE')
                               if name in os.environ}
                worker = Path(__file__).with_name('semantic_worker.py')
                proc = subprocess.run([sys.executable, str(worker), str(source), str(old_source),
                                       str(root / 'base-contracts'), str(candidate), self.base_sha,
                                       str(binary.resolve())], cwd=root, env=environment,
                                      stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=1200)
                if proc.returncode:
                    raise TransitionError()
                report = json.loads(proc.stdout)
                return CompatibilityResult(report['reason'])
        except Exception:
            # This is a closed runner failure, not permission to skip semantics.
            return CompatibilityResult('CONTRACT_TOOL_ERROR')
