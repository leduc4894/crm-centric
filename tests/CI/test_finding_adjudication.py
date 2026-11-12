"""Local trust-boundary prototype: synthetic trusted capability is never CI approval."""
from dataclasses import replace
from datetime import datetime, timedelta, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
try:
    from scripts.ci import finding_adjudication as subject
except ImportError:
    subject=None

ROOT=Path(__file__).resolve().parents[2]
NOW=datetime(2026,11,13,tzinfo=timezone.utc)
class TrustedHarness:
    def __init__(self, decision, source=b'reference bytes'):
        self.decision=decision; self.source=source
    def lookup(self, identifier):
        return self.decision if identifier==self.decision.finding_id else None
    def source_bytes(self, decision): return self.source

class AdjudicationTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(subject, "scoped adjudication implementation missing")
        self.assertIn('binary',subject.Context.__dataclass_fields__, 'scanner binary identity missing')
        self.source=b'reference bytes'; self.digest=hashlib.sha256(self.source).hexdigest()
        self.path='sdks/typescript/crm/provenance.json'
        self.field='crm-api.openapi.yaml'
        self.document=json.dumps({'inputHashes':{self.field:self.digest}},indent=2).encode()
        self.row={'File':self.path,'RuleID':'generic-api-key','StartLine':3,'EndLine':3,
                  'Secret':'REDACTED','Match':self.field+'": "REDACTED"','Commit':''}
        self.context=subject.Context('a'*40,'8.30.1','b'*64,'e'*64)
        self.decision=subject.Decision(subject.finding_id(self.row), self.context,self.path,
            ('inputHashes',self.field),3,3,'generic-api-key',1,'security-reviewer',NOW+timedelta(days=1),'contract')
    def run_case(self, rows=None, history=(), trusted=True, decision=None, source=b'reference bytes', document=None, context=None):
        return subject.adjudicate([self.row] if rows is None else rows,history,
            {self.path:self.document if document is None else document,'docs/contracts/'+self.field:source},context or self.context,NOW,
            TrustedHarness(decision or self.decision,source) if trusted else None)
    def test_positive_retains_every_record_and_safe_evidence(self):
        result=self.run_case()
        self.assertEqual('verified-metadata',result[0]['decision'])
        self.assertEqual(self.row,result[0]['record'])
        self.assertEqual('security-reviewer',result[0]['owner'])
    def test_absent_and_candidate_authored_capability_fail_closed(self):
        for capability in (None,{}, {'lookup':self.decision},self.decision):
            result=subject.adjudicate([self.row],[],{self.path:self.document,'docs/contracts/'+self.field:self.source},self.context,NOW,capability)
            self.assertEqual('blocked',result[0]['decision'])
    def test_approval_binding_expiry_owner_and_multiplicity(self):
        for decision in (replace(self.decision,owner=''),replace(self.decision,expires=NOW),
                         replace(self.decision,context=replace(self.context,candidate='c'*40)),
                         replace(self.decision,context=replace(self.context,scanner='other')),
                         replace(self.decision,context=replace(self.context,binary='f'*64)),
                         replace(self.decision,context=replace(self.context,config='c'*64)),
                         replace(self.decision,finding_id='d'*64), replace(self.decision,rule='other'),
                         replace(self.decision,path='moved'),replace(self.decision,start=2),
                         replace(self.decision,field=('moved',self.field)),replace(self.decision,count=2)):
            with self.subTest(case=str(decision.field)):
                self.assertEqual('blocked',self.run_case(decision=decision)[0]['decision'])
        self.assertTrue(all(x['decision']=='blocked' for x in self.run_case(rows=[self.row,self.row])))
    def test_missing_identity_components_block_even_matching_decision(self):
        for context in (replace(self.context,binary=''),replace(self.context,config=''),
                        replace(self.context,scanner=''),replace(self.context,candidate='')):
            self.assertEqual('blocked',self.run_case(context=context,decision=replace(self.decision,context=context))[0]['decision'])

    def test_source_and_json_ambiguity_and_selected_credentials_block(self):
        for document in (b'{', self.document.replace(b'"inputHashes"',b'"inputHashes":{},"inputHashes"'),
                         self.document.replace(self.digest.encode(),b'credential-value'),
                         self.document.replace(self.digest.encode(),b'f'*64),
                         self.document.replace(b'"inputHashes"',b'"moved"'),
                         self.document.replace(self.digest.encode(),self.digest.encode()+b' extra')):
            self.assertEqual('blocked',self.run_case(document=document)[0]['decision'])
        for source in (None,b'changed bytes'):
            self.assertEqual('blocked',self.run_case(source=source)[0]['decision'])
    def test_non_json_numeric_constants_are_malformed(self):
        document=self.document[:-2]+b', "outside": NaN\n}'
        self.assertEqual('blocked',self.run_case(document=document)[0]['decision'])

    def test_selected_line_must_be_the_unique_structural_member(self):
        document=json.dumps({'another':{self.field:self.digest},'inputHashes':{self.field:self.digest}},indent=2).encode()
        self.assertEqual('blocked',self.run_case(document=document)[0]['decision'])

    def test_all_unreviewed_records_and_history_origins_remain_blocking(self):
        for changed in (dict(self.row,RuleID='another-rule'),dict(self.row,File='moved'),
                        dict(self.row,Match=self.row['Match']+' extra'),dict(self.row,StartLine=4)):
            result=self.run_case(rows=[self.row,changed])
            self.assertEqual(['verified-metadata','blocked'],[r['decision'] for r in result])
        for origin in ('history-only','added-then-deleted','merge-only'):
            result=self.run_case(history=[dict(self.row,Commit=origin)])
            self.assertEqual('blocked',result[-1]['decision'])
    def test_jars_require_independent_source_even_with_approval(self):
        path='docs/ci/candidates/keycloak26/diagnostic-runtime-jars.json'; key='lib/api.jar'
        row=dict(self.row,File=path,StartLine=2,EndLine=2,Match=key+'": "REDACTED"')
        decision=replace(self.decision,finding_id=subject.finding_id(row),path=path,field=(key,),start=2,end=2,source_kind='jar')
        for source,expected in ((None,'blocked'),(b'substituted','blocked'),(self.source,'verified-metadata')):
            result=subject.adjudicate([row],[],{path:json.dumps({key:self.digest},indent=2).encode()},self.context,NOW,TrustedHarness(decision,source))
            self.assertEqual(expected,result[0]['decision'])

