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
            def interrupt(kind,path,expected):
                self.assertNotEqual(path,output)
                self.assertIn('synthetic-native-secret-sentinel',path.read_text())
                self.assertFalse(output.exists())
                raise KeyboardInterrupt
            with patch('scripts.ci.jobs.parse_report',side_effect=interrupt):
                with self.assertRaises(KeyboardInterrupt):publish_report(report,output,identity)
            self.assertFalse(output.exists())

    def test_valid_native_payload_is_normalized_before_atomic_publication(self):
        from scripts.ci.jobs import publish_report
        identity=RunIdentity('run','1','a'*40,'b'*40,None)
        policy=json.loads(Path('tools/ci/coverage.json').read_text())
        native=policy['suites']['non-compose']['required']
        local=policy['suites']['local']['required']
        trx='<TestRun><Results>'+''.join('<UnitTestResult testName='+quoteattr(name)+' outcome="Passed" />' for name in native)+'</Results><ResultSummary outcome="Completed"><Counters total="'+str(len(native))+'" passed="'+str(len(native))+'" failed="0" /></ResultSummary><Output>synthetic-native-secret-sentinel</Output></TestRun>'
        report={'job':'backend','identity':vars(identity),'status':'success','command_id':'backend',
                'elapsed_seconds':1,'tools':{'python':'3.15.2'},'artifacts':[],
                'suites':[{'name':'non-compose','format':'trx','report':trx},
                          {'name':'local','tests':[{'id':name,'outcome':'passed'} for name in local]}]}
        with tempfile.TemporaryDirectory() as directory:
            output=Path(directory)/'summary.json'
            publish_report(report,output,identity)
            text=output.read_text()
            self.assertNotIn('synthetic-native-secret-sentinel',text)
            self.assertNotIn('<TestRun>',text)
            self.assertEqual(json.loads(text)['total'],len(native)+len(local))
