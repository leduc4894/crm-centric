"""Safety contract for an informational runner capability probe."""
from pathlib import Path
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / '.github/workflows/runner-cgroup-probe.yml'


class RunnerCgroupProbeWorkflowTests(unittest.TestCase):
    def test_probe_is_branch_scoped_unprivileged_and_does_not_run_candidate(self):
        self.assertTrue(WORKFLOW.is_file(), 'runner probe workflow missing')
        workflow = yaml.safe_load(WORKFLOW.read_text(encoding='utf-8'))
        triggers = workflow.get('on', workflow.get(True))
        self.assertEqual({'push': {'branches': ['wave-3']}}, triggers)
        self.assertEqual({'contents': 'read'}, workflow['permissions'])
        self.assertEqual({'cgroup-probe'}, set(workflow['jobs']))
        job = workflow['jobs']['cgroup-probe']
        self.assertEqual('ubuntu-24.04', job['runs-on'])
        self.assertLessEqual(job['timeout-minutes'], 5)
        self.assertNotIn('environment', job)
        self.assertNotIn('permissions', job)
        self.assertEqual(1, len(job['steps']))
        step = job['steps'][0]
        self.assertNotIn('uses', step)
        self.assertNotIn('env', step)
        script = step['run']
        self.assertNotIn('${{', script)
        for forbidden in ('checkout', 'sudo', 'curl', 'wget', 'GITHUB_TOKEN', 'secrets.', 'github.workspace'):
            self.assertNotIn(forbidden, script)
        for required in ('GITHUB_SHA', '/sys/fs/cgroup', 'cgroup.kill', 'start_new_session=True', 'json.dumps'):
            self.assertIn(required, script)


if __name__ == '__main__':
    unittest.main()
