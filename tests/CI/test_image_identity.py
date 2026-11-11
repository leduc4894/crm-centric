import unittest
from dataclasses import replace
from scripts.ci import image_identity as images
from scripts.ci.sbom import normalize_sbom
from scripts.ci.scan import ScanError
import hashlib,json
from datetime import datetime,timezone

def evidence(identity):
    sbom={'bomFormat':'CycloneDX','specVersion':'1.6','components':[{'type':'library','name':'safe','version':'1'}]}
    sbom=normalize_sbom(sbom,identity,'0.76.2')
    return {'identity':vars(identity),'status':'pass','packages':1,'sbom':sbom,'sbomSha256':hashlib.sha256(json.dumps(sbom,sort_keys=True,separators=(',',':')).encode()).hexdigest(),'databaseUpdatedAt':datetime.now(timezone.utc).isoformat(),'javaDatabaseUpdatedAt':datetime.now(timezone.utc).isoformat()}

class ImageTests(unittest.TestCase):
    def identity(self):
        return images.ImageIdentity('compose','42','sha256:'+'a'*64,'linux/amd64',None)

    def test_config_id_does_not_become_manifest_digest(self):
        item=images.from_inspect('compose','42',{'Id':self.identity().imageId,'Os':'linux','Architecture':'amd64','RepoDigests':[]})
        self.assertEqual(self.identity(),item)
        self.assertIsNone(item.manifestDigest)

    def test_missing_invalid_identity_rejected(self):
        for value in ('latest','sha256:abc',''):
            with self.assertRaises(ScanError): images.validate(replace(self.identity(),imageId=value))

    def test_job_run_and_digest_binding(self):
        expected=[self.identity()]
        images.require_evidence(expected,[evidence(expected[0])])
        for change in ({'job':'web-browser'},{'runId':'43'},{'imageId':'sha256:'+'b'*64}):
            with self.assertRaises(ScanError): images.require_evidence(expected,[{'identity':vars(replace(expected[0],**change)),'status':'pass','packages':1,'sbomSha256':'c'*64}])
        for reports in ([],[{'identity':vars(expected[0]),'status':'blocked','packages':1,'sbomSha256':'c'*64}]):
            with self.assertRaises(ScanError): images.require_evidence(expected,reports)

    def test_sbom_nonempty_and_allowlisted(self):
        raw={'bomFormat':'CycloneDX','specVersion':'1.6','components':[{'type':'library','name':'safe','version':'1','description':'SENTINEL','properties':[{'name':'secret','value':'SENTINEL'}]}]}
        result=normalize_sbom(raw,self.identity(),'0.76.2')
        self.assertNotIn('SENTINEL',str(result))
        self.assertEqual(1,len(result['components']))
        with_group=dict(raw,components=[{'type':'application','name':'group'},*raw['components']])
        self.assertEqual(1,len(normalize_sbom(with_group,self.identity(),'0.76.2')['components']))
        for bad in ({},dict(raw,components=[]),dict(raw,components=[{'name':'bad\nSENTINEL','version':'1'}])):
            with self.assertRaises(ScanError): normalize_sbom(bad,self.identity(),'0.76.2')

    def test_unavailable_scanner_fails_closed(self):
        from scripts.ci.scan import ScannerConfig,scan_image
        from pathlib import Path
        result=scan_image(ScannerConfig('trivy',Path('/nonexistent'), 'a'*64,'0.76.2',cache=Path('/nonexistent')),self.identity())
        self.assertEqual('incomplete',result['status'])

if __name__=='__main__': unittest.main()

class BindingTests(unittest.TestCase):
    def test_required_roles_missing_closes_job(self):
        with self.assertRaises(ScanError): images.verify_job_evidence([], 'compose','42')

    def test_running_container_identity_not_mutable_tag(self):
        from unittest.mock import patch
        cid='d'*64; image='sha256:'+'a'*64
        container=[{'Id':cid,'Image':image,'State':{'StartedAt':'2026-11-11T00:00:00Z'}}]
        config=[{'Id':image,'Os':'linux','Architecture':'amd64'}]
        with patch('scripts.ci.image_identity._run_json',side_effect=[container,config]):
            self.assertEqual(image,images.observe_container('compose','42',cid).imageId)
        with patch('scripts.ci.image_identity._run_json',return_value=[dict(container[0],State={'StartedAt':'0001-01-01T00:00:00Z'})]):
            with self.assertRaises(ScanError): images.observe_container('compose','42',cid)

