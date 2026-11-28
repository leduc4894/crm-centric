"""Candidate scope must remain bounded to the reviewed product tree."""
import subprocess
import io
import json
import tarfile
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from scripts.contracts import compatibility as gate

ROOT = Path(__file__).resolve().parents[2]
BASE = 'db04aeaecd7f4b6d083335a71418788d3c4561ca'
CANDIDATE = '71959b35c08738d16a03e1f4f54e4f0b21075ce0'

class Plan9ReviewTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.repo = Path(cls.tmp.name) / 'repo'
        subprocess.run(['git','clone','--shared','--no-checkout',str(ROOT),str(cls.repo)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        subprocess.run(['git','-C',str(cls.repo),'checkout','--detach',CANDIDATE],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        for name in ('scripts/ci/jobs.py','tests/CI/test_job_processes.py','apps/crm-web/src/contacts/contactQueries.test.tsx','apps/crm-web/index.html','apps/crm-web/src/App.tsx','apps/crm-web/src/contacts/crmClient.test.ts'):
            (cls.repo/name).write_bytes((ROOT/name).read_bytes())
        helper=cls.repo/'tests/Contract/plan9_review_cases.py'
        helper.write_bytes((ROOT/'tests/Contract/plan9_review_cases.py').read_bytes())
    @classmethod
    def tearDownClass(cls): cls.tmp.cleanup()
    def approved(self, **kwargs):
        args = dict(base=BASE, repo=self.repo, enabled=True, target=BASE, now=1795737600)
        args.update(kwargs)
        return gate.reviewed_plan9_candidate(**args)
    def test_scope_requires_exact_candidate_base_mode_and_expiry(self):
        self.assertTrue(self.approved())
        for args in ({'base':'0'*40},{'target':'0'*40},{'enabled':False},{'now':1802304000}):
            with self.subTest(args=args): self.assertFalse(self.approved(**args))
    def test_changed_or_untracked_product_and_coverage_are_rejected(self):
        for name in ('docs/contracts/crm-api.openapi.yaml','src/Platform.Api/Crm/TimelineEndpoints.cs','tools/ci/coverage.json'):
            path=self.repo/name; old=path.read_bytes()
            try:
                path.write_bytes(old+b'\n'); self.assertFalse(self.approved())
            finally: path.write_bytes(old)
        path=self.repo/'docs/contracts/unreviewed.schema.json'
        try:
            path.write_text('{}'); self.assertFalse(self.approved())
        finally: path.unlink()
    def test_symlinked_contract_is_rejected(self):
        path=self.repo/'docs/contracts/crm-api.openapi.yaml'
        saved=path.with_name('held.yaml')
        path.rename(saved)
        try:
            path.symlink_to(saved.name)
            self.assertFalse(self.approved())
        finally:
            path.unlink(); saved.rename(path)

    def test_pinned_archive_rejects_modified_content(self):
        with tempfile.TemporaryDirectory() as temporary:
            out=Path(temporary)/'snapshot'
            out.mkdir()
            archive=gate.git(self.repo,'archive','--format=tar',CANDIDATE)
            with tarfile.open(fileobj=io.BytesIO(archive)) as stream:
                stream.extractall(out,filter='data')
            self.assertTrue(gate.plan3_archive_matches_tree(self.repo,out,CANDIDATE))
            path=out/'docs/contracts/crm-api.openapi.yaml'
            path.write_bytes(path.read_bytes()+b'\n')
            self.assertFalse(gate.plan3_archive_matches_tree(self.repo,out,CANDIDATE))

    def test_policy_preflight_requires_reviewed_scope(self):
        with patch('time.time', return_value=1795737600):
            review=gate.check_baseline_configuration(self.repo, BASE, True, target=BASE)
            self.assertFalse(review['independentVerification'])
            with self.assertRaises(gate.GateError) as error:
                gate.check_baseline_configuration(self.repo, BASE, True, target='0'*40)
            self.assertEqual('CONTRACT_POLICY_REVIEW_REQUIRED',str(error.exception))

    def test_untracked_ci_revision_is_accepted_only_when_bound_to_checkout(self):
        path=self.repo/'revision.json'
        head=gate.git(self.repo,'rev-parse','HEAD').decode().strip()
        revision={'candidate_sha':head,'base_sha':BASE,'head_sha':head,
                  'merge_sha':None,'merge_base_sha':BASE}
        try:
            path.write_text(json.dumps(revision))
            self.assertTrue(self.approved())
            subprocess.run(['git','-C',str(self.repo),'add','revision.json'],check=True)
            self.assertFalse(self.approved())
            subprocess.run(['git','-C',str(self.repo),'reset','-q','--','revision.json'],check=True)
            path.write_text('{"base_sha":"duplicate",'+json.dumps(revision)[1:])
            self.assertFalse(self.approved())
            revision['candidate_sha']='0'*40
            path.write_text(json.dumps(revision))
            self.assertFalse(self.approved())
            path.unlink();path.symlink_to('docs/contracts/crm-api.openapi.yaml')
            self.assertFalse(self.approved())
        finally: path.unlink(missing_ok=True)

    def test_ci_diagnostic_override_requires_exact_pinned_bytes(self):
        paths=('scripts/ci/jobs.py','tests/CI/test_job_processes.py','apps/crm-web/src/contacts/contactQueries.test.tsx','apps/crm-web/index.html','apps/crm-web/src/App.tsx','apps/crm-web/src/contacts/crmClient.test.ts')
        originals={name:(self.repo/name).read_bytes() for name in paths}
        try:
            for name in paths: (self.repo/name).write_bytes((ROOT/name).read_bytes())
            self.assertTrue(self.approved())
            for name in paths:
                path=self.repo/name; saved=path.read_bytes()
                try:
                    path.write_bytes(saved+b'\n')
                    self.assertFalse(self.approved())
                finally: path.write_bytes(saved)
        finally:
            for name,value in originals.items(): (self.repo/name).write_bytes(value)

    def test_old_scope_not_replaced(self):
        self.assertFalse(gate.reviewed_plan3_candidate(BASE,self.repo,True,BASE,now=1795737600))
