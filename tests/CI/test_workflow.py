"""Workflow contract: mutations must make a mandatory gate fail closed."""
import copy
import importlib.util
from pathlib import Path
import unittest
import yaml

ROOT = Path(__file__).resolve().parents[2]

class WorkflowTests(unittest.TestCase):
    def test_workflow_contract_and_negative_mutations(self):
        path = ROOT / '.github/workflows/wave0.yml'
        self.assertTrue(path.exists(), 'mandatory Wave 0 workflow missing')
        from scripts.ci.workflow import validate
        workflow = yaml.safe_load(path.read_text())
        validate(workflow)
        for mutation in ('skip', 'sha', 'needs'):
            bad = copy.deepcopy(workflow)
            if mutation == 'skip': bad['jobs']['web-browser']['if'] = 'false'
            if mutation == 'sha': bad['jobs']['backend']['steps'][0]['uses'] = 'actions/checkout@v6'
            if mutation == 'needs': bad['jobs']['wave0-required']['needs'].remove('compose')
            with self.assertRaises(ValueError, msg=mutation): validate(bad)

    def test_old_required_status_retired(self):
        path = ROOT / '.github/workflows/contracts.yml'
        self.assertFalse(path.exists(), 'old contracts status must be retired explicitly')

    def test_missing_and_wrong_prerequisite_fail(self):
        from scripts.ci.jobs import verify_versions
        good = {'node':'24.22.2','npm':'11.19.1','python':'3.15.2','dotnet':'10.0.403','docker':'29.9.3','compose':'5.7.0'}
        verify_versions(good)
        for key in good:
            bad = dict(good); bad.pop(key)
            with self.assertRaises(ValueError): verify_versions(bad)
            bad = dict(good); bad[key] = '0.0.0'
            with self.assertRaises(ValueError): verify_versions(bad)

class RealProducerTests(unittest.TestCase):
    def test_real_discovery_records_fail_and_skip_without_payloads(self):
        import json
        import subprocess
        import sys
        import tempfile
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            (root/'test_probe.py').write_text('import unittest\nclass Probe(unittest.TestCase):\n def test_ok(self): pass\n def test_fail(self): self.fail("sensitive-probe-payload")\n @unittest.skip("sensitive-probe-payload")\n def test_skip(self): pass\n')
            output = root/'cases.json'
            result = subprocess.run([sys.executable,'-m','scripts.ci.unittest_report',str(root),str(output)],cwd=ROOT,capture_output=True,text=True)
            self.assertNotEqual(result.returncode,0)
            self.assertNotIn('sensitive-probe-payload',result.stdout+result.stderr+output.read_text())
            self.assertEqual(json.loads(result.stdout),{'total':3,'passed':1,'failed':1,'skipped':1})
            self.assertEqual({case['outcome'] for case in json.loads(output.read_text())},{'passed','failed','skipped'})

    def test_browser_real_stack_and_report_are_required(self):
        runner = (ROOT/'scripts/ci/jobs.py').read_text()
        self.assertIn("'--with-browser'",runner)
        self.assertIn("json.loads(browser.read_text())",runner)
        self.assertIn('PLAYWRIGHT_JSON_OUTPUT_NAME',(ROOT/'apps/crm-web/playwright.config.ts').read_text())

class TrustBootstrapTests(unittest.TestCase):
    def test_mvp_aggregate_defers_independent_trust_and_selects_explicit_mode(self):
        workflow = yaml.safe_load((ROOT/'.github/workflows/wave0.yml').read_text())
        steps = workflow['jobs']['wave0-required']['steps']
        blocked = [step for step in steps if 'CI_TRUST_NOT_PROVISIONED' in step.get('run','')]
        self.assertEqual(blocked,[])
        aggregate=next(step for step in steps if step.get('id')=='aggregate')
        self.assertIn('--mode mvp',aggregate['run'])
        for job in ('backend','compose','contracts-sdk','web-browser'):
            steps_job=workflow['jobs'][job]['steps']
            gate=next(step for step in steps_job if step.get('id')=='gate')
            self.assertIn('--mode mvp',gate['run'])
            setup=next(step for step in steps_job if step.get('id')=='setup')
            self.assertTrue(setup['run'].endswith(' mvp'))
        for job in ('backend','compose','contracts-sdk','web-browser'):
            pins=[step['with']['python-version'] for step in workflow['jobs'][job]['steps']
                  if step.get('uses','').startswith('actions/setup-python@')]
            self.assertEqual(pins,['3.15.2'])

class IsolationMutationTests(unittest.TestCase):
    def test_untrusted_workflow_mutations_are_rejected(self):
        from scripts.ci.workflow import validate
        source=yaml.safe_load((ROOT/'.github/workflows/wave0.yml').read_text())
        mutations=[
            lambda w:w['jobs']['security'].update(permissions={'contents':'write'}),
            lambda w:w['jobs']['security'].update(secrets='inherit'),
            lambda w:w['jobs']['security'].update(environment='production'),
            lambda w:w['jobs']['security']['steps'].append({'run':'echo ${{ secrets.TEST }}'}),
            lambda w:w['jobs']['security']['steps'].append({'run':'echo ${{ github.event.pull_request.title }}'}),
            lambda w:w['jobs']['security']['steps'].append({'run':"echo ${{ format('{0}', secrets.TEST) }}"}),
            lambda w:w['jobs']['security']['steps'].append({'run':"echo ${{ join(github.event.pull_request.title) }}"}),
            lambda w:w['jobs']['security']['steps'].append({'uses':'actions/cache@'+'a'*40}),
            lambda w:w['jobs']['security']['steps'].append({'uses':'actions/upload-artifact@'+'a'*40,'with':{'name':'raw','path':'.local/**','retention-days':90}}),
            lambda w:w['jobs']['wave0-required']['steps'].append({'run':'echo unsafe','if':'always()'}),
        ]
        for mutate in mutations:
            bad=copy.deepcopy(source);mutate(bad)
            with self.subTest(mutation=mutate),self.assertRaises(ValueError):validate(bad)

    def test_security_is_real_and_all_jobs_have_completion_diagnostics(self):
        workflow=yaml.safe_load((ROOT/'.github/workflows/wave0.yml').read_text())
        for name,job in workflow['jobs'].items():
            commands=[step.get('run','') for step in job['steps']]
            if name!='wave0-required':
                self.assertTrue(any('scripts.ci.completion' in command for command in commands),name)
        commands=[step.get('run','') for step in workflow['jobs']['security']['steps']]
        self.assertTrue(any('scripts.ci.security' in command for command in commands))
        self.assertFalse(any('CI_SECURITY_NOT_PROVISIONED' in command for command in commands))

    def test_mvp_defers_semgrep_but_hardening_keeps_hash_locked_runtime(self):
        workflow=yaml.safe_load((ROOT/'.github/workflows/wave0.yml').read_text())
        steps=workflow['jobs']['security']['steps']
        runtimes=[s for s in steps if s.get('id')=='scanner-python']
        self.assertEqual(runtimes,[])
        setup=(ROOT/'scripts/ci/setup.sh').read_text()
        self.assertIn('if [[ "${1:-}" == security && "${2:-hardening}" != mvp ]]; then',setup)
        self.assertIn('--require-hashes',setup)
        self.assertIn('semgrep-requirements.lock',setup)
