import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from scripts.ci.summary import validate_coverage
from scripts.ci.summary import RunIdentity, ReportError, parse_report

IDENTITY = RunIdentity('123', '1', 'a'*40, 'b'*40, None)

def report(job='backend'):
    return dict(identity=dict(IDENTITY.__dict__), job=job, command_id='unit', status='success',
                elapsed_seconds=1, tools={'python':'3'}, artifacts=[],
                suites=[dict(name='local', tests=[dict(id='required', outcome='passed')])])

class SummaryTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        policy = Path(self.directory.name)/'policy.json'
        policy.write_text(json.dumps({'jobs':{'backend':['local']}, 'suites':{
            'local':{'minimum':1,'required':['required']}}}))
        self.patcher = patch('scripts.ci.summary.POLICY_PATH', policy)
        self.patcher.start()
        self.addCleanup(self.patcher.stop)

    def test_required_identity_cannot_be_replaced(self):
        data=report()
        data['suites'][0]['tests'][0]['id']='replacement'
        with self.assertRaises(ReportError): self.parse(data)

    def parse(self, data):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'report.json'
            path.write_text(json.dumps(data))
            return parse_report('normalized', path, IDENTITY)

    def test_valid_and_additional_tests(self):
        data=report()
        data['suites'][0]['tests'].append(dict(id='new', outcome='passed'))
        self.assertEqual(self.parse(data).total, 2)

    def test_malformed_wrong_run_sha_duplicate_and_skip(self):
        mutations=[lambda d:d.update(identity={}),
                   lambda d:d['identity'].update(run_id='other'),
                   lambda d:d['identity'].update(candidate_sha='c'*40),
                   lambda d:d['suites'][0]['tests'].append(d['suites'][0]['tests'][0]),
                   lambda d:d['suites'][0]['tests'][0].update(outcome='skipped'),
                   lambda d:d['suites'][0].update(tests=[])]
        for mutate in mutations:
            with self.subTest(mutate=mutate):
                data=report(); mutate(data)
                with self.assertRaises(ReportError) as error:self.parse(data)
                self.assertTrue(error.exception.reason_token.startswith('CI_'))
                self.assertTrue(error.exception.correlation_id)

    def test_simulation_allows_added_pass_but_rejects_removed_or_skipped_required(self):
        from scripts.ci.summary import validate_simulation
        policy={'suites':{'contract':{'minimum':2, 'required':['mod.C.test_pass','mod.C.test_skip']}},
                'simulation_skips':{'mod.C.test_skip':'missing prerequisite'}}
        output="test_pass (mod.C.test_pass) ... ok\ntest_skip (mod.C.test_skip) ... skipped 'missing prerequisite'\n"
        validate_simulation(output+"test_new (mod.C.test_new) ... ok\n", policy)
        for bad in (output.splitlines()[0], output.replace('missing prerequisite','unexplained'),
                    output.replace('test_pass (mod.C.test_pass) ... ok',
                                   "test_pass (mod.C.test_pass) ... skipped 'missing prerequisite'")):
            with self.assertRaises(ReportError): validate_simulation(bad,policy)

    def test_native_parsers_reject_empty_partial_and_skipped_reports(self):
        from scripts.ci.summary import parse_cases
        trx='<TestRun><Results><UnitTestResult testName="required" outcome="Passed" /></Results><ResultSummary><Counters total="1" passed="1" failed="0" /></ResultSummary></TestRun>'
        self.assertEqual(parse_cases('trx',trx),[{'id':'required','outcome':'passed'}])
        vitest={'numTotalTests':1,'testResults':[{'name':'/repo/apps/crm-web/a.test.ts',
            'assertionResults':[{'ancestorTitles':['suite'],'title':'case','status':'passed'}]}]}
        self.assertEqual(parse_cases('vitest',vitest)[0]['id'], 'apps/crm-web/a.test.ts::suite > case')
        browser={'suites':[{'file':'test.spec.ts','specs':[{'title':'case','tests':[
            {'projectName':'chromium','expectedStatus':'passed','results':[{'status':'passed'}]}]}]}]}
        self.assertEqual(parse_cases('playwright',browser)[0]['outcome'],'passed')
        for kind,data in [('trx',trx.replace('total="1"','total="2"')),
                          ('trx',trx.replace('Passed','NotExecuted')),
                          ('vitest',dict(vitest,numTotalTests=2)),
                          ('playwright',{'suites':[]}),('unknown',{})]:
            with self.subTest(kind=kind):
                with self.assertRaises(ReportError):parse_cases(kind,data)

    def test_native_trx_is_validated_inside_run_bound_envelope(self):
        data=report()
        data['suites']=[dict(name='local',format='trx',report=
            '<TestRun><Results><UnitTestResult testName="required" outcome="Passed" />'
            '</Results><ResultSummary><Counters total="1" passed="1" failed="0" />'
            '</ResultSummary></TestRun>')]
        self.assertEqual(self.parse(data).passed,1)
        data['suites'][0]['report']=data['suites'][0]['report'].replace('required','removed')
        with self.assertRaises(ReportError): self.parse(data)

    def test_native_global_errors_cannot_hide_behind_passing_tests(self):
        from scripts.ci.summary import parse_cases
        vitest={'numTotalTests':1,'success':False,'testResults':[{'name':'apps/crm-web/a.test.ts',
            'assertionResults':[{'ancestorTitles':[],'title':'case','status':'passed'}]}]}
        browser={'errors':[{'message':'untrusted'}],'suites':[{'file':'test.spec.ts',
            'specs':[{'title':'case','tests':[{'projectName':'chromium','expectedStatus':'passed',
                                            'results':[{'status':'passed'}]}]}]}]}
        trx='<TestRun><Results><UnitTestResult testName="required" outcome="Passed" /></Results><ResultSummary outcome="Failed"><Counters total="1" passed="1" failed="0" error="1" /></ResultSummary></TestRun>'
        for kind,data in [('trx',trx),('vitest',vitest),('playwright',browser)]:
            with self.subTest(kind=kind):
                with self.assertRaises(ReportError):parse_cases(kind,data)

    def test_missing_and_raw_payload_are_safe(self):
        with self.assertRaises(ReportError) as error:
            parse_report('normalized', Path('/missing'), IDENTITY)
        self.assertNotIn('/missing', str(error.exception))
        with self.assertRaises(ReportError): self.parse({'secret':'SENTINEL'})
