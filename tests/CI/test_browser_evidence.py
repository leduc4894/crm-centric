"""Both browser runs must contribute passing, distinct test evidence."""
import copy
import unittest
from scripts.ci import jobs
from scripts.ci.summary import ReportError, parse_cases


def report(title, file='health.spec.ts'):
    return {'errors': [], 'stats': {'expected': 1, 'unexpected': 0, 'skipped': 0, 'flaky': 0},
            'suites': [{'file': file, 'specs': [{'title': title, 'tests': [{
                'projectName': 'chromium', 'expectedStatus': 'passed', 'status': 'expected',
                'results': [{'status': 'passed'}]}]}]}]}


class BrowserEvidenceTests(unittest.TestCase):
    def test_combines_regular_and_real_provider_runs(self):
        merge = getattr(jobs, 'merge_browser_evidence', lambda *args: None)
        regular = report('regular')
        provider = report('real provider signs in isolated tenants and terminates provider session',
                          'browser-oidc.spec.ts')
        combined = merge(regular, provider)
        self.assertIsNotNone(combined)
        self.assertEqual(2, len(parse_cases('playwright', combined)))

    def test_rejects_missing_provider_and_any_skips(self):
        merge = getattr(jobs, 'merge_browser_evidence', lambda *args: None)
        regular = report('regular')
        provider = report('real provider signs in isolated tenants and terminates provider session',
                          'browser-oidc.spec.ts')
        invalid = copy.deepcopy(provider)
        invalid['stats']['skipped'] = 1
        for bad in (invalid, report('unrelated', 'browser-oidc.spec.ts')):
            with self.subTest(bad=bad), self.assertRaises(ReportError): merge(regular, bad)
