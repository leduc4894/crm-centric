"""Transition safety at the trusted workflow boundary; no live signing keys."""
from dataclasses import replace
import hashlib
import hmac
import json
from pathlib import Path
import subprocess
import tempfile
import time
import unittest
from scripts.ci.revision import RevisionContext
from scripts.ci import policy_transition as p


def encoded(payload):
    return json.dumps(payload, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


class TrustedFixture:
    # Synthetic signer/verifier stands in for independently provisioned service.
    candidate_tree = 'd' * 40
    merge_method = 'merge'
    old_policy = (('tools/contracts/policy.json', b'old'),)
    new_policy = (('tools/contracts/policy.json', b'new'),)
    base_contracts = (('docs/contracts/api.openapi.yaml', b'base contracts'),)
    candidate_contracts = (('docs/contracts/api.openapi.yaml', b'real candidate contracts'),)

    def verify(self, payload, signature):
        return hmac.compare_digest(signature, hmac.digest(b'synthetic-test-key', payload, 'sha256').hex())


class TransitionTests(unittest.TestCase):
    def setUp(self):
        self.key = TrustedFixture()
        self.rev = RevisionContext('c' * 40, 'a' * 40, 'b' * 40, 'c' * 40, 'a' * 40)
        self.old = {'tools/contracts/policy.json': hashlib.sha256(b'old').hexdigest()}
        self.new = {'tools/contracts/policy.json': hashlib.sha256(b'new').hexdigest()}
        self.payload = dict(version=1, identity='review-123', base_sha=self.rev.base_sha,
                            head_sha=self.rev.head_sha, candidate_sha=self.rev.candidate_sha,
                            candidate_tree=self.key.candidate_tree, merge_method='merge',
                            event='pull_request', old_hashes=self.old, new_hashes=self.new,
                            files=list(self.old), adr='docs/adr/0042.md',
                            consumer_tests=['tests/consumers/reviewed'], reviewer='maintainer',
                            expires_at=int(time.time()) + 3600)

    def attestation(self, **changes):
        payload = {**self.payload, **changes}
        return dict(payload=payload, signature=hmac.digest(b'synthetic-test-key', encoded(payload), 'sha256').hex())

    def verify(self, attestation=None, key=True):
        return p.verify_transition(attestation or self.attestation(), self.key if key else None,
                                   self.rev, self.old, self.new)

    def test_valid_transition_uses_same_immutable_real_inputs_twice(self):
        transition = self.verify()
        seen = []
        def evaluator(policy, base, candidate):
            seen.append((policy, base, candidate))
            return p.CompatibilityResult('CONTRACT_COMPATIBLE')
        result = p.evaluate_transition(transition, evaluator, evaluator)
        self.assertEqual(result.reason, 'CONTRACT_COMPATIBLE')
        self.assertEqual(seen, [(self.key.old_policy, self.key.base_contracts, self.key.candidate_contracts),
                                (self.key.new_policy, self.key.base_contracts, self.key.candidate_contracts)])
        self.assertEqual(len(result.evaluations), 2)
        self.assertEqual(result.attestation_identity, 'review-123')

    def test_preflight_accepts_only_exact_verified_hash_identity(self):
        from scripts.contracts.compatibility import policy_identity_preflight, GateError
        transition = self.verify()
        policy_identity_preflight(self.old, self.new, self.rev, transition)
        policy_identity_preflight(self.old, self.old, self.rev)
        for old, new, revision, proof in [
            (self.old, self.new, self.rev, None),
            (self.old, self.new, replace(self.rev, base_sha='e'*40), transition),
            (self.new, self.old, self.rev, transition),
            (self.old, self.new, self.rev, {'waiver': True}),
        ]:
            with self.assertRaises(GateError):
                policy_identity_preflight(old, new, revision, proof)

    def test_result_binds_hashes_even_when_evaluator_fails(self):
        transition = self.verify()
        for reason in ['CONTRACT_COMPATIBLE', 'CONTRACT_BREAKING']:
            result = p.evaluate_transition(transition, lambda *a: p.CompatibilityResult(reason),
                                            lambda *a: p.CompatibilityResult('CONTRACT_COMPATIBLE'))
            self.assertEqual(dict(result.old_policy_hashes), self.old)
            self.assertEqual(dict(result.new_policy_hashes), self.new)
            self.assertEqual(dict(result.base_contract_hashes),
                             {'docs/contracts/api.openapi.yaml': hashlib.sha256(b'base contracts').hexdigest()})
            self.assertEqual(dict(result.candidate_contract_hashes),
                             {'docs/contracts/api.openapi.yaml': hashlib.sha256(b'real candidate contracts').hexdigest()})

    def test_git_adapter_reads_objects_instead_of_dirty_worktree(self):
        with tempfile.TemporaryDirectory() as temp:
            repo = Path(temp)
            def git(*args):
                return subprocess.run(['git', '-c', 'user.name=Fixture', '-c',
                                       'user.email=fixture@example.invalid', *args], cwd=repo,
                                      check=True, capture_output=True, text=True).stdout.strip()
            git('init', '-q')
            (repo / 'docs/contracts').mkdir(parents=True)
            (repo / 'tools/contracts').mkdir(parents=True)
            policy = repo / 'tools/contracts/policy.json'
            contract = repo / 'docs/contracts/api.openapi.yaml'
            policy.write_bytes(b'old')
            contract.write_bytes(b'base contracts')
            git('add', '.')
            git('commit', '-qm', 'base')
            base = git('rev-parse', 'HEAD')
            policy.write_bytes(b'new')
            contract.write_bytes(b'real candidate contracts')
            git('add', '.')
            git('commit', '-qm', 'candidate')
            candidate = git('rev-parse', 'HEAD')
            tree = git('rev-parse', 'HEAD^{tree}')
            self.rev = RevisionContext(candidate, base, candidate, None, base)
            policy.write_bytes(b'dirty policy')
            contract.write_bytes(b'dirty contracts')
            self.key = p.GitSnapshotCapability.from_git(repo, self.rev, self.key.verify,
                                                       'merge', tuple(self.old))
            self.payload.update(base_sha=base, head_sha=candidate, candidate_sha=candidate,
                                candidate_tree=tree, event='push')
            transition = self.verify()
            self.assertEqual(transition.old_policy, (('tools/contracts/policy.json', b'old'),))
            self.assertEqual(transition.new_policy, (('tools/contracts/policy.json', b'new'),))
            self.assertEqual(transition.base_contracts, (('docs/contracts/api.openapi.yaml', b'base contracts'),))
            self.assertEqual(transition.candidate_contracts,
                             (('docs/contracts/api.openapi.yaml', b'real candidate contracts'),))
            with self.assertRaises(p.TransitionError):
                self.verify(self.attestation(candidate_tree='e'*40))
            for paths in [('../policy',), ('missing',), ()]:
                with self.assertRaises(p.TransitionError):
                    p.GitSnapshotCapability.from_git(repo, self.rev, self.key.verify, 'merge', paths)

    def test_bad_bindings_and_missing_review_fail_closed(self):
        for field, value in [('base_sha', 'e'*40), ('head_sha', 'e'*40),
                             ('candidate_sha', 'e'*40), ('candidate_tree', 'e'*40),
                             ('old_hashes', self.new), ('new_hashes', self.old),
                             ('files', ['waiver.json']), ('expires_at', 0), ('reviewer', ''),
                             ('adr', ''), ('consumer_tests', []), ('event', 'push')]:
            with self.subTest(field=field), self.assertRaises(p.TransitionError) as failure:
                self.verify(self.attestation(**{field: value}))
            self.assertTrue(failure.exception.correlation_id)

    def test_forgery_waiver_and_absent_capability_fail(self):
        forged = self.attestation()
        forged['payload']['reviewer'] = 'forged'
        for attestation, key in [(forged, True), ({'waiver': True}, True), (self.attestation(), False)]:
            with self.subTest(attestation=attestation), self.assertRaises(p.TransitionError):
                self.verify(attestation, key)

    def test_snapshot_mismatch_rejected(self):
        self.key.old_policy = (('tools/contracts/policy.json', b'tampered'),)
        with self.assertRaises(p.TransitionError):
            self.verify()

    def test_each_evaluator_breaking_or_error_fails_even_with_signature(self):
        transition = self.verify()
        good = lambda *args: p.CompatibilityResult('CONTRACT_COMPATIBLE')
        def error(*args):
            raise RuntimeError('sensitive diagnostic')
        for bad in [lambda *args: p.CompatibilityResult('CONTRACT_BREAKING'), error,
                    lambda *args: None]:
            for evaluators in [(good, bad), (bad, good)]:
                result = p.evaluate_transition(transition, *evaluators)
                self.assertNotEqual(result.reason, 'CONTRACT_COMPATIBLE')
                self.assertEqual(len(result.evaluations), 2)
                self.assertNotIn('sensitive', repr(result))

    def test_first_push_requires_fresh_before_method_and_resulting_tree(self):
        self.rev = replace(self.rev, head_sha=self.rev.candidate_sha, merge_sha=None)
        self.payload.update(event='push', head_sha=self.rev.head_sha)
        self.assertEqual(self.verify().identity, 'review-123')
        for changes in [dict(base_sha='e'*40), dict(merge_method='squash'), dict(candidate_tree='e'*40)]:
            with self.assertRaises(p.TransitionError):
                self.verify(self.attestation(**changes))

    def test_rollback_needs_separate_reverse_attestation(self):
        with self.assertRaises(p.TransitionError):
            p.verify_transition(self.attestation(), self.key, self.rev, self.new, self.old)
        self.key.old_policy, self.key.new_policy = self.key.new_policy, self.key.old_policy
        self.old, self.new = self.new, self.old
        self.assertEqual(self.verify(self.attestation(old_hashes=self.old, new_hashes=self.new,
                                                    identity='rollback-124')).identity, 'rollback-124')


if __name__ == '__main__':
    unittest.main()
