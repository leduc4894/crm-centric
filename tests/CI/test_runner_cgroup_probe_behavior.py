"""Probe cleanup contract; fake backend never touches a real cgroup."""
import importlib.util
from pathlib import Path
import unittest


SOURCE = Path(__file__).resolve().parents[2] / '.github/probes/runner_cgroup_probe.py'


def load_probe():
    spec = importlib.util.spec_from_file_location('runner_probe', SOURCE)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class FakeBackend:
    def __init__(self, failure=None):
        self.failure = failure
        self.calls = []
    def perform(self, operation):
        self.calls.append(operation)
        if operation == self.failure:
            raise OSError('fixture failure')
        return {'locate': 'v2', 'populated': False, 'verify_live': True, 'reap': -9}.get(operation)


class ProbeBehaviorTests(unittest.TestCase):
    def test_spawn_then_bad_pid_still_kills_reaps_and_removes(self):
        probe = load_probe()
        fake = FakeBackend('read_pid')
        result = probe.run_probe(fake)
        self.assertEqual(['locate', 'create', 'spawn', 'attach', 'read_pid',
                          'kill', 'populated', 'reap', 'remove'], fake.calls)
        self.assertEqual('failed', result['kill_tree'])
        self.assertEqual('removed', result['cleanup'])

    def test_existing_group_is_never_cleaned_or_removed(self):
        probe = load_probe()
        fake = FakeBackend('create')
        result = probe.run_probe(fake)
        self.assertEqual(['locate', 'create'], fake.calls)
        self.assertEqual('not_needed', result['cleanup'])

    def test_failed_kill_never_removes_and_marks_cleanup_failure(self):
        probe = load_probe()
        fake = FakeBackend('kill')
        result = probe.run_probe(fake)
        self.assertIn('kill', fake.calls)
        self.assertNotIn('remove', fake.calls)
        self.assertIn('reap', fake.calls)
        self.assertEqual('failed', result['cleanup'])

    def test_live_membership_checks_parent_child_and_population(self):
        probe = load_probe()
        self.assertTrue(probe.has_live_tree(None, 'populated 1\n', '100\n101\n', 100, 101))
        for status, events, members in ((0, 'populated 1\n', '100\n101\n'),
                                         (None, 'populated 0\n', '100\n101\n'),
                                         (None, 'populated 1\n', '100\n'),
                                         (None, 'populated 1\n', '101\n')):
            self.assertFalse(probe.has_live_tree(status, events, members, 100, 101))

    def test_empty_or_exited_before_kill_is_not_termination_success(self):
        probe = load_probe()
        for bad_state in ('empty', 'parent_exited'):
            class BeforeKill(FakeBackend):
                def perform(self, operation):
                    if operation == 'verify_live':
                        self.calls.append(operation)
                        raise ValueError(bad_state)
                    return super().perform(operation)
            fake = BeforeKill()
            result = probe.run_probe(fake)
            self.assertIn('kill', fake.calls)
            self.assertEqual('failed', result['kill_tree'])
            self.assertEqual('removed', result['cleanup'])

    def test_membership_mismatch_is_not_termination_success(self):
        probe = load_probe()
        class WrongGroup(FakeBackend):
            def perform(self, operation):
                if operation == 'verify_live':
                    self.calls.append(operation)
                    raise ValueError('membership mismatch')
                return super().perform(operation)
        fake = WrongGroup()
        result = probe.run_probe(fake)
        self.assertEqual('failed', result['kill_tree'])
        self.assertIn('kill', fake.calls)
        self.assertEqual('removed', result['cleanup'])

    def test_success_requires_live_tree_and_killed_parent(self):
        probe = load_probe()
        fake = FakeBackend()
        result = probe.run_probe(fake)
        self.assertLess(fake.calls.index('verify_live'), fake.calls.index('kill'))
        self.assertEqual('terminated', result['kill_tree'])
        self.assertEqual('removed', result['cleanup'])

    def test_parent_exit_not_sigkill_never_reports_success(self):
        probe = load_probe()
        class WrongExit(FakeBackend):
            def perform(self, operation):
                if operation == 'reap':
                    self.calls.append(operation)
                    return 0
                return super().perform(operation)
        fake = WrongExit()
        result = probe.run_probe(fake)
        self.assertEqual('failed', result['kill_tree'])
        self.assertEqual('removed', result['cleanup'])

    def test_unsupported_mount_is_reported_without_attempting_creation(self):
        probe = load_probe()
        class Unsupported(FakeBackend):
            def perform(self, operation):
                self.calls.append(operation)
                raise probe.UnsupportedMount()
        fake = Unsupported()
        result = probe.run_probe(fake)
        self.assertEqual(['locate'], fake.calls)
        self.assertEqual('unsupported', result['mount_layout'])
        self.assertEqual('not_needed', result['cleanup'])

    def test_nested_mount_root_is_explicitly_unsupported(self):
        probe = load_probe()
        line = '29 23 0:26 /nested /sys/fs/cgroup rw - cgroup2 cgroup rw'
        self.assertIsNone(probe.resolve_cgroup_parent(line, '0::/nested/job'))
        self.assertEqual('/sys/fs/cgroup/job', probe.resolve_cgroup_parent(
            '29 23 0:26 / /sys/fs/cgroup rw - cgroup2 cgroup rw', '0::/job'))
        self.assertIsNone(probe.resolve_cgroup_parent(
            '29 23 0:26 / /sys/fs/cgroup rw - cgroup2 cgroup rw', '0::/../escape'))


if __name__ == '__main__':
    unittest.main()
