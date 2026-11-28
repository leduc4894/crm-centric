"""Immutable reviewed-schema event and Git boundary; synthetic authority only."""
from dataclasses import replace
from contextlib import contextmanager
import hashlib
import hmac
import importlib
import json
import os
import signal
from pathlib import Path
import subprocess
import tempfile
import time
import unittest
from unittest.mock import patch

from scripts.ci.revision import GitReader, resolve_event

try:
    subject = importlib.import_module('scripts.ci.reviewed_schema')
except ImportError as error:
    if error.name != 'scripts.ci.reviewed_schema':
        raise
    subject = None


class SyntheticEventAuthority:
    """Test-only authority binds proof to all identity and method fields."""
    @staticmethod
    def proof(revision, kind, method):
        data = [revision.candidate_sha, revision.base_sha, revision.head_sha,
                revision.merge_sha, revision.merge_base_sha, kind, method]
        return hmac.digest(b'public-test-event-key', json.dumps(data, separators=(',', ':')).encode(), 'sha256')

    def verify_event(self, revision, kind, method, proof):
        return hmac.compare_digest(proof, self.proof(revision, kind, method))

    @contextmanager
    def confine(self, repo, revision, candidate_tree):
        # A test double; this does not implement an OS or CI confinement boundary.
        class Guard:
            valid = True

            def assert_confined(self):
                return self.valid

        yield Guard()


class ReviewSnapshotTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(subject, 'reviewed-schema boundary is not implemented')
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.git('init', '-q')
        self.git('config', 'user.name', 'Test')
        self.git('config', 'user.email', 'test@example.invalid')
        self.write('docs/contracts/api.openapi.yaml', 'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $ref: defs.json#/$defs/Item\n')
        self.write('docs/contracts/defs.json', '{"$defs":{"Item":{"$ref":"deep.json"}}}')
        self.write('docs/contracts/deep.json', '{"type":"string"}')
        self.write('docs/contracts/item.schema.json', '{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"string"}')
        self.write('scripts/contracts/generate.py', 'print("fixture")\n')
        self.write('tools/contracts/toolchain.json', '{"tool":"pinned"}')
        self.write('docs/architecture/adr.md', '# Review policy\n')
        self.write('sdks/client/schema.d.ts', 'export type Item = string;\n')
        self.write('tests/Contract/consumer.py', '# independent fixture\n')
        self.write('global.json', '{"sdk":{"version":"10.0.403"}}')
        self.write('.node-version', '24.22.2\n')
        self.write('Directory.Build.props', '<Project/>\n')
        self.write('Directory.Packages.props', '<Project/>\n')
        self.write('.github/actions/example/action.yml', 'name: example\n')
        self.write('scripts/other-tool.py', '# relevant tool\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'base')
        self.base = self.git('rev-parse', 'HEAD').strip()
        self.git('checkout', '-qb', 'feature')
        self.write('docs/contracts/item.schema.json', '{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"integer"}')
        self.git('add', '.')
        self.git('commit', '-qm', 'feature')
        self.head = self.git('rev-parse', 'HEAD').strip()
        self.git('checkout', '-q', '--detach', self.base)
        self.git('merge', '-q', '--no-ff', '-m', 'merge', self.head)
        self.candidate = self.git('rev-parse', 'HEAD').strip()
        self.tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        self.authority = SyntheticEventAuthority()
        self.pr_data = {'event_name': 'pull_request', 'pull_request': {'base': {'sha': self.base}, 'head': {'sha': self.head}}}
        self.pr = resolve_event(self.pr_data, self.candidate, GitReader(self.repo), event_sha=self.candidate)

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.repo, text=True, stderr=subprocess.DEVNULL)

    def write(self, name, value):
        path = self.repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value, encoding='utf-8')

    def verified(self, revision=None, kind='pull_request', method='merge', authority=None):
        revision = revision or self.pr
        authority = self.authority if authority is None else authority
        proof = authority.proof(revision, kind, method)
        return subject.wrap_verified_event(revision, kind, method, proof, authority)

    def test_total_deadline_bounds_slow_authority(self):
        event = self.verified()
        original = event.authority.verify_event

        def slow_verify(*args):
            time.sleep(0.2)
            return original(*args)

        self.assertTrue(hasattr(subject, '_CAPTURE_TIMEOUT_SECONDS'), 'total capture deadline is missing')
        with patch.object(subject, '_CAPTURE_TIMEOUT_SECONDS', 0.05), \
                patch.object(event.authority, 'verify_event', side_effect=slow_verify):
            started = time.monotonic()
            with self.assertRaises(subject.ReviewSnapshotError):
                subject.capture_review_snapshot(self.repo, event, self.tree)
            self.assertLess(time.monotonic() - started, 0.18)

    def test_total_deadline_kills_and_reaps_blocked_worker_descendant(self):
        event = self.verified()
        self.assertTrue(hasattr(subject, '_CAPTURE_TIMEOUT_SECONDS'), 'total capture deadline is missing')
        marker = self.repo.parent / (self.repo.name + '-descendant-pid')

        def blocked_read(*args):
            child = subprocess.Popen(['/bin/sleep', '30'], start_new_session=True)
            marker.write_text(str(child.pid), encoding='ascii')
            read_fd, write_fd = os.pipe()
            try:
                os.read(read_fd, 1)
            finally:
                os.close(read_fd)
                os.close(write_fd)

        try:
            with patch.object(subject, '_CAPTURE_TIMEOUT_SECONDS', 1), \
                    patch.object(subject, '_read_worktree_file', side_effect=blocked_read):
                with self.assertRaises(subject.ReviewSnapshotError):
                    subject.capture_review_snapshot(self.repo, event, self.tree)
            self.assertTrue(marker.exists(), 'blocked read was not reached')
            pid = int(marker.read_text(encoding='ascii'))
            self.assertFalse(Path('/proc', str(pid)).exists(), 'descendant not terminated and reaped')
        finally:
            if marker.exists():
                pid = int(marker.read_text(encoding='ascii'))
                try:
                    os.kill(pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                marker.unlink()

    def test_total_deadline_limits_many_individually_fast_git_calls(self):
        event = self.verified()
        original_git = subject._git
        def slow_git(*args, **kwargs):
            time.sleep(0.02)
            return original_git(*args, **kwargs)
        with patch.object(subject, '_CAPTURE_TIMEOUT_SECONDS', 0.15), \
                patch.object(subject, '_git', side_effect=slow_git):
            started = time.monotonic()
            with self.assertRaises(subject.ReviewSnapshotError):
                subject.capture_review_snapshot(self.repo, event, self.tree)
            self.assertLess(time.monotonic() - started, 1)

    def test_supervisor_unavailable_and_authority_exception_fail_closed(self):
        event = self.verified()
        with patch.object(subject.ctypes, 'CDLL', side_effect=OSError('unavailable')):
            with self.assertRaises(subject.ReviewSnapshotError) as failure:
                subject.capture_review_snapshot(self.repo, event, self.tree)
            self.assertEqual('CI_REVIEW_SUPERVISOR_UNAVAILABLE', failure.exception.reason_token)
        with patch.object(event.authority, 'verify_event', side_effect=RuntimeError('private')):
            with self.assertRaises(subject.ReviewSnapshotError) as failure:
                subject.capture_review_snapshot(self.repo, event, self.tree)
            self.assertEqual('CI_REVIEW_EVENT_UNTRUSTED', failure.exception.reason_token)
            self.assertNotIn('private', str(failure.exception))

    def test_exact_git_snapshot_and_dirty_input_fail_closed(self):
        event = self.verified()
        result = subject.capture_review_snapshot(self.repo, event, self.tree)
        base = dict(result.base_inputs)
        current = dict(result.candidate_inputs)
        self.assertEqual(b'{"$defs":{"Item":{"$ref":"deep.json"}}}', current['docs/contracts/defs.json'])
        self.assertEqual(b'{"type":"string"}', current['docs/contracts/deep.json'])
        self.assertNotEqual(base['docs/contracts/item.schema.json'], current['docs/contracts/item.schema.json'])
        self.assertEqual(hashlib.sha256(current['docs/contracts/defs.json']).hexdigest(), dict(result.candidate_hashes)['docs/contracts/defs.json'])
        self.assertIn('scripts/contracts/generate.py', dict(result.candidate_policy))
        self.assertIn('docs/architecture/adr.md', dict(result.candidate_adr))
        self.assertIn('sdks/client/schema.d.ts', dict(result.candidate_sdk))
        self.assertIn('tests/Contract/consumer.py', dict(result.candidate_consumers))
        self.write('docs/contracts/defs.json', '{}')
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, event, self.tree)
        self.git('add', 'docs/contracts/defs.json')
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, event, self.tree)
        self.git('reset', '-q', 'HEAD', '--', 'docs/contracts/defs.json')
        self.git('checkout', '--', 'docs/contracts/defs.json')
        self.write('docs/contracts/new.schema.json', '{}')
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, event, self.tree)

    def test_dirty_status_change_during_capture_fails_closed(self):
        event = self.verified()
        self.assertTrue(hasattr(subject, '_check_worktree'), 'safe worktree inspection is missing')
        check_worktree = subject._check_worktree
        first_check = True

        def racing_check(*args):
            nonlocal first_check
            result = check_worktree(*args)
            if first_check:
                first_check = False
                self.write('docs/contracts/defs.json', '{}')
            return result

        with patch.object(subject, '_check_worktree', side_effect=racing_check):
            with self.assertRaises(subject.ReviewSnapshotError):
                subject.capture_review_snapshot(self.repo, event, self.tree)

    def test_replaced_git_object_is_not_accepted(self):
        event = self.verified()
        self.git('replace', self.base, self.head)
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, event, self.tree)

    def test_git_alternates_and_promisor_configuration_fail_closed(self):
        event = self.verified()
        alternates = self.repo / '.git/objects/info/alternates'
        alternates.write_text(str(self.repo / '.git/objects') + '\n', encoding='utf-8')
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, event, self.tree)
        alternates.unlink()
        self.git('config', 'remote.synthetic.promisor', 'true')
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, event, self.tree)

    def test_replace_ref_added_during_capture_fails_closed(self):
        event = self.verified()
        check_worktree = subject._check_worktree
        first_check = True

        def racing_check(*args):
            nonlocal first_check
            result = check_worktree(*args)
            if first_check:
                first_check = False
                self.git('replace', self.base, self.head)
            return result

        with patch.object(subject, '_check_worktree', side_effect=racing_check):
            with self.assertRaises(subject.ReviewSnapshotError):
                subject.capture_review_snapshot(self.repo, event, self.tree)

    def test_missing_base_object_is_rejected_even_with_bound_test_proof(self):
        missing_base = 'f' * 40
        revision = replace(self.pr, base_sha=missing_base, merge_base_sha=missing_base)
        event = self.verified(revision)
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, event, self.tree)

    def test_oversize_tracked_file_fails_closed(self):
        oversized = self.repo / 'docs/contracts/large.json'
        oversized.write_bytes(b'x' * (16 * 1024 * 1024 + 1))
        self.git('add', 'docs/contracts/large.json')
        self.git('commit', '-qm', 'oversize')
        candidate = self.git('rev-parse', 'HEAD').strip()
        tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        revision = resolve_event({'event_name': 'push', 'before': self.candidate, 'after': candidate, 'forced': False},
                                 candidate, GitReader(self.repo))
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, self.verified(revision, 'push', None), tree)

    def test_oversize_git_blob_is_not_read_when_worktree_file_is_smaller(self):
        self.write('docs/contracts/large.json', 'x' * (16 * 1024 * 1024 + 1))
        self.git('add', 'docs/contracts/large.json')
        self.git('commit', '-qm', 'oversize blob')
        candidate = self.git('rev-parse', 'HEAD').strip()
        tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        oid = self.git('rev-parse', 'HEAD:docs/contracts/large.json').strip()
        revision = resolve_event({'event_name': 'push', 'before': self.candidate, 'after': candidate, 'forced': False},
                                 candidate, GitReader(self.repo))
        event = self.verified(revision, 'push', None)
        self.write('docs/contracts/large.json', '{}')
        original = subject._git
        oversized_reads = []

        def recording_git(repo, *args, **kwargs):
            if args == ('cat-file', 'blob', oid):
                oversized_reads.append(oid)
            return original(repo, *args, **kwargs)

        with patch.object(subject, '_git', side_effect=recording_git):
            with self.assertRaises(subject.ReviewSnapshotError):
                subject.capture_review_snapshot(self.repo, event, tree)
        self.assertEqual([], oversized_reads, 'oversized Git object was read into memory')

    def test_status_does_not_execute_repo_configured_fsmonitor(self):
        event = self.verified()
        marker = self.repo / '.git' / 'fsmonitor-ran'
        hook = self.repo / '.git' / 'synthetic-fsmonitor'
        hook.write_text(f'#!/bin/sh\ntouch "{marker}"\nexit 0\n', encoding='utf-8')
        hook.chmod(0o755)
        self.git('config', 'core.fsmonitor', str(hook))
        subject.capture_review_snapshot(self.repo, event, self.tree)
        self.assertFalse(marker.exists(), 'capture executed repository-configured fsmonitor')

    def test_status_does_not_execute_repo_configured_clean_filter(self):
        marker = self.repo / '.git' / 'filter-ran'
        hook = self.repo / '.git' / 'synthetic-filter'
        hook.write_text(f'#!/bin/sh\ntouch "{marker}"\ncat\n', encoding='utf-8')
        hook.chmod(0o755)
        self.git('config', 'filter.synthetic.clean', str(hook))
        self.git('config', 'filter.synthetic.required', 'true')
        self.write('.gitattributes', 'docs/contracts/filtered.json filter=synthetic\n')
        self.write('docs/contracts/filtered.json', '{}')
        self.git('add', '.')
        self.git('commit', '-qm', 'filtered fixture')
        self.assertTrue(marker.exists(), 'synthetic filter did not execute during git add')
        marker.unlink(missing_ok=True)
        candidate = self.git('rev-parse', 'HEAD').strip()
        tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        revision = resolve_event({'event_name': 'push', 'before': self.candidate, 'after': candidate, 'forced': False},
                                 candidate, GitReader(self.repo))
        event = self.verified(revision, 'push', None)
        self.write('docs/contracts/filtered.json', '[]')
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, event, tree)
        self.assertFalse(marker.exists(), 'capture executed repository-configured clean filter')

    def test_semantically_noncanonical_eol_attributes_fail_closed(self):
        self.write('.gitattributes', 'docs/contracts/defs.json text eol=crlf\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'unsafe eol fixture')
        candidate = self.git('rev-parse', 'HEAD').strip()
        tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        revision = resolve_event({'event_name': 'push', 'before': self.candidate, 'after': candidate, 'forced': False},
                                 candidate, GitReader(self.repo))
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, self.verified(revision, 'push', None), tree)

    def test_safe_lf_auto_attributes_allow_unchanged_binary_and_text(self):
        self.write('.gitattributes', '* text=auto eol=lf\n')
        (self.repo / 'sdks/client/image.bin').write_bytes(b'\x00\r\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'safe lf fixture')
        candidate = self.git('rev-parse', 'HEAD').strip()
        tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        revision = resolve_event({'event_name': 'push', 'before': self.candidate, 'after': candidate, 'forced': False},
                                 candidate, GitReader(self.repo))
        result = subject.capture_review_snapshot(self.repo, self.verified(revision, 'push', None), tree)
        self.assertEqual(b'\x00\r\n', dict(result.candidate_sdk)['sdks/client/image.bin'])

    def test_symlinked_worktree_parent_fails_closed(self):
        event = self.verified()
        source = self.repo / 'docs/contracts'
        relocated = self.repo / '.git' / 'synthetic-contracts'
        source.rename(relocated)
        source.symlink_to(relocated, target_is_directory=True)
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, event, self.tree)

    def test_complete_tracked_tree_binds_root_and_workflow_dependencies(self):
        result = subject.capture_review_snapshot(self.repo, self.verified(), self.tree)
        self.assertTrue(hasattr(result, 'candidate_repository'), 'complete repository inventory is missing')
        repository = dict(result.candidate_repository)
        for name in ('global.json', '.node-version', 'Directory.Build.props', 'Directory.Packages.props',
                     '.github/actions/example/action.yml', 'scripts/other-tool.py'):
            with self.subTest(name=name):
                self.assertIn(name, repository)
                self.assertEqual(hashlib.sha256(repository[name]).hexdigest(), dict(result.candidate_repository_hashes)[name])
        self.write('global.json', '{"sdk":{"version":"forged"}}')
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, self.verified(), self.tree)

    def test_committed_root_mutation_and_action_addition_change_repository_digest_set(self):
        self.write('global.json', '{"sdk":{"version":"10.0.404"}}')
        self.write('.github/actions/example/added.yml', 'name: added\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'change policy dependencies')
        candidate = self.git('rev-parse', 'HEAD').strip()
        tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        revision = resolve_event({'event_name': 'push', 'before': self.candidate, 'after': candidate, 'forced': False},
                                 candidate, GitReader(self.repo))
        result = subject.capture_review_snapshot(self.repo, self.verified(revision, 'push', None), tree)
        before, after = dict(result.base_repository_hashes), dict(result.candidate_repository_hashes)
        self.assertNotEqual(before['global.json'], after['global.json'])
        self.assertNotIn('.github/actions/example/added.yml', before)
        self.assertIn('.github/actions/example/added.yml', after)

    def test_missing_or_revoked_confinement_fails_closed(self):
        class NoGuardAuthority:
            def verify_event(self, revision, kind, method, proof):
                return True
        unguarded = subject.wrap_verified_event(self.pr, 'pull_request', 'merge', b'public-test-proof', NoGuardAuthority())
        with self.assertRaises(subject.ReviewSnapshotError) as failure:
            subject.capture_review_snapshot(self.repo, unguarded, self.tree)
        self.assertEqual('CI_REVIEW_CONFINEMENT_UNAVAILABLE', failure.exception.reason_token)

        class RevokedAuthority(SyntheticEventAuthority):
            @contextmanager
            def confine(self, repo, revision, candidate_tree):
                class Guard:
                    checks = 0
                    def assert_confined(self):
                        self.checks += 1
                        return self.checks < 2
                yield Guard()
        authority = RevokedAuthority()
        proof = authority.proof(self.pr, 'pull_request', 'merge')
        event = subject.wrap_verified_event(self.pr, 'pull_request', 'merge', proof, authority)
        with self.assertRaises(subject.ReviewSnapshotError) as failure:
            subject.capture_review_snapshot(self.repo, event, self.tree)
        self.assertEqual('CI_REVIEW_CONFINEMENT_UNAVAILABLE', failure.exception.reason_token)

    def test_transient_restored_worktree_change_is_rejected_when_guard_detects_it(self):
        class GuardedAuthority(SyntheticEventAuthority):
            @contextmanager
            def confine(self, repo, revision, candidate_tree):
                class Guard:
                    valid = True
                    def assert_confined(self):
                        return self.valid
                self.guard = Guard()
                yield self.guard
        authority = GuardedAuthority()
        proof = authority.proof(self.pr, 'pull_request', 'merge')
        event = subject.wrap_verified_event(self.pr, 'pull_request', 'merge', proof, authority)
        original_inventory = subject._inventory
        altered = False

        def mutate_and_restore(*args):
            nonlocal altered
            if not altered:
                altered = True
                path = self.repo / 'global.json'
                original = path.read_bytes()
                path.write_bytes(b'{"temporary":"changed"}')
                path.write_bytes(original)
                authority.guard.valid = False
            return original_inventory(*args)

        with patch.object(subject, '_inventory', side_effect=mutate_and_restore):
            with self.assertRaises(subject.ReviewSnapshotError) as failure:
                subject.capture_review_snapshot(self.repo, event, self.tree)
        self.assertEqual('CI_REVIEW_CONFINEMENT_UNAVAILABLE', failure.exception.reason_token)

    def test_ignored_untracked_root_dependency_fails_closed(self):
        self.git('rm', '-q', 'global.json')
        self.write('.gitignore', 'global.json\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'ignore fixture')
        candidate = self.git('rev-parse', 'HEAD').strip()
        tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        revision = resolve_event({'event_name': 'push', 'before': self.candidate, 'after': candidate, 'forced': False},
                                 candidate, GitReader(self.repo))
        self.write('global.json', '{"sdk":{"version":"forged"}}')
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, self.verified(revision, 'push', None), tree)

    def test_invalid_json_pointer_and_unsupported_schema_scope_fail_closed(self):
        bad_documents = (
            'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $ref: defs.json#/$defs/missing\n',
            'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $ref: defs.json#badAnchor\n',
            'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $id: https://other.example/\n',
            'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $anchor: foo\n',
            'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $dynamicAnchor: foo\n',
            'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $dynamicRef: "#thing"\n',
            'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $ref: "defs.json#/$defs/invalid~2escape"\n',
            'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $ref: "#/components/schemas/Item"\n',
        )
        previous = self.candidate
        for index, text in enumerate(bad_documents):
            with self.subTest(document=index):
                self.write('docs/contracts/api.openapi.yaml', text)
                self.git('add', '.')
                self.git('commit', '-qm', f'invalid-{index}')
                candidate = self.git('rev-parse', 'HEAD').strip()
                tree = self.git('rev-parse', 'HEAD^{tree}').strip()
                revision = resolve_event({'event_name': 'push', 'before': previous, 'after': candidate, 'forced': False},
                                         candidate, GitReader(self.repo))
                with self.assertRaises(subject.ReviewSnapshotError):
                    subject.capture_review_snapshot(self.repo, self.verified(revision, 'push', None), tree)
                previous = candidate

    def test_valid_escaped_json_pointer_and_array_reference(self):
        self.write('docs/contracts/defs.json', '{"$defs":{"a/b":[{"~name":{"type":"string"}}]}}')
        self.write('docs/contracts/api.openapi.yaml', 'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $ref: "defs.json#/$defs/a~1b/0/~0name"\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'escaped pointer')
        candidate = self.git('rev-parse', 'HEAD').strip()
        tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        revision = resolve_event({'event_name': 'push', 'before': self.candidate, 'after': candidate, 'forced': False},
                                 candidate, GitReader(self.repo))
        result = subject.capture_review_snapshot(self.repo, self.verified(revision, 'push', None), tree)
        self.assertIn('docs/contracts/defs.json', dict(result.candidate_inputs))

    def test_git_output_and_total_inventory_limits_apply_before_read(self):
        self.assertTrue(hasattr(subject, '_MAX_GIT_STDOUT_BYTES'), 'missing bounded Git output')
        self.assertTrue(hasattr(subject, '_MAX_SNAPSHOT_BYTES'), 'missing cumulative object budget')
        with patch.object(subject, '_MAX_GIT_STDOUT_BYTES', 128):
            with self.assertRaises(subject.ReviewSnapshotError):
                subject._git(self.repo, 'ls-tree', '-r', '-z', self.candidate)
        calls = []
        real_git = subject._git
        def record(repo, *args, **kwargs):
            if args[:2] == ('cat-file', 'blob'):
                calls.append(args)
            return real_git(repo, *args, **kwargs)
        with patch.object(subject, '_MAX_SNAPSHOT_BYTES', 1), patch.object(subject, '_git', side_effect=record):
            with self.assertRaises(subject.ReviewSnapshotError):
                subject._inventory(self.repo, self.candidate)
        self.assertEqual([], calls, 'object read before cumulative size validation')

    def test_ci_evidence_is_included_but_only_noninput_caches_are_ignored(self):
        self.write('.gitignore', '__pycache__/\nobj/\n*.schema.json\n')
        self.write('tests/CI/security_test.py', '# policy evidence\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'ci evidence')
        candidate = self.git('rev-parse', 'HEAD').strip()
        tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        revision = resolve_event({'event_name': 'push', 'before': self.candidate, 'after': candidate, 'forced': False},
                                 candidate, GitReader(self.repo))
        event = self.verified(revision, 'push', None)
        self.write('tests/CI/__pycache__/security_test.pyc', 'test-cache')
        self.write('sdks/client/obj/build.bin', 'test-output')
        result = subject.capture_review_snapshot(self.repo, event, tree)
        self.assertIn('tests/CI/security_test.py', dict(result.candidate_consumers))
        self.write('docs/contracts/untracked.schema.json', '{}')
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, event, tree)

    def test_pr_merge_and_push_identity_are_not_interchangeable(self):
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.wrap_verified_event(self.pr, 'push', None, b'wrong', self.authority)
        self.git('checkout', '-q', '--detach', self.head)
        push = resolve_event({'event_name': 'push', 'before': self.base, 'after': self.head, 'forced': False},
                             self.head, GitReader(self.repo))
        event = self.verified(push, 'push', None)
        self.assertEqual(push.base_sha, self.base)
        self.assertEqual(push.head_sha, self.head)
        self.assertIsNone(push.merge_sha)
        self.assertEqual(event.kind, 'push')
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.wrap_verified_event(push, 'pull_request', 'merge', self.authority.proof(push, 'pull_request', 'merge'), self.authority)

    def test_push_requires_null_integration_method_and_pr_method_is_independently_attested(self):
        with self.assertRaises(subject.ReviewSnapshotError) as failure:
            subject.wrap_verified_event(self.pr, 'pull_request', 'merge', b'fake', None)
        self.assertEqual('CI_REVIEW_AUTHORITY_UNAVAILABLE', failure.exception.reason_token)
        self.assertRegex(failure.exception.correlation_id, r'^[0-9a-f-]{36}$')
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.wrap_verified_event(self.pr, 'pull_request', 'squash', self.authority.proof(self.pr, 'pull_request', 'merge'), self.authority)
        self.git('checkout', '-q', '--detach', self.head)
        push = resolve_event({'event_name': 'push', 'before': self.base, 'after': self.head, 'forced': False},
                             self.head, GitReader(self.repo))
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.wrap_verified_event(push, 'push', 'merge', self.authority.proof(push, 'push', 'merge'), self.authority)
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.wrap_verified_event(replace(push, merge_base_sha='0' * 40), 'push', None,
                                        self.authority.proof(push, 'push', None), self.authority)

    def test_missing_extra_symlink_path_and_ref_fail_closed(self):
        event = self.verified()
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, event, '0' * 40)
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, replace(event, revision=replace(self.pr, candidate_sha='f' * 40)), self.tree)
        self.git('checkout', '-q', '-b', 'invalid')
        self.write('docs/contracts/api.openapi.yaml', 'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $ref: https://outside.example/schema.json\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'external ref')
        bad = self.git('rev-parse', 'HEAD').strip()
        bad_tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        bad_rev = resolve_event({'event_name': 'push', 'before': self.candidate, 'after': bad, 'forced': False}, bad, GitReader(self.repo))
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, self.verified(bad_rev, 'push', None), bad_tree)
        self.write('docs/contracts/api.openapi.yaml', 'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $ref: missing.json\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'missing ref')
        missing = self.git('rev-parse', 'HEAD').strip()
        missing_tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        missing_rev = resolve_event({'event_name': 'push', 'before': bad, 'after': missing, 'forced': False}, missing, GitReader(self.repo))
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, self.verified(missing_rev, 'push', None), missing_tree)
        self.write('docs/contracts/api.openapi.yaml', 'openapi: 3.1.0\ncomponents:\n  schemas:\n    Item:\n      $ref: defs.json#/$defs/Item\n')
        (self.repo / 'docs/contracts/link.json').symlink_to('defs.json')
        self.git('add', '.')
        self.git('commit', '-qm', 'symlink')
        link = self.git('rev-parse', 'HEAD').strip()
        link_tree = self.git('rev-parse', 'HEAD^{tree}').strip()
        link_rev = resolve_event({'event_name': 'push', 'before': missing, 'after': link, 'forced': False}, link, GitReader(self.repo))
        with self.assertRaises(subject.ReviewSnapshotError):
            subject.capture_review_snapshot(self.repo, self.verified(link_rev, 'push', None), link_tree)


if __name__ == '__main__':
    unittest.main()
