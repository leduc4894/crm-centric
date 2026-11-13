"""Narrow historical metadata review; native payloads never leave memory."""
import json
from pathlib import Path
import unittest
from scripts.ci import finding_adjudication as kernel
ROOT=Path(__file__).resolve().parents[2]

class HistoryReviewTests(unittest.TestCase):
    def test_inventory_is_exact_authorized_mapping(self):
        policy=json.loads((ROOT/'tools/ci/mvp-history-reviews.json').read_bytes())
        evidence=json.loads((ROOT/'docs/engineering/evidence/2026-11-13-mvp-rehearsal-9706334.json').read_bytes())
        expected=evidence['history_review']['records']
        self.assertEqual(64,len(policy['decisions']))
        self.assertEqual(4,len(policy['decisions'][60:]))
        for row in policy['decisions'][60:]:
            self.assertEqual('00e0ce4ebea101a0a17c4755a21f5406168ada50',row['commit'])
            self.assertEqual('28cdadfe09febbc7ca6719b8e13fbadcb0794dda',row['blob'])
            self.assertEqual('docs/engineering/evidence/2026-11-13-balanced-mvp-c5cfdd1.json',row['path'])
        self.assertEqual([259,265,273,279],[r['start'] for r in policy['decisions'][60:]])
        self.assertEqual({r['history_finding_id'] for r in expected},{r['history_finding_id'] for r in policy['decisions'][:60]})
        for row in policy['decisions']:
            self.assertEqual('maintainer',row['owner'])
            self.assertEqual('2027-02-11T00:00:00+00:00',row['expires'])
        self.assertTrue(callable(kernel.adjudicate_mvp_history))

    def test_real_pinned_history_exact64_and_tampering_fail_closed(self):
        import copy,hashlib,subprocess
        from datetime import datetime,timezone
        exe=ROOT/'.local/task5/bin/gitleaks';config=ROOT/'tools/ci/gitleaks.toml'
        policy=(ROOT/'tools/ci/mvp-history-reviews.json').read_bytes()
        tree_policy=(ROOT/'tools/ci/mvp-secret-reviews.json').read_bytes()
        candidate=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
        context=kernel.Context(candidate,'8.30.1',hashlib.sha256(config.read_bytes()).hexdigest(),hashlib.sha256(exe.read_bytes()).hexdigest())
        proc=subprocess.run([str(exe),'git','.', '--log-opts=--full-history --diff-merges=separate c4a5ef40a34daded023d5e5121c0462e94cc1487..'+candidate,
            '--config',str(config),'--no-banner','--redact=100','--ignore-gitleaks-allow','--gitleaks-ignore-path=/dev/null',
            '--exit-code=0','--report-format=json','--report-path=/dev/stdout'],cwd=ROOT,capture_output=True,check=True)
        rows=json.loads(proc.stdout)
        self.assertEqual(64,len(rows))
        def blob(commit,path):
            return (subprocess.check_output(['git','rev-parse',commit+':'+path],cwd=ROOT,text=True).strip(),
                    subprocess.check_output(['git','show',commit+':'+path],cwd=ROOT))
        def run(records=rows,p=policy,c=context,b=blob):
            return kernel.adjudicate_mvp_history(records,{},c,datetime.now(timezone.utc),p,tree_policy,b)
        self.assertEqual(64,sum(r['decision']=='verified-metadata' for r in run()))
        from scripts.ci.scan import ScannerConfig,ScanInput,run_scanner
        from scripts.ci.summary import RunIdentity
        actual=run_scanner(ScannerConfig('gitleaks',exe,context.binary,context.scanner,config),ScanInput(ROOT),
            RunIdentity('regression','1',candidate,'c4a5ef40a34daded023d5e5121c0462e94cc1487',None),mode='mvp')
        self.assertEqual('pass',actual.status)
        self.assertEqual(128,len(actual.reviewed_metadata))

        for key,value in [('Commit','0'*40),('StartLine',999),('RuleID','other'),('Match','private'),('File','other.json')]:
            changed=copy.deepcopy(rows);changed[0][key]=value
            self.assertEqual(63,sum(r['decision']=='verified-metadata' for r in run(changed)))
        self.assertEqual(63,sum(r['decision']=='verified-metadata' for r in run(rows+[rows[0]])))
        self.assertTrue(all(r['decision']=='blocked' for r in run(b=lambda *_:('0'*40,b'{}'))))
        for mutation in ('expired','field','id','config','count'):
            changed=json.loads(policy)
            if mutation=='config':changed['context']['config']='0'*64
            else:
                key={'expired':'expires','field':'field','id':'history_finding_id','count':'count'}[mutation]
                changed['decisions'][0][key]={'expired':'2020-01-01T00:00:00+00:00','field':['wrong'],'id':'0'*64,'count':2}[mutation]
            self.assertLess(sum(r['decision']=='verified-metadata' for r in run(p=json.dumps(changed))),64)

    def test_four_evidence_history_records_require_exact_review_and_source(self):
        import hashlib,subprocess
        from datetime import datetime,timezone
        exe=ROOT/'.local/task5/bin/gitleaks';config=ROOT/'tools/ci/gitleaks.toml'
        context=kernel.Context(subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
            '8.30.1',hashlib.sha256(config.read_bytes()).hexdigest(),hashlib.sha256(exe.read_bytes()).hexdigest())
        rows=json.loads(subprocess.check_output([str(exe),'git','.',
            '--log-opts=00e0ce4^..00e0ce4','--config',str(config),'--no-banner','--redact=100',
            '--ignore-gitleaks-allow','--gitleaks-ignore-path=/dev/null','--exit-code=0',
            '--report-format=json','--report-path=/dev/stdout'],stderr=subprocess.DEVNULL))
        self.assertEqual(4,len(rows))
        approved=json.loads((ROOT/'tools/ci/mvp-history-reviews.json').read_bytes())['decisions'][60:]
        self.assertEqual({kernel.finding_id(r) for r in rows},{r['history_finding_id'] for r in approved})
        def blob(commit,path):
            return (subprocess.check_output(['git','rev-parse',commit+':'+path],text=True).strip(),
                    subprocess.check_output(['git','show',commit+':'+path]))
        policy=(ROOT/'tools/ci/mvp-history-reviews.json').read_bytes()
        tree=(ROOT/'tools/ci/mvp-secret-reviews.json').read_bytes()
        def run(records=rows,b=blob,p=policy):
            return kernel.adjudicate_mvp_history(records,{},context,datetime.now(timezone.utc),p,tree,b)
        self.assertEqual(4,sum(r['decision']=='verified-metadata' for r in run()))
        self.assertTrue(all(r['verification_basis']=='source-recomputed' for r in run()))
        for index in range(4):
            import copy
            changed=copy.deepcopy(rows);changed[index]['StartLine']=999
            self.assertEqual(3,sum(r['decision']=='verified-metadata' for r in run(changed)))
        def wrong_contract(commit,path):
            oid,content=blob(commit,path)
            return (oid,b'changed') if path.startswith('docs/contracts/') else (oid,content)
        self.assertTrue(all(r['decision']=='blocked' for r in run(b=wrong_contract)))

    def test_real_merge_introduced_deleted_secret_remains_blocked_and_tool_error_closed(self):
        import hashlib,subprocess,tempfile
        from scripts.ci.scan import ScannerConfig,ScanInput,run_scanner
        from scripts.ci.summary import RunIdentity
        exe=ROOT/'.local/task5/bin/gitleaks';config=ROOT/'tools/ci/gitleaks.toml'
        scanner=ScannerConfig('gitleaks',exe,hashlib.sha256(exe.read_bytes()).hexdigest(),'8.30.1',config)
        with tempfile.TemporaryDirectory() as tmp:
            repo=Path(tmp)
            def git(*args):return subprocess.check_output(['git',*args],cwd=repo,stderr=subprocess.DEVNULL,text=True).strip()
            git('init','-b','main');git('config','user.name','Synthetic');git('config','user.email','synthetic@example.invalid')
            (repo/'safe').write_text('baseline');git('add','.');git('commit','-m','base');base=git('rev-parse','HEAD')
            git('checkout','-b','branch')
            (repo/'synthetic.txt').write_text('api_key = "synthetic-'+'A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6'+'"')
            git('add','.');git('commit','-m','synthetic');(repo/'synthetic.txt').unlink();git('add','-u');git('commit','-m','remove')
            git('checkout','main');(repo/'safe').write_text('main change');git('add','.');git('commit','-m','main')
            git('merge','--no-ff','branch','-m','merge');head=git('rev-parse','HEAD')
            run=RunIdentity('test','1',head,base,None)
            result=run_scanner(scanner,ScanInput(repo),run,mode='mvp')
            self.assertEqual('blocked',result.status);self.assertGreaterEqual(len(result.findings),1)
            self.assertEqual((),result.reviewed_metadata)
            self.assertNotIn('synthetic-',json.dumps(result.to_dict()))
            from dataclasses import replace
            broken=run_scanner(replace(scanner,sha256='0'*64),ScanInput(repo),run,mode='mvp')
            self.assertNotEqual('pass',broken.status)