class JobEvidenceTests(unittest.TestCase):
    def test_complete_roles_still_reject_wrong_run_and_failed_scan(self):
        identity=images.ImageIdentity('compose','42','sha256:'+'a'*64,'linux/amd64',None)
        scan=evidence(identity)
        records=[{'role':role,'testedIdentity':vars(identity),'scan':dict(scan)} for role in ('api','migrator','postgres','keycloak')]
        images.verify_job_evidence(records,'compose','42')
        with self.assertRaises(ScanError): images.verify_job_evidence(records,'compose','43')
        records[0]['scan']['status']='blocked'
        with self.assertRaises(ScanError): images.verify_job_evidence(records,'compose','42')

    def test_summary_parser_rejects_missing_and_mismatched_scan_evidence(self):
        import json,tempfile
        from pathlib import Path
        from unittest.mock import patch
        from scripts.ci.summary import parse_report,ReportError,RunIdentity
        identity=images.ImageIdentity('compose','42','sha256:'+'a'*64,'linux/amd64',None)
        scan=evidence(identity)
        records=[{'role':role,'testedIdentity':vars(identity),'scan':dict(scan)} for role in ('api','migrator','postgres','keycloak')]
        run=RunIdentity('42','1','a'*40,'b'*40,None)
        report={'identity':vars(run),'job':'compose','command_id':'compose','status':'success','elapsed_seconds':1,'tools':{'python':'test'},'artifacts':[],
                'suites':[{'name':'compose','tests':[{'id':'required','outcome':'passed'}]}]}
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); policy=root/'policy.json'; data=root/'report.json'
            policy.write_text(json.dumps({'jobs':{'compose':['compose']},'suites':{'compose':{'minimum':1,'required':['required']}}}))
            with patch('scripts.ci.summary.POLICY_PATH',policy):
                data.write_text(json.dumps(report))
                with self.assertRaises(ReportError): parse_report('normalized',data,run)
                report['image_evidence']=records;data.write_text(json.dumps(report))
                self.assertEqual(1,parse_report('normalized',data,run).passed)
                records[0]['scan']['identity']=vars(replace(identity,runId='43'));data.write_text(json.dumps(report))
                with self.assertRaises(ReportError): parse_report('normalized',data,run)

class EvidenceIntegrityTests(unittest.TestCase):
    def test_empty_tampered_sbom_and_stale_database_close(self):
        identity=ImageTests().identity()
        for mutation in ('empty','tamper','stale'):
            report=evidence(identity)
            if mutation=='empty': report['sbom']['components']=[]
            if mutation=='tamper': report['sbom']['components'][0]['version']='2'
            if mutation=='stale': report['databaseUpdatedAt']='2000-01-01T00:00:00Z'
            with self.assertRaises(ScanError): images.require_evidence([identity],[report])

class MalformedEvidenceTests(unittest.TestCase):
    def test_malformed_nested_data_always_closes_with_reason(self):
        identity=ImageTests().identity()
        broken=evidence(identity); broken['sbom']['metadata']['tools']['components']=[]
        for report in (broken,[],{'identity':{}}):
            with self.assertRaises(ScanError): images.require_evidence([identity],[report])
        for records in ([[]],[None]):
            with self.assertRaises(ScanError): images.verify_job_evidence(records,'compose','42')
        with self.assertRaises(ScanError): images.validate(replace(identity,runId=None))

class SentinelTests(unittest.TestCase):
    def test_sentinel_in_allowlisted_package_name_closes(self):
        from scripts.ci.sbom import require_no_secrets
        with self.assertRaises(ScanError): require_no_secrets({'components':[{'name':'synthetic-sentinel'}]},['synthetic-sentinel'])
        require_no_secrets({'components':[{'name':'safe'}]},['synthetic-sentinel'])

