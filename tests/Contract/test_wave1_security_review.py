"""The MVP review applies only to the exact reviewed security change."""
import copy
import contextlib
import io
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import yaml
import prerequisites
from scripts.contracts import compatibility

ROOT = Path(__file__).resolve().parents[2]
BASE = '40bc2905b7750b139ff0607000127a3e5c06e2d8'


@unittest.skipUnless(prerequisites.OASDIFF[0], prerequisites.OASDIFF[1])
class Wave1SecurityReviewTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Exercise the exact reviewed Wave 1 revision, not the evolving Wave 2
        # checkout. This isolated clone is a fixture, never the gate baseline.
        cls.snapshot = tempfile.TemporaryDirectory()
        cls.repo = Path(cls.snapshot.name) / 'repo'
        subprocess.run(['git', 'clone', '--shared', '--no-checkout', str(ROOT), str(cls.repo)],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        subprocess.run(['git', '-C', str(cls.repo), 'checkout', '--detach',
                        '2c2851b8077a645e1c88b2c4e2dfd687e18ee3c7'],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    @classmethod
    def tearDownClass(cls):
        cls.snapshot.cleanup()

    def setUp(self):
        self.old = yaml.safe_load(subprocess.check_output(
            ['git', 'show', BASE + ':docs/contracts/crm-api.openapi.yaml'], cwd=self.repo))
        self.new = yaml.safe_load((self.repo / 'docs/contracts/crm-api.openapi.yaml').read_text())

    def reviewed(self, old=None, new=None, base=BASE, enabled=True, repo=None, now=1795737600):
        check = getattr(compatibility, 'reviewed_security_change', lambda *args, **kwargs: False)
        return check(base, 'docs/contracts/crm-api.openapi.yaml', old or self.old,
                     new or self.new, repo or self.repo, enabled, now=now)

    def test_exact_reviewed_change_is_accepted_only_in_mvp_before_expiry(self):
        self.assertTrue(self.reviewed())
        self.assertFalse(self.reviewed(enabled=False))
        self.assertFalse(self.reviewed(base='0' * 40))
        self.assertFalse(self.reviewed(now=1802304000))

    def test_additional_security_change_or_modified_review_evidence_is_rejected(self):
        changed = copy.deepcopy(self.new)
        changed['components']['securitySchemes']['bearerAuth']['scheme'] = 'basic'
        self.assertFalse(self.reviewed(new=changed))
        with patch.object(compatibility, 'sha', return_value='0' * 64):
            self.assertFalse(self.reviewed())

    def test_current_contact_contract_cannot_inherit_wave1_review(self):
        # Pin the later ADR-020/021 contract as a negative fixture too; an
        # unrelated future checkout must not change this historical assertion.
        changed = yaml.safe_load(subprocess.check_output(
            ['git', 'show', '4ee14a848cf66f1f09d0e2d92393521b5c514c94:docs/contracts/crm-api.openapi.yaml'], cwd=ROOT))
        self.assertFalse(self.reviewed(new=changed))

    def test_other_contract_cannot_inherit_review(self):
        check = getattr(compatibility, 'reviewed_security_change', lambda *args, **kwargs: False)
        self.assertFalse(check(BASE, 'docs/contracts/platform-operations.openapi.yaml', self.old,
                               self.new, self.repo, True, now=1795737600))

    def test_reviewed_break_is_reported_as_break_not_compatible(self):
        output = io.StringIO()
        with patch.object(compatibility, 'run', return_value={'reason': 'CONTRACT_BREAKING_REVIEWED'}), \
             patch('sys.argv', ['compatibility.py', '--mvp-reviewed-policy']), \
             contextlib.redirect_stdout(output):
            self.assertEqual(0, compatibility.main())
        self.assertEqual('reviewed-breaking', json.loads(output.getvalue())['classification'])

    def test_real_gate_keeps_hardening_closed_and_runs_sdk_after_mvp_review(self):
        # Exercise the historical review at a time within its approval window.
        with patch('time.time', return_value=1795737600), patch('scripts.contracts.sdk_surface.compare', return_value=True) as compare:
            self.assertEqual('SECURITY_REVIEW_REQUIRED', compatibility.run(BASE, self.repo, {}, False)['reason'])
            compare.assert_not_called()
            result = compatibility.run(BASE, self.repo, {}, True)
            self.assertEqual('CONTRACT_BREAKING_REVIEWED', result['reason'])
            self.assertFalse(result['securityReview']['independentVerification'])
            compare.assert_called_once()
