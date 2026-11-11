import json
from pathlib import Path
import tempfile
import unittest

class CompletionTests(unittest.TestCase):
    def test_step_failures_never_create_success_and_do_not_echo_payload(self):
        from scripts.ci.completion import complete
        for name, outcome, expected in [('setup','failure','CI_SETUP_FAILED'),('checkout','failure','CI_ACTION_FAILED'),('gate','cancelled','CI_COMMAND_CANCELLED'),('gate','skipped','CI_STEP_NON_SUCCESS')]:
            with self.subTest(name=name,outcome=outcome),tempfile.TemporaryDirectory() as directory:
                path=Path(directory)/'backend.json'
                path.write_text('sensitive-sentinel')
                token=complete('backend',path,{name:{'outcome':outcome,'outputs':{'unsafe':'sensitive-sentinel'}}})
                self.assertEqual(token,expected)
                self.assertNotIn('sensitive-sentinel',path.read_text())
                self.assertEqual(json.loads(path.read_text())['status'],'failure')

    def test_missing_gate_cannot_pass_even_with_success_setup(self):
        from scripts.ci.completion import complete
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'backend.json'
            self.assertEqual(complete('backend',path,{'setup':{'outcome':'success'}}),'CI_STEP_NON_SUCCESS')

    def test_invalid_cli_arguments_never_echo_input(self):
        import subprocess,sys
        for module in ('completion','security'):
            result=subprocess.run([sys.executable,'-m','scripts.ci.'+module,'--unsafe-sensitive-sentinel'],capture_output=True,text=True,timeout=20)
            self.assertNotEqual(result.returncode,0)
            self.assertEqual(result.stderr,'')
            self.assertNotIn('sensitive-sentinel',result.stdout)
            self.assertTrue(json.loads(result.stdout)['reasonToken'].startswith('CI_'))

    def test_valid_native_success_and_failure_are_republished_without_diagnostics(self):
        from unittest.mock import patch
        from scripts.ci.completion import complete
        from scripts.ci.summary import parse_report
        from test_summary import report,IDENTITY
        from xml.sax.saxutils import quoteattr
        sentinel='synthetic-completion-private-sentinel'
        for status in ('success','failure'):
            with self.subTest(status=status),tempfile.TemporaryDirectory() as directory:
                root=Path(directory);output=root/'backend.json';policy=root/'policy.json'
                policy.write_text(json.dumps({'jobs':{'backend':['local']},'suites':{'local':{'minimum':1,'required':['required']}}}))
                data=report();data['status']=status
                outcome='Passed' if status=='success' else 'Failed'
                passed=int(status=='success');failed=1-passed
                trx='<TestRun><Results><UnitTestResult testName="required" outcome='+quoteattr(outcome)+'><Output>'+sentinel+'</Output></UnitTestResult></Results><ResultSummary outcome="Completed"><Counters total="1" passed="'+str(passed)+'" failed="'+str(failed)+'" /></ResultSummary><Output>'+sentinel+'</Output></TestRun>'
                data.update(raw_diagnostics=sentinel,artifacts=[sentinel],reason_token=sentinel,correlation_id=sentinel)
                data['tools']['raw_diagnostics']=sentinel
                data['suites']=[{'name':'local','format':'trx','report':trx,'raw_diagnostics':sentinel}]
                data['image_evidence'][0]['raw_diagnostics']=sentinel
                data['image_evidence'][0]['scan']['raw_diagnostics']=sentinel
                if status=='failure':
                    # Failed reports also contain scanner-native nested extensions.
                    import hashlib
                    scan=data['image_evidence'][0]['scan']
                    scan.update(status='blocked',findings=[{'kind':'vulnerability','finding_id':'d'*64,'verdict':'blocked','raw_diagnostics':sentinel}])
                    scan['sbom']['components'][0]['description']=sentinel
                    scan['sbom']['metadata']['raw_diagnostics']=sentinel
                    scan['sbom']['metadata']['tools']['components'][0]['raw_diagnostics']=sentinel
                    scan['sbomSha256']=hashlib.sha256(json.dumps(scan['sbom'],sort_keys=True,separators=(',',':')).encode()).hexdigest()
                output.write_text(json.dumps(data))
                with patch('scripts.ci.summary.POLICY_PATH',policy):
                    # Parser acceptance is deliberately insufficient for publication.
                    before=parse_report('normalized',output,IDENTITY)
                    token=complete('backend',output,{'gate':{'outcome':status}},IDENTITY)
                    payload=output.read_text()
                    self.assertTrue(sentinel not in payload,'CI_PUBLICATION_DIAGNOSTICS_EXPOSED')
                    self.assertNotIn('<TestRun>',payload)
                    self.assertEqual(parse_report('normalized',output,IDENTITY),before)
                clean=json.loads(payload)
                self.assertEqual(token,'CI_JOB_COMPLETE' if status=='success' else 'CI_JOB_FAILED')
                self.assertEqual(clean['artifacts'],[])
                self.assertEqual(clean['suites'],[{'name':'local','tests':[{'id':'required','outcome':'passed' if passed else 'failed'}]}])

    def test_security_nested_evidence_is_rebuilt_before_publication(self):
        import hashlib
        from scripts.ci.completion import complete
        from scripts.ci.sbom import source_sbom
        from scripts.ci.summary import RunIdentity,parse_report
        run=RunIdentity('7','1','a'*40,'b'*40,None)
        sentinel='synthetic-nested-completion-private-sentinel'
        for status in ('success','failure'):
            with self.subTest(status=status),tempfile.TemporaryDirectory() as directory:
                output=Path(directory)/'security.json'
                sbom=source_sbom({'package-lock.json':{('safe','1.0.0')}},run,'1.0.0')
                sbom['components'][0]['description']=sentinel
                sbom['metadata']['raw_diagnostics']=sentinel
                sbom['metadata']['tools']['components'][0]['raw_diagnostics']=sentinel
                source={'identity':vars(run),'status':'inventory-only','coverageGaps':[],'lockCount':1,
                        'sbom':sbom,'sbomSha256':hashlib.sha256(json.dumps(sbom,sort_keys=True,separators=(',',':')).encode()).hexdigest(),'raw_diagnostics':sentinel}
                scan={'identity':vars(run),'status':'pass','scanned_count':1,'findings':[],'reason_token':'CI_SCAN_OK','raw_diagnostics':sentinel}
                if status=='failure':
                    scan.update(status='blocked',findings=[{'kind':'secret','finding_id':'c'*64,'verdict':'blocked','raw_diagnostics':sentinel}])
                data={'identity':vars(run),'job':'security','command_id':'security','status':status,
                      'tools':{'semgrep':'1.185.0'},'artifacts':[],'elapsed_seconds':1,
                      'security_evidence':scan,'source_evidence':source,'raw_diagnostics':sentinel,
                      'suites':[{'name':'security','tests':[{'id':'repository-security','outcome':'passed' if status=='success' else 'failed','raw_diagnostics':sentinel}]}]}
                output.write_text(json.dumps(data));before=parse_report('normalized',output,run)
                token=complete('security',output,{'gate':{'outcome':status}},run)
                self.assertTrue(sentinel not in output.read_text(),'CI_PUBLICATION_DIAGNOSTICS_EXPOSED')
                self.assertEqual(token,'CI_JOB_COMPLETE' if status=='success' else 'CI_JOB_FAILED')
                self.assertEqual(parse_report('normalized',output,run),before)
                clean=json.loads(output.read_text())
                self.assertEqual(clean['source_evidence']['sbom'],source_sbom({'package-lock.json':{('safe','1.0.0')}},run,'1.0.0'))

    def test_allowed_license_producer_survives_completion_and_unsafe_rows_fail(self):
        import copy, hashlib
        from scripts.ci import scan
        from scripts.ci.completion import complete
        from scripts.ci.sbom import source_sbom
        from scripts.ci.summary import parse_report
        from test_scan import RUN
        rows,_=scan.parse_findings('trivy',{'SchemaVersion':2,'Results':[{'Class':'lang-pkgs','Target':'package-lock.json','Packages':[{'Name':'safe','Version':'1.0.0','Licenses':['MIT']}]}]})
        reports={kind:scan.ScanSummary(RUN,'pass','CI_SCAN_OK','00000000-0000-4000-8000-000000000000',tuple(rows) if kind=='trivy' else (),1) for kind in ('semgrep','trivy','gitleaks')}
        combined=scan.combine_scans(reports,(),RUN)
        self.assertEqual(combined.status,'pass')
        sbom=source_sbom({'package-lock.json':{('safe','1.0.0')}},RUN,'1.0.0')
        data={'identity':vars(RUN),'job':'security','command_id':'security','status':'success','tools':{'trivy':'0.76.2'},'artifacts':[],'elapsed_seconds':1,
              'security_evidence':combined.to_dict(),'source_evidence':{'identity':vars(RUN),'status':'inventory-only','coverageGaps':[],'lockCount':1,'sbom':sbom,'sbomSha256':hashlib.sha256(json.dumps(sbom,sort_keys=True,separators=(',',':')).encode()).hexdigest()},
              'suites':[{'name':'security','tests':[{'id':'repository-security','outcome':'passed'}]}]}
        mutations=[{'verdict':v} for v in ('blocked','incomplete','review-required','unknown')]+[{'kind':'unknown'},{'kind':'secret'},{'finding_id':'invalid'},{'extra':'untrusted'}]
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'security.json';path.write_text(json.dumps(data))
            self.assertEqual(complete('security',path,{'gate':{'outcome':'success'}},RUN),'CI_JOB_COMPLETE')
            self.assertEqual(parse_report('normalized',path,RUN).status,'success')
            self.assertEqual(json.loads(path.read_text())['security_evidence']['findings'],rows)
            for mutation in mutations:
                bad=copy.deepcopy(data);bad['security_evidence']['findings'][0].update(mutation)
                unsafe=scan.ScanSummary(RUN,'pass','CI_SCAN_OK','00000000-0000-4000-8000-000000000000',tuple(bad['security_evidence']['findings']),1)
                self.assertEqual(scan.combine_scans({**reports,'trivy':unsafe},(),RUN).status,'incomplete')
                path.write_text(json.dumps(bad))
                with self.subTest(mutation=mutation):
                    self.assertNotEqual(complete('security',path,{'gate':{'outcome':'success'}},RUN),'CI_JOB_COMPLETE')

    def test_interruption_withholds_original_report_from_always_upload(self):
        from unittest.mock import patch
        from scripts.ci.completion import complete
        from test_summary import report,IDENTITY
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);output=root/'backend.json';policy=root/'policy.json'
            policy.write_text(json.dumps({'jobs':{'backend':['local']},'suites':{'local':{'minimum':1,'required':['required']}}}))
            data=report();data['raw_diagnostics']='synthetic-interruption-private-sentinel'
            output.write_text(json.dumps(data))
            with patch('scripts.ci.summary.POLICY_PATH',policy),patch('scripts.ci.completion.normalized_report',side_effect=KeyboardInterrupt):
                with self.assertRaises(KeyboardInterrupt):complete('backend',output,{'gate':{'outcome':'success'}},IDENTITY)
            self.assertFalse(output.exists(),'CI_UNVALIDATED_ARTIFACT_LEFT_UPLOADABLE')

    def test_sigterm_during_initial_read_has_no_uploadable_original(self):
        import signal,subprocess,sys
        with tempfile.TemporaryDirectory() as directory:
            output=Path(directory)/'backend.json'
            output.write_text('synthetic-initial-read-private-sentinel')
            # Kill the real subprocess at its first payload read, before any
            # Python finally block or signal handler could remove the original.
            script='''
import os,signal,sys
from pathlib import Path
from scripts.ci.completion import complete
from scripts.ci.summary import RunIdentity
def terminated_read(path):
    os.kill(os.getpid(),signal.SIGTERM)
    raise AssertionError('SIGTERM did not terminate process')
Path.read_bytes=terminated_read
complete('backend',Path(sys.argv[1]),{'gate':{'outcome':'success'}},
         RunIdentity('1','1','a'*40,'b'*40,None))
'''
            result=subprocess.run([sys.executable,'-c',script,str(output)],capture_output=True,text=True,timeout=20)
            self.assertEqual(result.returncode,-signal.SIGTERM)
            self.assertEqual(result.stdout,'')
            self.assertEqual(result.stderr,'')
            self.assertFalse(output.exists(),'CI_INITIAL_READ_ORIGINAL_UPLOADABLE')
