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
        return {'locate': 'v2', 'populated': False}.get(operation)


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
