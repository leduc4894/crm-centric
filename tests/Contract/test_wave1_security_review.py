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
    def setUp(self):
        self.old = yaml.safe_load(subprocess.check_output(
            ['git', 'show', BASE + ':docs/contracts/crm-api.openapi.yaml'], cwd=ROOT))
        self.new = yaml.safe_load((ROOT / 'docs/contracts/crm-api.openapi.yaml').read_text())

    def reviewed(self, old=None, new=None, base=BASE, enabled=True, repo=ROOT, now=1795737600):
        check = getattr(compatibility, 'reviewed_security_change', lambda *args, **kwargs: False)
        return check(base, 'docs/contracts/crm-api.openapi.yaml', old or self.old,
                     new or self.new, repo, enabled, now=now)

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

    def test_other_contract_cannot_inherit_review(self):
        check = getattr(compatibility, 'reviewed_security_change', lambda *args, **kwargs: False)
        self.assertFalse(check(BASE, 'docs/contracts/platform-operations.openapi.yaml', self.old,
                               self.new, ROOT, True, now=1795737600))

    def test_reviewed_break_is_reported_as_break_not_compatible(self):
        output = io.StringIO()
        with patch.object(compatibility, 'run', return_value={'reason': 'CONTRACT_BREAKING_REVIEWED'}), \
             patch('sys.argv', ['compatibility.py', '--mvp-reviewed-policy']), \
             contextlib.redirect_stdout(output):
            self.assertEqual(0, compatibility.main())
        self.assertEqual('reviewed-breaking', json.loads(output.getvalue())['classification'])

    def test_real_gate_keeps_hardening_closed_and_runs_sdk_after_mvp_review(self):
        with patch('scripts.contracts.sdk_surface.compare', return_value=True) as compare:
            self.assertEqual('SECURITY_REVIEW_REQUIRED', compatibility.run(BASE, ROOT, {}, False)['reason'])
            compare.assert_not_called()
            result = compatibility.run(BASE, ROOT, {}, True)
            self.assertEqual('CONTRACT_BREAKING_REVIEWED', result['reason'])
            self.assertFalse(result['securityReview']['independentVerification'])
            compare.assert_called_once()
