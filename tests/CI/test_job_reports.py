"""Regressions for exact harness inputs and cancellation-safe publication."""
import json
from pathlib import Path
import tempfile
import unittest
from xml.sax.saxutils import quoteattr
from unittest.mock import patch

from scripts.ci.summary import RunIdentity, ReportError

class JobReportTests(unittest.TestCase):
    def test_harnesses_receive_exact_identity_and_real_browser(self):
        from scripts.ci.jobs import plan3_commands
        identity=RunIdentity('run','1','a'*40,'b'*40,None)
        commands=plan3_commands(identity)
        self.assertEqual(len(commands),5)
        self.assertIn(['bash','tests/Plan3/clean-export.sh','--sha','a'*40,
                       '--base-ref','b'*40,'--with-browser'],commands)
        for harness in ('negative-cases.sh','aggregate-reporting.sh'):
            self.assertIn(['bash','tests/Plan3/'+harness,'--base-ref','b'*40],commands)

    def test_interruption_during_validation_cannot_publish_raw_native_payload(self):
        from scripts.ci.jobs import publish_report
        identity=RunIdentity('run','1','a'*40,'b'*40,None)
        with tempfile.TemporaryDirectory() as directory:
            output=Path(directory)/'summary.json'
            report={'job':'backend','suites':[{'name':'non-compose','format':'trx',
                       'report':'synthetic-native-secret-sentinel'}]}
            def interrupt(kind,path,expected,mode="hardening"):
                self.assertNotEqual(path,output)
                self.assertIn('synthetic-native-secret-sentinel',path.read_text())
                self.assertFalse(output.exists())
                raise KeyboardInterrupt
            with patch('scripts.ci.jobs.parse_report',side_effect=interrupt):
                with self.assertRaises(KeyboardInterrupt):publish_report(report,output,identity)
            self.assertFalse(output.exists())

    def test_valid_native_payload_is_normalized_before_atomic_publication(self):
        from scripts.ci.jobs import publish_report
        from scripts.ci.image_identity import ImageIdentity
        from test_image_identity import evidence
        identity=RunIdentity('123','1','a'*40,'b'*40,None)
        policy=json.loads(Path('tools/ci/coverage.json').read_text())
        native=policy['suites']['non-compose']['required']
        local=policy['suites']['local']['required']
        trx='<TestRun><Results>'+''.join('<UnitTestResult testName='+quoteattr(name)+' outcome="Passed" />' for name in native)+'</Results><ResultSummary outcome="Completed"><Counters total="'+str(len(native))+'" passed="'+str(len(native))+'" failed="0" /></ResultSummary><Output>synthetic-native-secret-sentinel</Output></TestRun>'
        report={'job':'backend','identity':vars(identity),'status':'success','command_id':'backend',
                'elapsed_seconds':1,'tools':{'python':'3.15.2'},'artifacts':[],
                'suites':[{'name':'non-compose','format':'trx','report':trx},
                          {'name':'local','tests':[{'id':name,'outcome':'passed'} for name in local]}]}
        image=ImageIdentity('backend','123','sha256:'+'c'*64,'linux/amd64',None)
        report['image_evidence']=[{'role':'runtime','testedIdentity':vars(image),'scan':evidence(image)}]
        with tempfile.TemporaryDirectory() as directory:
            output=Path(directory)/'summary.json'
            publish_report(report,output,identity)
            text=output.read_text()
            self.assertNotIn('synthetic-native-secret-sentinel',text)
            self.assertNotIn('<TestRun>',text)
            self.assertEqual(json.loads(text)['total'],len(native)+len(local))

class BrowserProvisioningTests(unittest.TestCase):
    def test_privileged_dependencies_are_setup_and_runner_verifies_real_browser(self):
        setup=Path('scripts/ci/setup.sh').read_text()
        runner=Path('scripts/ci/jobs.py').read_text()
        self.assertIn('--with-deps',setup)
        self.assertNotIn('--with-deps',runner)
        self.assertIn('chromium.launch',runner)
        self.assertIn('browser.close',runner)

class MvpExecutionTests(unittest.TestCase):
    def test_mvp_scope_removes_inherited_image_hooks_and_restores_environment(self):
        import os
        from scripts.ci.jobs import execution_mode
        with patch.dict(os.environ,{'CI_IMAGE_EVIDENCE':'unavailable','CI_IMAGE_JOB':'backend'}):
            with execution_mode('mvp','backend',Path('/unused')):
                self.assertNotIn('CI_IMAGE_EVIDENCE',os.environ)
                self.assertNotIn('CI_IMAGE_JOB',os.environ)
            self.assertEqual(os.environ['CI_IMAGE_EVIDENCE'],'unavailable')
        with execution_mode('hardening','backend',Path('/evidence')):
            self.assertEqual(os.environ['CI_IMAGE_EVIDENCE'],'/evidence')

    def test_mvp_setup_does_not_provision_scanners_for_test_jobs(self):
        # Shell execution with inert prerequisite commands proves the branch,
        # without installing tools or requiring scanner downloads.
        import subprocess
        with tempfile.TemporaryDirectory() as directory:
            script=Path(directory)/'setup.sh'
            source=Path('scripts/ci/setup.sh').read_text()
            start=source.index('if [[ "${2:-hardening}"')
            end=source.index('case "${1:-}"')
            script.write_text('set -eu\npython() { echo "$*"; }\n'+source[start:end])
            for job in ('backend','compose','contracts-sdk','web-browser'):
                result=subprocess.run(['bash',str(script),job,'mvp'],capture_output=True,text=True)
                self.assertEqual(result.returncode,0)
                self.assertNotIn('provision_scanners',result.stdout)