class RealScannerTests(unittest.TestCase):
    def test_credentials_outside_selected_field_in_every_proposed_file_block(self):
        self.assertIsNotNone(subject, "scoped adjudication implementation missing")
        proposal=(ROOT/'docs/engineering/evidence/2026-11-12-mvp-secret-review-proposal.md').read_text()
        import re
        paths=set(re.findall(r'^\| `([^`]+)`',proposal,re.M))
        self.assertEqual(5,len(paths))
        exe=ROOT/'.local/task5/bin/gitleaks'
        self.assertTrue(exe.is_file(),'pinned real Gitleaks prerequisite missing')
        with tempfile.TemporaryDirectory() as directory:
            tree=Path(directory)
            for path in paths:
                file=tree/path;file.parent.mkdir(parents=True,exist_ok=True)
                file.write_text(json.dumps({'api_key':'synthetic-'+'A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6'}))
            process=subprocess.run([str(exe),'dir','.', '--config',str(ROOT/'tools/ci/gitleaks.toml'),
                '--redact=100','--ignore-gitleaks-allow','--gitleaks-ignore-path=/dev/null',
                '--exit-code=0','--report-format=json','--report-path=/dev/stdout','--no-banner'],cwd=tree,capture_output=True)
            self.assertEqual(0,process.returncode)
            rows=json.loads(process.stdout)
            self.assertEqual(paths,{r['File'] for r in rows})
            results=subject.adjudicate(rows,[],{},subject.Context('a'*40,'8.30.1','b'*64,'e'*64),NOW)
            self.assertTrue(all(r['decision']=='blocked' for r in results))

