"""Approved inventory integration; no raw scanner output is published."""
from datetime import datetime, timezone
import json
from pathlib import Path
import unittest
from scripts.ci import finding_adjudication as kernel
ROOT=Path(__file__).resolve().parents[2]

class ActivePolicyTests(unittest.TestCase):
    def test_reviewed_content_binding_is_full_digest_and_not_candidate_sha(self):
        self.assertTrue(hasattr(kernel,'reviewed_content_digest'))
        docs={'a':b'one','b':b'two'}
        digest=kernel.reviewed_content_digest(docs)
        self.assertEqual(64,len(digest))
        self.assertNotEqual(digest,kernel.reviewed_content_digest({'a':b'changed','b':b'two'}))
        self.assertEqual(digest,kernel.reviewed_content_digest(dict(reversed(list(docs.items())))))

    def test_active_inventory_is_exact_approved68_with_maintainer_90day_expiry(self):
        path=ROOT/'tools/ci/mvp-secret-reviews.json'
        self.assertTrue(path.is_file())
        policy=json.loads(path.read_bytes())
        pending=json.loads((ROOT/'docs/engineering/evidence/2026-11-13-mvp-exact-finding-policy.proposed.json').read_bytes())
        self.assertEqual(2,policy['schema_version'])
        self.assertEqual({e['finding_id'] for e in pending['decisions']},{e['finding_id'] for e in policy['decisions'][:64]})
        self.assertEqual(68,len(policy['decisions']))
        evidence=policy['decisions'][64:]
        self.assertEqual([259,265,273,279],[e['start'] for e in evidence])
        self.assertTrue(all(e['source_kind']=='evidence-contract' for e in evidence))
        for entry in policy['decisions']:
            self.assertEqual('approved',entry['status'])
            self.assertEqual('maintainer',entry['owner'])
            self.assertEqual('2027-02-11T00:00:00+00:00',entry['expires'])

    def test_real_scanner_adjudicates68_but_blocks_new_history_changed_expired_and_malformed(self):
        import hashlib, io, subprocess, tarfile, tempfile
        from scripts.ci.scan import ScannerConfig,ScanInput,run_scanner
        from scripts.ci.summary import RunIdentity
        exe=ROOT/'.local/task5/bin/gitleaks'
        config=ROOT/'tools/ci/gitleaks.toml'
        scanner=ScannerConfig('gitleaks',exe,hashlib.sha256(exe.read_bytes()).hexdigest(),'8.30.1',config)
        with tempfile.TemporaryDirectory() as directory:
            tree=Path(directory)
            archive=subprocess.run(['git','archive','HEAD'],cwd=ROOT,capture_output=True,check=True).stdout
            with tarfile.open(fileobj=io.BytesIO(archive)) as tar:tar.extractall(tree,filter='data')
            policy_path=tree/'tools/ci/mvp-secret-reviews.json'
            policy_path.write_bytes((ROOT/'tools/ci/mvp-secret-reviews.json').read_bytes())
            def git(*args):return subprocess.run(['git',*args],cwd=tree,capture_output=True,check=True).stdout.decode().strip()
            git('init','-b','main');git('config','user.name','Synthetic Test');git('config','user.email','synthetic@example.invalid')
            git('add','.');git('commit','-m','reviewed baseline');base=git('rev-parse','HEAD')
            def scan(mode='mvp'):
                sha=git('rev-parse','HEAD')
                run=RunIdentity('test','1',sha,base,None)
                return run_scanner(scanner,ScanInput(tree),run,mode=mode)
            result=scan()
            self.assertEqual('pass',result.status)
            self.assertEqual(68,len(result.reviewed_metadata))
            self.assertEqual(60,sum(r['verification_basis']=='reviewed-jar-snapshot' for r in result.reviewed_metadata))
            self.assertEqual(8,sum(r['verification_basis']=='source-recomputed' for r in result.reviewed_metadata))
            self.assertEqual((),result.findings)
            self.assertEqual('blocked',scan('hardening').status)
            original=policy_path.read_bytes()
            for mutation in ('expired','malformed','changed','downgraded','origin'):
                if mutation=='expired':
                    policy=json.loads(original)
                    for entry in policy['decisions']:entry['expires']='2020-01-01T00:00:00+00:00'
                    policy_path.write_text(json.dumps(policy))
                elif mutation=='malformed':policy_path.write_text('{')
                elif mutation=='origin':
                    policy=json.loads(original);policy['context']['candidate']=''
                    policy_path.write_text(json.dumps(policy))
                elif mutation=='downgraded':
                    policy=json.loads(original);policy['schema_version']=1
                    policy.pop('reviewed_content_sha256')
                    policy_path.write_text(json.dumps(policy))
                else:
                    path=tree/'docs/ci/candidates/keycloak26/diagnostic-runtime-jars.json'
                    path.write_bytes(path.read_bytes()+b'\n')
                git('add','.');git('commit','-m',mutation)
                result=scan();self.assertEqual('blocked',result.status)
                self.assertEqual(68,len(result.findings));self.assertEqual((),result.reviewed_metadata)
                git('reset','--hard',base)
            secret=tree/'synthetic.txt'
            secret.write_text('api_key = "synthetic-'+'A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6'+'"')
            git('add','.');git('commit','-m','synthetic new')
            result=scan();self.assertEqual('blocked',result.status)
            self.assertEqual(68,len(result.reviewed_metadata));self.assertGreaterEqual(len(result.findings),2)
            safe=json.dumps(result.to_dict())
            self.assertNotIn('synthetic-',safe);self.assertNotIn('Match',safe);self.assertNotIn('Secret',safe)
            secret.unlink();git('add','-u');git('commit','-m','delete synthetic')
            result=scan();self.assertEqual('blocked',result.status)
            self.assertEqual(68,len(result.reviewed_metadata));self.assertGreaterEqual(len(result.findings),1)

    def test_safe_review_evidence_rejects_unknown_ids_and_payload_extensions(self):
        import copy
        from scripts.ci.scan import ScanError,validate_reviewed_metadata,_safe_mvp_review
        policy=json.loads((ROOT/'tools/ci/mvp-secret-reviews.json').read_bytes())
        entry=policy['decisions'][0]
        row={'finding_id':entry['finding_id'],'verification_basis':entry['review_basis'],
             'owner':'maintainer','expires':entry['expires'],'reason':'CI_METADATA_SNAPSHOT_REVIEWED',
             'correlation_id':'00000000-0000-4000-8000-000000000000'}
        self.assertEqual([row],validate_reviewed_metadata([row]))
        for key,value in [('finding_id','f'*64),('owner','arbitrary text'),('expires','2099-01-01'),
                          ('verification_basis','source-recomputed'),('record',{'Match':'private'})]:
            changed=copy.deepcopy(row);changed[key]=value
            with self.assertRaises(ScanError):validate_reviewed_metadata([changed])
        with self.assertRaises(ScanError):validate_reviewed_metadata([row,row])
        self.assertFalse(_safe_mvp_review([dict(row,finding_id="malformed")],"gitleaks"))