class LifecycleHookTests(unittest.TestCase):
    def test_scan_failure_propagates_safe_reason_before_stack_success(self):
        import sys,os
        from pathlib import Path
        from types import SimpleNamespace
        from unittest.mock import patch,Mock
        sys.path.insert(0,str(Path('scripts').resolve()))
        from local import compose
        docker=SimpleNamespace(config=SimpleNamespace(root=Path.cwd(),state_dir=Path('/tmp/unused')),containers=lambda:[])
        with patch.dict(os.environ,{'CI_IMAGE_EVIDENCE':'/tmp/test-evidence'}),patch.object(compose,'_owned',return_value={}),patch.object(compose.subprocess,'Popen',return_value=SimpleNamespace(returncode=1,communicate=lambda *a,**kw: ('',''))):
            with self.assertRaises(compose.LocalFailure) as failure: compose.capture_stack_images(docker)
        self.assertEqual('CI_IMAGE_EVIDENCE_INCOMPLETE',failure.exception.token)

class ProvisionAndSourceTests(unittest.TestCase):
    def test_provisioned_archive_must_match_pin_and_extract_only_binary(self):
        from scripts.ci.provision_scanners import unpack_trivy
        import io,tarfile,tempfile
        from pathlib import Path
        buffer=io.BytesIO()
        with tarfile.open(fileobj=buffer,mode='w:gz') as archive:
            member=tarfile.TarInfo('trivy');member.size=4;archive.addfile(member,io.BytesIO(b'tool'))
        data=buffer.getvalue()
        with tempfile.TemporaryDirectory() as directory:
            target=Path(directory)/'trivy'
            unpack_trivy(data,hashlib.sha256(data).hexdigest(),hashlib.sha256(b'tool').hexdigest(),target)
            self.assertEqual(b'tool',target.read_bytes())
            with self.assertRaises(ScanError): unpack_trivy(data,'0'*64,hashlib.sha256(b'tool').hexdigest(),target)

    def test_source_sbom_bindings_require_exact_inventory(self):
        from scripts.ci.sbom import source_sbom
        from scripts.ci.summary import RunIdentity
        run=RunIdentity('42','1','a'*40,'b'*40,None)
        sbom=source_sbom({'package-lock.json':{('safe','1')}},run,'0.76.2')
        self.assertEqual(1,len(sbom['components']))
        with self.assertRaises(ScanError): source_sbom({},run,'0.76.2')

    def test_backend_runtime_evidence_required(self):
        identity=replace(ImageTests().identity(),job='backend')
        records=[{'role':'runtime','testedIdentity':vars(identity),'scan':evidence(identity)}]
        images.verify_job_evidence(records,'backend','42')
        with self.assertRaises(ScanError): images.verify_job_evidence([],'backend','42')

class ScanSerializationTests(unittest.TestCase):
    def test_evidence_lock_serializes_processes(self):
        import subprocess,sys,tempfile
        from pathlib import Path
        with tempfile.TemporaryDirectory() as directory:
            marker=Path(directory)/'acquired'
            with images.scan_lock(Path(directory)):
                code="from pathlib import Path\nfrom scripts.ci.image_identity import scan_lock\nprint('waiting',flush=True)\nwith scan_lock(Path("+repr(directory)+")):\n Path("+repr(str(marker))+").write_text('acquired')\n"
                process=subprocess.Popen([sys.executable,'-c',code],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
                self.assertEqual('waiting',process.stdout.readline().strip())
                self.assertFalse(marker.exists())
            process.communicate(timeout=5)
            self.assertEqual(0,process.returncode)
            self.assertTrue(marker.exists())

class DirectComposeTests(unittest.TestCase):
    def test_direct_runtime_evidence_adds_to_required_compose_roles(self):
        identity=ImageTests().identity()
        records=[{'role':role,'testedIdentity':vars(identity),'scan':evidence(identity)} for role in ('api','migrator','postgres','keycloak','runtime')]
        images.verify_job_evidence(records,'compose','42')
        with self.assertRaises(ScanError): images.verify_job_evidence(records[1:],'compose','42')
