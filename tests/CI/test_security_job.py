import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

class SecurityJobTests(unittest.TestCase):
    def test_missing_scanner_fails_with_safe_summary(self):
        import os
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);out=root/'security.json';rev=root/'revision.json'
            head=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
            rev.write_text(json.dumps({'candidate_sha':head,'base_sha':head,'merge_sha':None}))
            result=subprocess.run([sys.executable,'-m','scripts.ci.security','--revision',str(rev),'--output',str(out)],env={**os.environ,'GITHUB_RUN_ID':'7','GITHUB_RUN_ATTEMPT':'1','CI_SCANNER_ROOT':str(root/'missing-sensitive-sentinel')},capture_output=True,text=True,timeout=30)
            self.assertNotEqual(result.returncode,0)
            self.assertNotIn('sensitive-sentinel',result.stdout+result.stderr)
            self.assertEqual(result.stderr,'')
            data=json.loads(out.read_text())
            self.assertEqual(data['status'],'failure')
            self.assertEqual(data['reason_token'],'CI_SCAN_INCOMPLETE')
            self.assertGreater(data['failed'],0)

    def test_security_success_requires_run_bound_nonempty_scan_evidence(self):
        from scripts.ci.summary import parse_report,ReportError,RunIdentity
        run=RunIdentity('7','1','a'*40,'b'*40,None)
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'security.json'
            data={'identity':vars(run),'job':'security','command_id':'security','status':'success',
                  'tools':{'test':'1'},'artifacts':[],'elapsed_seconds':1,
                  'suites':[{'name':'security','tests':[{'id':'repository-security','outcome':'passed'}]}]}
            for evidence in (None,{'identity':vars(run),'status':'pass','scanned_count':0},
                             {'identity':{**vars(run),'run_id':'other'},'status':'pass','scanned_count':1}):
                data['security_evidence']=evidence;path.write_text(json.dumps(data))
                with self.subTest(evidence=evidence),self.assertRaises(ReportError):parse_report('normalized',path,run)

    def test_complete_security_evidence_and_source_coverage_are_both_required(self):
        import copy,hashlib
        from scripts.ci.summary import parse_report,ReportError,RunIdentity
        run=RunIdentity('7','1','a'*40,'b'*40,None)
        sbom={'components':[{'name':'fixture','version':'1'}]}
        source={'identity':vars(run),'status':'inventory-only','coverageGaps':[],'lockCount':1,
                'sbom':sbom,'sbomSha256':hashlib.sha256(json.dumps(sbom,sort_keys=True,separators=(',',':')).encode()).hexdigest()}
        data={'identity':vars(run),'job':'security','command_id':'security','status':'success',
              'tools':{'fixture':'1'},'artifacts':[],'elapsed_seconds':1,
              'security_evidence':{'identity':vars(run),'status':'pass','scanned_count':1,'findings':[],'reason_token':'CI_SCAN_OK'},
              'source_evidence':source,'suites':[{'name':'security','tests':[{'id':'repository-security','outcome':'passed'}]}]}
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'security.json';path.write_text(json.dumps(data))
            self.assertEqual(parse_report('normalized',path,run).total,1)
            for key,value in [('status','incomplete'),('coverageGaps',['CI_TOOL_DEPENDENCY_COVERAGE']),('sbomSha256','0'*64),('lockCount',0),('identity',{**vars(run),'run_id':'other'})]:
                bad=copy.deepcopy(data);bad['source_evidence'][key]=value;path.write_text(json.dumps(bad))
                with self.subTest(key=key),self.assertRaises(ReportError):parse_report('normalized',path,run)
