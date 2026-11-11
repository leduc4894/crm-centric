"""Real synthetic Git graphs catch acceptance of unrelated/stale revisions."""
import dataclasses
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts' / 'ci'))
from revision import GitReader, RevisionError, resolve_event


class RevisionTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.path = pathlib.Path(self.tmp.name)
        self.run_git('init', '-q')
        self.tree = self.run_git('mktree', input='')
        self.base = self.commit()
        self.head = self.commit(self.base)
        self.merge = self.commit(self.base, self.head)
        self.git = GitReader(self.path)
        self.event = {'event_name': 'pull_request', 'pull_request': {
            'base': {'sha': self.base}, 'head': {'sha': self.head},
            'merge_commit_sha': self.merge}}

    def run_git(self, *args, input=None):
        return subprocess.run(['git', '-c', 'user.name=Fixture', '-c',
                               'user.email=fixture@example.invalid', *args],
                              cwd=self.path, input=input, text=True,
                              capture_output=True, check=True).stdout.strip()

    def commit(self, *parents):
        args = ['commit-tree', self.tree]
        for parent in parents:
            args.extend(['-p', parent])
        return self.run_git(*args, input='synthetic\n')

    def reject(self, event, checkout, token):
        with self.assertRaises(RevisionError) as raised:
            resolve_event(event, checkout, self.git)
        self.assertEqual(token, raised.exception.reason_token)
        self.assertTrue(raised.exception.correlation_id)

    def test_pr_binds_all_immutable_revisions(self):
        ctx = resolve_event(self.event, self.merge, self.git)
        self.assertEqual((self.merge, self.base, self.head, self.merge, self.base),
                         (ctx.candidate_sha, ctx.base_sha, ctx.head_sha,
                          ctx.merge_sha, ctx.merge_base_sha))
        with self.assertRaises(dataclasses.FrozenInstanceError):
            ctx.base_sha = self.head

    def test_pr_rejects_replaced_or_stale_checkout(self):
        self.reject(self.event, self.head, 'CI_CANDIDATE_UNAVAILABLE')

    def test_pr_rejects_wrong_parent_order_and_non_merge(self):
        for candidate in (self.commit(self.head, self.base), self.head):
            self.event['pull_request']['merge_commit_sha'] = candidate
            self.reject(self.event, candidate, 'CI_CANDIDATE_UNAVAILABLE')

    def test_pr_rejects_advanced_event_base(self):
        self.event['pull_request']['base']['sha'] = self.head
        self.reject(self.event, self.merge, 'CI_CANDIDATE_UNAVAILABLE')

    def test_pr_missing_objects_fail_closed(self):
        self.event['pull_request']['base']['sha'] = 'a' * 40
        self.reject(self.event, self.merge, 'CI_BASE_UNAVAILABLE')
        self.event['pull_request']['base']['sha'] = self.base
        self.event['pull_request']['head']['sha'] = 'a' * 40
        self.reject(self.event, self.merge, 'CI_CANDIDATE_UNAVAILABLE')

    def test_push_accepts_proven_ancestor(self):
        ctx = resolve_event({'before': self.base, 'after': self.head, 'forced': False}, self.head, self.git)
        self.assertEqual((self.head, self.base, self.head, None),
                         (ctx.candidate_sha, ctx.base_sha, ctx.head_sha, ctx.merge_sha))

    def test_push_rejects_missing_zero_unrelated_and_forced_base(self):
        for base in (None, '0' * 40, 'a' * 40, self.merge):
            self.reject({'before': base, 'after': self.head, 'forced': False}, self.head,
                        'CI_BASE_UNAVAILABLE')
        self.reject({'before': self.base, 'after': self.head, 'forced': True}, self.head,
                    'CI_BASE_UNAVAILABLE')

    def test_push_rejects_wrong_checkout_or_missing_after(self):
        for after, checkout in ((self.head, self.base), ('a' * 40, 'a' * 40), (None, self.head)):
            self.reject({'before': self.base, 'after': after, 'forced': False}, checkout,
                        'CI_CANDIDATE_UNAVAILABLE')

    def test_manual_and_merge_queue_never_guess_base(self):
        for name in ('workflow_dispatch', 'merge_group'):
            self.reject({'event_name': name}, self.head, 'CI_BASE_UNAVAILABLE')

    def test_malformed_sha_never_becomes_git_argument(self):
        for value in ('HEAD', '--help', 'a' * 39, '0' * 40, 'a' * 40 + '\n'):
            self.reject(self.event, value, 'CI_CANDIDATE_UNAVAILABLE')

    def test_cli_argument_errors_are_safe_json(self):
        for args in (['--event-file', '/nonexistent', '--unexpected=SYNTHETIC_SENTINEL'],
                     ['--event-file'], [], ['--event-file', '/nonexistent', 'SYNTHETIC_SENTINEL']):
            with self.subTest(args=args):
                result = subprocess.run([sys.executable, str(ROOT / 'scripts/ci/revision.py'),
                                         *args], capture_output=True, text=True)
                self.assertEqual(1, result.returncode)
                self.assertEqual('', result.stderr)
                self.assertNotIn('SYNTHETIC_SENTINEL', result.stdout)
                output = json.loads(result.stdout)
                self.assertEqual('CI_CANDIDATE_UNAVAILABLE', output['reasonToken'])
                self.assertTrue(output['correlationId'])

    def test_cli_failure_is_safe_json(self):
        event_file = self.path / 'event.json'
        event_file.write_text('{SENSITIVE_SENTINEL')
        result = subprocess.run([sys.executable, str(ROOT / 'scripts/ci/revision.py'),
                                 '--event-file', str(event_file)], capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertNotIn('SENSITIVE_SENTINEL', result.stdout + result.stderr)
        output = json.loads(result.stdout)
        self.assertEqual('CI_CANDIDATE_UNAVAILABLE', output['reasonToken'])
        self.assertTrue(output['correlationId'])


if __name__ == '__main__':
    unittest.main()
