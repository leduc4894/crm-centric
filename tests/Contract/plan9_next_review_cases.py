"""Draft exact-candidate scope regressions; never an approval by themselves."""
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from scripts.contracts import compatibility as gate

ROOT = Path(__file__).resolve().parents[2]
BASE = 'db04aeaecd7f4b6d083335a71418788d3c4561ca'
CANDIDATE = 'bc147e4dae9dbed662bd62a7c721774d34ca1e9f'

class Plan9NextReviewTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.repo = Path(cls.tmp.name) / 'repo'
        subprocess.run(['git','clone','--shared','--no-checkout',str(ROOT),str(cls.repo)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        subprocess.run(['git','-C',str(cls.repo),'checkout','--detach',CANDIDATE],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        (cls.repo/'tests/Contract/plan9_next_review_cases.py').write_bytes((ROOT/'tests/Contract/plan9_next_review_cases.py').read_bytes())
        for name in ('tests/CI/test_mvp_active_policy.py','tests/CI/test_mvp_history_review.py',
                     'scripts/ci/jobs.py','tests/CI/test_job_reports.py','tests/CI/test_job_processes.py',
                     'tools/ci/coverage.json','apps/crm-web/src/contacts/ContactAttributes.test.tsx',
                     'apps/crm-web/src/contacts/recordTestSupport.tsx'):
            (cls.repo/name).write_bytes((ROOT/name).read_bytes())
    @classmethod
    def tearDownClass(cls): cls.tmp.cleanup()
    def approved(self, **kwargs):
        args = dict(base=BASE,repo=self.repo,enabled=True,target=BASE,now=1795737600)
        args.update(kwargs)
        return gate.reviewed_plan9_next_candidate(**args)
    def test_exact_scope_requires_base_target_mode_and_expiry(self):
        self.assertTrue(self.approved())
        for args in ({'base':'0'*40},{'target':'0'*40},{'enabled':False},{'now':1802304000}):
            with self.subTest(args=args): self.assertFalse(self.approved(**args))
    def test_product_inventory_and_untracked_drift_are_rejected(self):
        for name in ('scripts/ci/jobs.py','tools/ci/coverage.json','scripts/benchmarks/contact_list.py','tests/Integration/BenchmarkSeedTests.cs'):
            path=self.repo/name; old=path.read_bytes()
            try:
                path.write_bytes(old+b'\n'); self.assertFalse(self.approved())
            finally: path.write_bytes(old)
        path=self.repo/'docs/contracts/unreviewed.schema.json'
        try:
            path.write_text('{}'); self.assertFalse(self.approved())
        finally: path.unlink()
    def test_scope_helper_cannot_be_modified_or_symlinked(self):
        path=self.repo/'tests/Contract/plan9_next_review_cases.py';old=path.read_bytes()
        try:
            path.write_bytes(old+b'\n');self.assertFalse(self.approved())
            path.unlink();path.symlink_to('plan9_review_cases.py');self.assertFalse(self.approved())
        finally:
            path.unlink(missing_ok=True);path.write_bytes(old)
    def test_historical_ci_fixtures_require_the_exact_reviewed_delta(self):
        names=('tests/CI/test_mvp_active_policy.py','tests/CI/test_mvp_history_review.py')
        original={name:(self.repo/name).read_bytes() for name in names}
        try:
            for name in names: (self.repo/name).write_bytes((ROOT/name).read_bytes())
            self.assertTrue(self.approved())
            for name in names:
                path=self.repo/name; saved=path.read_bytes()
                try:
                    path.write_bytes(saved+b'\n');self.assertFalse(self.approved())
                finally: path.write_bytes(saved)
        finally:
            for name,value in original.items(): (self.repo/name).write_bytes(value)
    def test_ci_prerequisite_changes_require_exact_review(self):
        names=('scripts/ci/jobs.py','tests/CI/test_job_reports.py','tests/CI/test_job_processes.py')
        original={name:(self.repo/name).read_bytes() for name in names}
        try:
            for name in names:(self.repo/name).write_bytes((ROOT/name).read_bytes())
            self.assertTrue(self.approved())
            for name in names:
                path=self.repo/name;saved=path.read_bytes()
                try:path.write_bytes(saved+b'\n');self.assertFalse(self.approved())
                finally:path.write_bytes(saved)
        finally:
            for name,value in original.items():(self.repo/name).write_bytes(value)
    def test_ui_readiness_fixture_requires_exact_review(self):
        names=('apps/crm-web/src/contacts/ContactAttributes.test.tsx',
               'apps/crm-web/src/contacts/recordTestSupport.tsx')
        original={name:(self.repo/name).read_bytes() for name in names}
        try:
            for name in names:(self.repo/name).write_bytes((ROOT/name).read_bytes())
            self.assertTrue(self.approved())
            for name in names:
                path=self.repo/name;saved=path.read_bytes()
                try:path.write_bytes(saved+b'\n');self.assertFalse(self.approved())
                finally:path.write_bytes(saved)
        finally:
            for name,value in original.items():(self.repo/name).write_bytes(value)
    def test_ci_revision_must_be_bound_to_exact_checkout(self):
        path=self.repo/'revision.json';head=gate.git(self.repo,'rev-parse','HEAD').decode().strip()
        revision={'candidate_sha':head,'base_sha':BASE,'head_sha':head,'merge_sha':None,'merge_base_sha':BASE}
        try:
            path.write_text(json.dumps(revision));self.assertTrue(self.approved())
            revision['candidate_sha']='0'*40;path.write_text(json.dumps(revision));self.assertFalse(self.approved())
        finally: path.unlink(missing_ok=True)
