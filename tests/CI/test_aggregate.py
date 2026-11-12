import unittest
import subprocess
import sys
import tempfile
from pathlib import Path
from scripts.ci.summary import parse_report
import json
from unittest.mock import patch
from dataclasses import replace
from scripts.ci.aggregate import aggregate, INVENTORY
from scripts.ci.summary import CiSummary, ReportError
from test_summary import IDENTITY, report

class AggregateTests(unittest.TestCase):
    def evidence(self):
        return [CiSummary(job, IDENTITY, 'success', 1, 0, 0, 1) for job in INVENTORY]

    def test_complete_success(self):
        self.assertTrue(aggregate(dict.fromkeys(INVENTORY,'success'),self.evidence(),IDENTITY).success)

    def test_missing_duplicate_wrong_identity_and_failed_local(self):
        evidence=self.evidence()
        cases=[[],evidence[:-1],evidence+[evidence[0]],
               [replace(evidence[0],identity=replace(IDENTITY, run_id='other'))]+evidence[1:],
               [replace(evidence[0],failed=1,status='failure')]+evidence[1:]]
        for summaries in cases:
            result=aggregate(dict.fromkeys(INVENTORY,'success'),summaries,IDENTITY)
            self.assertFalse(result.success)
            self.assertTrue(result.reason_token.startswith('CI_'))
            self.assertTrue(result.correlation_id)

    def test_platform_results_are_authoritative(self):
        for status in ('failure','skipped','cancelled',None):
            needs=dict.fromkeys(INVENTORY,'success'); needs['backend']=status
            self.assertFalse(aggregate(needs,self.evidence(),IDENTITY).success)
        self.assertFalse(aggregate({},self.evidence(),IDENTITY).success)

    def test_cli_missing_evidence_exits_nonzero_with_safe_envelope(self):
        result=subprocess.run([sys.executable,'-m','scripts.ci.aggregate','--unsafe-SENTINEL'],
                              capture_output=True,text=True,timeout=20)
        self.assertNotEqual(result.returncode,0)
        self.assertNotIn('SENTINEL',result.stdout+result.stderr)
        self.assertEqual(result.stderr,'')
        data=json.loads(result.stdout)
        self.assertFalse(data['success'])
        self.assertTrue(data['reason_token'].startswith('CI_'))
        self.assertTrue(data['correlation_id'])

    def test_cli_oversized_elapsed_integer_fails_without_traceback(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            identity=root/'identity.json';identity.write_text(json.dumps(IDENTITY.__dict__))
            needs=root/'needs.json';needs.write_text(json.dumps(dict.fromkeys(INVENTORY,'success')))
            path=root/'report.json';data=report();data['elapsed_seconds']=10**400
            path.write_text(json.dumps(data))
            result=subprocess.run([sys.executable,'-m','scripts.ci.aggregate',
                                   '--identity',str(identity),'--needs',str(needs),
                                   '--reports',str(path)],capture_output=True,text=True,timeout=20)
            self.assertNotEqual(result.returncode,0)
            self.assertEqual(result.stderr,'')
            envelope=json.loads(result.stdout)
            self.assertFalse(envelope['success'])
            self.assertTrue(envelope['reason_token'].startswith('CI_'))
            self.assertTrue(envelope['correlation_id'])

    def test_deliberately_failing_local_test_fails_aggregate(self):
        # Real unittest process, same discovery entry point as the Local job.
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            local=root/'tests/Local'; local.mkdir(parents=True)
            (local/'test_deliberate.py').write_text(
                'import unittest\nclass LocalFailure(unittest.TestCase):\n'
                ' def test_deliberate(self): self.fail("controlled failure")\n')
            result=subprocess.run([sys.executable,'-m','unittest','discover','-s',str(local)],
                                  capture_output=True, timeout=20)
            self.assertNotEqual(result.returncode,0)
            policy=root/'policy.json'
            policy.write_text(json.dumps({'jobs':{'backend':['local']}, 'suites':{
                'local':{'minimum':1,'required':['test_deliberate.LocalFailure.test_deliberate']}}}))
            report=root/'report.json'
            report.write_text(json.dumps(dict(identity=dict(IDENTITY.__dict__),job='backend',
                command_id='local',status='failure',elapsed_seconds=1,tools={'python':'3'},
                artifacts=[],suites=[dict(name='local',tests=[dict(
                    id='test_deliberate.LocalFailure.test_deliberate',outcome='failed')])])))
            with patch('scripts.ci.summary.POLICY_PATH',policy):
                summary=parse_report('normalized',report,IDENTITY)
            evidence=[summary]+self.evidence()[1:]
            self.assertFalse(aggregate(dict.fromkeys(INVENTORY,'success'),evidence,IDENTITY).success)

    def test_every_actual_workflow_job_is_authoritative_and_zero_tests_fail(self):
        import yaml
        workflow=yaml.safe_load((Path(__file__).resolve().parents[2]/'.github/workflows/wave0.yml').read_text())
        actual=set(workflow['jobs'])-{'wave0-required'}
        self.assertEqual(actual,set(INVENTORY))
        for job in actual:
            for outcome in ('failure','skipped','cancelled',None):
                needs=dict.fromkeys(actual,'success');needs[job]=outcome
                with self.subTest(job=job,outcome=outcome):
                    self.assertFalse(aggregate(needs,self.evidence(),IDENTITY).success)
            evidence=[replace(item,total=0,passed=0) if item.job==job else item for item in self.evidence()]
            self.assertFalse(aggregate(dict.fromkeys(actual,'success'),evidence,IDENTITY).success)
        # A scheduler that never runs the aggregator emits no successful check;
        # no synthetic outcome may substitute for cancelled/missing prerequisites.
        self.assertFalse(aggregate({},[],IDENTITY).success)

    def test_mvp_complete_reports_without_images_and_security_still_mandatory(self):
        import copy,hashlib
        from scripts.ci.completion import complete
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);policy=root/'policy.json'
            policy.write_text(json.dumps({'jobs':{job:['local'] for job in INVENTORY},
                'suites':{'local':{'minimum':1,'required':['required']}}}))
            data=[]
            for job in INVENTORY:
                item=report(job);item.pop('image_evidence')
                if job in ('backend','compose','web-browser'):
                    item['image_hardening']={'status':'not-evaluated'}
                if job=='security':
                    sbom={'components':[{'name':'fixture','version':'1'}]}
                    item['security_evidence']={'identity':vars(IDENTITY),'status':'pass',
                        'scanned_count':1,'findings':[],'reason_token':'CI_SCAN_OK'}
                    item['source_evidence']={'identity':vars(IDENTITY),'status':'inventory-only',
                        'coverageGaps':[],'lockCount':1,'sbom':sbom,
                        'sbomSha256':hashlib.sha256(json.dumps(sbom,sort_keys=True,separators=(',',':')).encode()).hexdigest()}
                data.append(item)
            def summaries(items):
                result=[]
                for item in items:
                    path=root/(item['job']+'.json');path.write_text(json.dumps(item))
                    result.append(parse_report('normalized',path,IDENTITY,mode='mvp'))
                return result
            needs=dict.fromkeys(INVENTORY,'success')
            with patch('scripts.ci.summary.POLICY_PATH',policy):
                self.assertTrue(aggregate(needs,summaries(data),IDENTITY).success)
                path=root/'backend.json'
                self.assertEqual(complete('backend',path,{'gate':{'outcome':'success'}},IDENTITY,mode='mvp'),'CI_JOB_COMPLETE')
                normalized=json.loads(path.read_text())
                self.assertEqual(normalized['image_hardening'],{'status':'not-evaluated'})
                self.assertNotIn('image_evidence',normalized)
                for field,value in [('status','blocked'),('scanned_count',0)]:
                    bad=copy.deepcopy(data);bad[-1]['security_evidence'][field]=value
                    with self.subTest(field=field),self.assertRaises(ReportError):summaries(bad)
                for change in ('failure','skipped','missing','wrong-sha'):
                    bad=copy.deepcopy(data)
                    if change=='failure':
                        bad[0]['status']='failure';bad[0]['suites'][0]['tests'][0]['outcome']='failed'
                        self.assertFalse(aggregate(needs,summaries(bad),IDENTITY).success)
                    elif change=='missing':self.assertFalse(aggregate(needs,summaries(bad[:-1]),IDENTITY).success)
                    else:
                        if change=='skipped':bad[0]['suites'][0]['tests'][0]['outcome']='skipped'
                        else:bad[0]['identity']['candidate_sha']='f'*40
                        with self.subTest(change=change),self.assertRaises(ReportError):summaries(bad)