class CandidateReplayTests(unittest.TestCase):
    def test_exact_proposal_candidate_replay_preserves64_and_history0(self):
        import collections
        import io
        import re
        import tarfile
        candidate='b1c86f931fdc0a3ab95ea2243c95b35f29637851'
        base='601dde8847cad82d038ea837c01d3da0d7449450'
        exe=ROOT/'.local/task5/bin/gitleaks'
        version=subprocess.run([str(exe),'version'],capture_output=True,check=True).stdout.decode().strip()
        self.assertEqual('8.30.1',version)
        config=ROOT/'tools/ci/gitleaks.toml'
        args=['--config',str(config),'--no-banner','--redact=100','--ignore-gitleaks-allow',
              '--gitleaks-ignore-path=/dev/null','--exit-code=0','--report-format=json','--report-path=/dev/stdout']
        proposal=(ROOT/'docs/engineering/evidence/2026-11-12-mvp-secret-review-proposal.md').read_text()
        entries=re.findall(r'^\| `([^`]+)` \| (\d+)–(\d+) \| `([^`]+)` \| `([^`]+)` \| 1 \| `([a-f0-9]{64})`',proposal,re.M)
        self.assertEqual(64,len(entries))
        context=subject.Context(candidate,version,hashlib.sha256(config.read_bytes()).hexdigest(),hashlib.sha256(exe.read_bytes()).hexdigest())
        decisions={}
        for path,start,end,key,rule,identifier in entries:
            jar=path.endswith('diagnostic-runtime-jars.json')
            decisions[identifier]=subject.Decision(identifier,context,path,(key,) if jar else ('inputHashes',key),
                int(start),int(end),rule,1,'synthetic-test-reviewer',NOW+timedelta(days=1),'jar' if jar else 'contract')
        class ReplayHarness:
            def lookup(self,identifier): return decisions.get(identifier)
            def source_bytes(self,decision): return None
        with tempfile.TemporaryDirectory() as directory:
            tree=Path(directory)
            archive=subprocess.run(['git','archive',candidate],cwd=ROOT,capture_output=True,check=True).stdout
            with tarfile.open(fileobj=io.BytesIO(archive)) as tar: tar.extractall(tree,filter='data')
            def scan():
                return json.loads(subprocess.run([str(exe),'dir','.',*args],cwd=tree,capture_output=True,check=True).stdout)
            rows=scan();again=scan()
            self.assertEqual(64,len(rows))
            self.assertEqual(collections.Counter(subject.finding_id(r) for r in rows),collections.Counter(subject.finding_id(r) for r in again))
            self.assertEqual(set(decisions),{subject.finding_id(r) for r in rows})
            history=json.loads(subprocess.run([str(exe),'git',str(ROOT),'--log-opts=--full-history --diff-merges=separate '+base+'..'+candidate,*args],capture_output=True,check=True).stdout)
            self.assertEqual([],history)
            documents={p.relative_to(tree).as_posix():p.read_bytes() for p in tree.rglob('*') if p.is_file() and (p.suffix=='.json' or p.parent==tree/'docs/contracts')}
            default=subject.adjudicate(rows,history,documents,context,NOW)
            self.assertEqual(64,sum(r['decision']=='blocked' for r in default))
            synthetic=subject.adjudicate(rows,history,documents,context,NOW,ReplayHarness())
            self.assertEqual(4,sum(r['decision']=='verified-metadata' for r in synthetic))
            self.assertEqual(60,sum(r['decision']=='blocked' for r in synthetic))
            self.assertEqual(rows,[r['record'] for r in synthetic])

