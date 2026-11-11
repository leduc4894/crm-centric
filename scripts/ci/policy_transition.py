"""Trusted-workflow API, deliberately unavailable from candidate CLI arguments.

The caller must provision trusted_key (signature verifier and Git snapshots) from
an independent pinned workflow. This module neither discovers keys nor imports
candidate evaluators. Semantic evaluation belongs in a separate unprivileged job.
"""
from dataclasses import dataclass
import hashlib
import json
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
    return CompatibilityResult(reason, tuple(outcomes), transition.identity, str(uuid.uuid4()))
