"""Explicit real scanner evidence. Missing prerequisites FAIL, never skip.

Run separately from wrapper-only suite with CI_SCANNER_ROOT pointing to the
provisioned dedicated scanner tools. No real credential or provider is used.
"""
from dataclasses import replace
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

from scripts.ci.scan import (ScannerConfig, ScanInput, run_scanner, candidate_tree,
                             _history, ScanError)
from scripts.ci.summary import RunIdentity

REPO=Path(__file__).resolve().parents[2]
TOOLS=Path(os.environ.get('CI_SCANNER_ROOT',REPO/'.local/task5')).resolve()
PINS=json.loads((REPO/'tools/ci/toolchain.json').read_text())['scanners']

def config(kind):
    executable=TOOLS/('semgrep/bin/semgrep' if kind=='semgrep' else 'bin/'+kind)
    # Semgrep wheel and transitive hashes are checked at provisioning, while the
    # venv launcher embeds its absolute install path. This runtime hash is not an
    # independent trust attestation and must not be described as one.
    sha=hashlib.sha256(executable.read_bytes()).hexdigest() if kind=='semgrep' else PINS[kind]['executableSha256']
    return ScannerConfig(kind,executable,sha,PINS[kind]['version'],
        REPO/('tools/ci/semgrep.yml' if kind=='semgrep' else 'tools/ci/gitleaks.toml'),
        TOOLS/'trivy-cache')

def git(root,*args):
    return subprocess.check_output(['/usr/bin/git','-C',str(root),*args],stderr=subprocess.DEVNULL,text=True).strip()

def initialize(root):
    git(root,'init','-q');git(root,'config','user.name','Security fixture');git(root,'config','user.email','fixture@example.invalid')
    (root/'safe.txt').write_text('synthetic fixture')
    git(root,'add','.');git(root,'commit','-qm','base')
    return git(root,'rev-parse','HEAD')

class RealScannerTests(unittest.TestCase):
    def test_csharp_and_typescript_static_security_findings(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            (root/'tests').mkdir()
            (root/'tests/unsafe.cs').write_text('using System.Data.SqlClient; class Unsafe { void Run(string input) { var cmd = new SqlCommand("SELECT * FROM t WHERE x=" + input); } }\n')
            (root/'unsafe.ts').write_text('export function unsafe(input: string) { return eval(input); } // nosemgrep\n')
            run=RunIdentity('42','1','a'*40,'b'*40,None)
            result=run_scanner(config('semgrep'),ScanInput(root,frozenset({'tests/unsafe.cs','unsafe.ts'})),run)
            self.assertEqual('blocked',result.status)
            self.assertEqual(2,result.scanned_count)
            self.assertEqual(2,len(result.findings))

    def test_real_dependency_and_restricted_license(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            (root/'package-lock.json').write_text(json.dumps({'name':'synthetic-positive','lockfileVersion':3,'requires':True,'packages':{'':{'name':'synthetic-positive','version':'1.0.0','dependencies':{'lodash':'4.17.19'}},'node_modules/lodash':{'version':'4.17.19','dev':True,'resolved':'https://registry.npmjs.org/lodash/-/lodash-4.17.19.tgz','license':'GPL-3.0-only'}}}))
            run=RunIdentity('42','1','a'*40,'b'*40,None)
            result=run_scanner(config('trivy'),ScanInput(root,frozenset({'package-lock.json'})),run)
            self.assertEqual('blocked',result.status)
            self.assertTrue(any(f['kind']=='vulnerability' and f['verdict']=='blocked' for f in result.findings))
            self.assertTrue(any(f['kind']=='license' and f['verdict']=='review-required' for f in result.findings))

    def test_secret_tree_and_added_then_deleted_commit_range(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);base=initialize(root)
            # Synthetic concatenation keeps a reusable credential-shaped literal
            # out of this repository; it never names a live account.
            synthetic='github_token = "'+'ghp_'+'0123456789abcdefABCDEF0123456789abcdef'+'"\n'
            (root/'secret.txt').write_text(synthetic)
            git(root,'add','.');git(root,'commit','-qm','add synthetic fixture')
            head=git(root,'rev-parse','HEAD');run=RunIdentity('42','1',head,base,None)
            found=run_scanner(config('gitleaks'),ScanInput(root),run)
            self.assertEqual('blocked',found.status)
            (root/'secret.txt').unlink();git(root,'add','-u');git(root,'commit','-qm','delete synthetic fixture')
            head=git(root,'rev-parse','HEAD');run=replace(run,candidate_sha=head)
            deleted=run_scanner(config('gitleaks'),ScanInput(root),run)
            self.assertEqual('blocked',deleted.status)
            self.assertTrue(deleted.findings)
            self.assertNotIn('ghp_',json.dumps(deleted.to_dict()))

    def test_missing_history_object_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);base=initialize(root)
            run=RunIdentity('42','1',base,'f'*40,None)
            self.assertEqual('incomplete',run_scanner(config('gitleaks'),ScanInput(root),run).status)

    def test_merge_only_secret_then_deleted_is_in_complete_range(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);base=initialize(root);branch=git(root,'symbolic-ref','--short','HEAD')
            git(root,'checkout','-qb','fixture-side')
            (root/'side.txt').write_text('synthetic branch')
            git(root,'add','.');git(root,'commit','-qm','side')
            git(root,'checkout','-q',branch)
            git(root,'merge','--no-commit','--no-ff','fixture-side')
            (root/'merge-secret.txt').write_text('github_token = "'+'ghp_'+'0123456789abcdefABCDEF0123456789abcdef'+'"\n')
            git(root,'add','.');git(root,'commit','-qm','merge with synthetic fixture')
            (root/'merge-secret.txt').unlink();git(root,'add','-u');git(root,'commit','-qm','remove merge fixture')
            run=RunIdentity('42','1',git(root,'rev-parse','HEAD'),base,None)
            self.assertEqual('blocked',run_scanner(config('gitleaks'),ScanInput(root),run).status)

    def test_missing_historical_blob_cannot_be_treated_as_clean(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);base=initialize(root)
            (root/'removed.txt').write_text('synthetic historical content')
            git(root,'add','.');git(root,'commit','-qm','historical blob')
            blob=git(root,'rev-parse','HEAD:removed.txt')
            (root/'removed.txt').unlink();git(root,'add','-u');git(root,'commit','-qm','delete historical blob')
            head=git(root,'rev-parse','HEAD')
            (root/'.git/objects'/blob[:2]/blob[2:]).unlink()
            run=RunIdentity('42','1',head,base,None)
            self.assertEqual('incomplete',run_scanner(config('gitleaks'),ScanInput(root),run).status)

if __name__=='__main__': unittest.main()