class RealHistoryTests(unittest.TestCase):
    def test_added_deleted_and_side_branch_merge_findings_block(self):
        exe=ROOT/'.local/task5/bin/gitleaks'
        with tempfile.TemporaryDirectory() as directory:
            tree=Path(directory)
            def git(*args):
                return subprocess.run(['git',*args],cwd=tree,capture_output=True,check=True).stdout.decode().strip()
            git('init','-b','main');git('config','user.name','Synthetic Test');git('config','user.email','synthetic@example.invalid')
            (tree/'base').write_text('base');git('add','.');git('commit','-m','base');base=git('rev-parse','HEAD')
            credential='api_key = "synthetic-'+'A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6'+'"'
            (tree/'deleted.txt').write_text(credential);git('add','.');git('commit','-m','add synthetic')
            (tree/'deleted.txt').unlink();git('add','-u');git('commit','-m','delete synthetic')
            git('checkout','-b','side');(tree/'merged.txt').write_text(credential);git('add','.');git('commit','-m','side synthetic')
            git('checkout','main');(tree/'main.txt').write_text('main');git('add','.');git('commit','-m','main change')
            git('merge','--no-ff','--no-commit','side')
            (tree/'merge-only.txt').write_text(credential);git('add','.');git('commit','-m','merge synthetic');candidate=git('rev-parse','HEAD')
            args=['--config',str(ROOT/'tools/ci/gitleaks.toml'),'--no-banner','--redact=100',
                  '--ignore-gitleaks-allow','--gitleaks-ignore-path=/dev/null','--exit-code=0',
                  '--report-format=json','--report-path=/dev/stdout']
            rows=json.loads(subprocess.run([str(exe),'git','.', '--log-opts=--full-history --diff-merges=separate '+base+'..'+candidate,*args],cwd=tree,capture_output=True,check=True).stdout)
            self.assertTrue({'deleted.txt','merged.txt','merge-only.txt'}.issubset({r['File'] for r in rows}))
            self.assertTrue(all(r['Commit'] for r in rows))
            results=subject.adjudicate([],rows,{},subject.Context(candidate,'8.30.1','b'*64,'e'*64),NOW)
            self.assertEqual(len(rows),len(results))
            self.assertTrue(all(r['decision']=='blocked' for r in results))

class RealMixedTests(unittest.TestCase):
    def test_active_synthetic_capability_only_adjudicates_selected_metadata(self):
        import re
        proposal=(ROOT/'docs/engineering/evidence/2026-11-12-mvp-secret-review-proposal.md').read_text()
        paths=set(re.findall(r'^\| `([^`]+)`',proposal,re.M))
        exe=ROOT/'.local/task5/bin/gitleaks'
        source=b'reference bytes';digest=hashlib.sha256(source).hexdigest()
        context=subject.Context('a'*40,'8.30.1','b'*64,'e'*64)
        with tempfile.TemporaryDirectory() as directory:
            tree=Path(directory);documents={};fields={}
            for path in paths:
                jar=path.endswith('diagnostic-runtime-jars.json');key='lib/api.jar' if jar else 'crm-api.openapi.yaml'
                fields[path]=(key,) if jar else ('inputHashes',key)
                data={key:digest} if jar else {'inputHashes':{key:digest}}
                data['api_key']='synthetic-'+'A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6'
                file=tree/path;file.parent.mkdir(parents=True,exist_ok=True)
                file.write_text(json.dumps(data,indent=2));documents[path]=file.read_bytes()
            documents['docs/contracts/crm-api.openapi.yaml']=source
            def scan():
                process=subprocess.run([str(exe),'dir','.', '--config',str(ROOT/'tools/ci/gitleaks.toml'),
                    '--redact=100','--ignore-gitleaks-allow','--gitleaks-ignore-path=/dev/null',
                    '--exit-code=0','--report-format=json','--report-path=/dev/stdout','--no-banner'],cwd=tree,capture_output=True,check=True)
                return json.loads(process.stdout)
            rows=scan();self.assertEqual(10,len(rows))
            decisions={}
            for row in rows:
                if row['StartLine']==(2 if len(fields[row['File']])==1 else 3):
                    identifier=subject.finding_id(row)
                    decisions[identifier]=subject.Decision(identifier,context,row['File'],fields[row['File']],
                        row['StartLine'],row['EndLine'],row['RuleID'],1,'synthetic-reviewer',NOW+timedelta(days=1),
                        'jar' if len(fields[row['File']])==1 else 'contract')
            class Harness:
                def lookup(self,identifier): return decisions.get(identifier)
                def source_bytes(self,decision): return source
            results=subject.adjudicate(rows,[],documents,context,NOW,Harness())
            self.assertEqual(5,sum(r['decision']=='verified-metadata' for r in results))
            self.assertEqual(5,sum(r['decision']=='blocked' for r in results))
            for substitute in ('synthetic-'+'A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6','f'*64):
                for path in paths:
                    mutated=documents[path].replace(digest.encode(),substitute.encode())
                    (tree/path).write_bytes(mutated)
                changed=scan()
                mutated_documents={p:(tree/p).read_bytes() for p in paths}
                mutated_documents['docs/contracts/crm-api.openapi.yaml']=source
                results=subject.adjudicate(changed,[],mutated_documents,context,NOW,Harness())
                self.assertTrue(all(r['decision']=='blocked' for r in results))
