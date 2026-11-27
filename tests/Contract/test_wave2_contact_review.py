"""Exact reviewed scope must not become a general compatibility waiver."""
import copy
import hashlib
import io
import tarfile
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from types import SimpleNamespace
from unittest.mock import patch

from scripts.contracts import compatibility, sdk_surface

ROOT = Path(__file__).resolve().parents[2]
BASE = 'db04aeaecd7f4b6d083335a71418788d3c4561ca'
REVIEWED_FIXTURE = 'e1f1549c8b5b20654cba30962e5fd499841c088c'
FILES = (
    'docs/contracts/crm-api.openapi.yaml',
    'docs/contracts/platform-operations.openapi.yaml',
    'docs/contracts/contact-event-intent.schema.json',
    'docs/contracts/fixtures/contact-created.v1.json',
    'docs/contracts/fixtures/contact-updated.v1.json',
    'docs/contracts/fixtures/contact-deleted.v1.json',
    'docs/architecture/10-ADR-020-CONTACT-EMAIL-UPSERT.md',
    'docs/architecture/11-ADR-021-CONTACT-UPSERT-IDENTITY-DRAFT.md',
    'docs/operations/06-CONTACT-MIGRATION.md',
    'docs/operations/07-CONTACT-SDK-MIGRATION.md',
    'src/Platform.Api/Crm/ContactEndpoints.cs',
    'src/Platform.Infrastructure/Crm/ContactStore.cs',
    'src/Platform.Infrastructure/Crm/ContactMutationJournal.cs',
    'tests/Contract/test_contact_crud.py',
    'tests/Contract/test_contact_upsert_consumer.py',
    'tests/Contract/test_contact_precondition_identity.py',
    'tests/Integration/ContactEndpointsTests.cs',
    'tests/Integration/ContactConcurrencyTests.cs',
    'tests/ClientConsumers/contact-upsert/new/Program.cs',
    'tests/ClientConsumers/contact-upsert/old/Program.cs',
    'tests/ClientConsumers/run.sh',
    'tests/ClientConsumers/dotnet/packages.lock.json',
    'tests/ClientConsumers/dotnet-v2/packages.lock.json',
    'scripts/contracts/generate.py',
    'scripts/contracts/check.py',
    'src/Platform.Api/Identity/TenantAuthorization.cs',
    'src/Platform.Api/Identity/TenantEndpointTransaction.cs',
    'src/Platform.Infrastructure/Persistence/Migrations/20261127000000_ContactFoundation.cs',
    'src/Platform.Infrastructure/Persistence/Migrations/20261127010000_ContactEmailUpsert.cs',
    'tests/Integration/ContactSchemaTests.cs',
    'tests/Integration/ContactEmailSchemaTests.cs',
    'tests/Integration/ContactStreamingTests.cs',
    'tests/ClientConsumers/contact-upsert/new/Consumer.csproj',
    'tests/ClientConsumers/contact-upsert/new/packages.lock.json',
    'tests/ClientConsumers/contact-upsert/old/Consumer.csproj',
    'tests/ClientConsumers/contact-upsert/old/packages.lock.json',
    'tests/ClientConsumers/contact-upsert/run.sh',
    'tests/ClientConsumers/dotnet/Consumer.csproj',
    'tests/ClientConsumers/dotnet/Program.cs',
    'tests/ClientConsumers/dotnet-v2/Consumer.csproj',
    'tests/ClientConsumers/typescript/types.ts',
    'tests/ClientConsumers/typescript/wire.mjs',
    'tests/ClientConsumers/audit.py',
    'tests/ClientConsumers/normalize_packages.py',
    'tools/contracts/toolchain.json',
    'tools/contracts/facades/ResponseMetadataHandler.cs',
    'tools/contracts/locks/Crm.Client.json',
    'tools/contracts/locks/Platform.Operations.Client.json',
    'package.json',
    'package-lock.json',
    '.npmrc',
)


def reviewed_fixture_available():
    try:
        return subprocess.run(['git', '-C', str(ROOT), 'cat-file', '-e', REVIEWED_FIXTURE + '^{commit}'],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10).returncode == 0
    except (OSError, subprocess.SubprocessError):
        return False


@unittest.skipUnless(reviewed_fixture_available(), 'the immutable reviewed contact fixture is unavailable')
class ContactReviewScopeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.archive = subprocess.check_output(
            ['git', '-C', str(ROOT), 'archive', REVIEWED_FIXTURE, '--', 'docs/contracts', *FILES],
            stderr=subprocess.DEVNULL)

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.repo = Path(self.tmp.name)
        with tarfile.open(fileobj=io.BytesIO(self.archive)) as archive:
            archive.extractall(self.repo, filter='data')
        contract_inputs = [p.relative_to(self.repo).as_posix() for p in (self.repo / 'docs/contracts').rglob('*')
                           if p.is_file() and (p.name.endswith('.schema.json') or p.name.endswith('.openapi.yaml'))]
        self.hashes = {'current': {path: hashlib.sha256((self.repo / path).read_bytes()).hexdigest()
                                    for path in contract_inputs}}

    def approved(self, base=BASE, enabled=True, now=1795737600):
        fn = getattr(compatibility, 'reviewed_contact_change', lambda *args, **kwargs: False)
        return fn(base, self.repo, self.hashes, enabled, now=now)

    def test_approved_fixture_is_stable_when_current_checkout_drifts(self):
        with tempfile.TemporaryDirectory() as temporary:
            clone=Path(temporary)/'repo'
            subprocess.run(['git','clone','--shared','--no-checkout',str(ROOT),str(clone)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            subprocess.run(['git','-C',str(clone),'checkout','--detach',REVIEWED_FIXTURE],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            target=clone/'src/Platform.Api/Crm/ContactEndpoints.cs'
            target.write_bytes(target.read_bytes()+b'\n')
            (clone/'docs/contracts/new.schema.json').write_text('{}')
            with patch(__name__+'.ROOT',clone):
                fixture=ContactReviewScopeTests('runTest')
                try:
                    fixture.setUp()
                    self.assertTrue(fixture.approved(), 'approved fixture depended on current worktree')
                finally:
                    fixture.doCleanups()

    def test_only_reviewed_candidate_and_base_enabled_before_expiry(self):
        self.assertTrue(self.approved())
        self.assertFalse(self.approved(base='0' * 40))
        self.assertFalse(self.approved(enabled=False))
        self.assertFalse(self.approved(now=1802304000))

    def test_changed_or_missing_contract_code_test_or_migration_evidence_rejected(self):
        for path in ('docs/contracts/crm-api.openapi.yaml', 'docs/contracts/contact-event-intent.schema.json',
                     'docs/architecture/11-ADR-021-CONTACT-UPSERT-IDENTITY-DRAFT.md',
                     'src/Platform.Api/Crm/ContactEndpoints.cs',
                     'tests/Integration/ContactEndpointsTests.cs',
                     'tests/ClientConsumers/contact-upsert/new/Program.cs',
                     'scripts/contracts/generate.py', 'scripts/contracts/check.py'):
            with self.subTest(path=path):
                target = self.repo / path
                original = target.read_bytes()
                target.write_bytes(original + b'\n')
                self.assertFalse(self.approved())
                target.unlink()
                self.assertFalse(self.approved())
                target.write_bytes(original)

    def test_evidence_symlink_parent_is_not_accepted(self):
        directory = self.repo / 'src/Platform.Api/Crm'
        destination = self.repo / 'copied-crm'
        directory.rename(destination)
        directory.symlink_to(destination, target_is_directory=True)
        self.assertFalse(self.approved())

    def test_snapshot_hash_mismatch_cannot_be_approved(self):
        self.hashes = copy.deepcopy(self.hashes)
        self.hashes['current']['docs/contracts/contact-event-intent.schema.json'] = '0' * 64
        self.assertFalse(self.approved())

    def test_exact_target_runs_every_check_and_reports_reviewed_break(self):
        with tempfile.TemporaryDirectory() as temporary:
            clone=Path(temporary)/'repo'
            subprocess.run(['git','clone','--shared','--no-checkout',str(ROOT),str(clone)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            subprocess.run(['git','-C',str(clone),'checkout','--detach',REVIEWED_FIXTURE],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            paths=compatibility.git(clone,'ls-tree','-r','--name-only',BASE,'--','docs/contracts').decode().splitlines()
            original_run=subprocess.run
            calls=[]
            def run(command,*args,**kwargs):
                if command[0] == 'git': return original_run(command,*args,**kwargs)
                calls.append(command)
                if command[0] == 'docker': return SimpleNamespace(returncode=0,stdout=b'[]')
                if command[:2] == ['bash','tests/ClientConsumers/run.sh']:
                    return SimpleNamespace(returncode=0,stdout=b'TYPESCRIPT_WIRE_OK\nDOTNET_WIRE_OK\nDOTNET_WIRE_OK\nOLD_201_ONLY_CONSUMER_OK\nNEW_UPSERT_CONSUMER_OK\nSDK_PACKAGE_BOUNDARY_OK\nPACKAGED_CONSUMERS_OK\n')
                raise AssertionError('unexpected subprocess')
            def compare(old,new,source,workspace,base_toolchain,report):
                report['sdkChecks']={'typescriptSurface':False,'typescriptHistoricalConsumer':True,'dotnetSurface':False,'dotnetHistoricalConsumer':True}
                return False
            with patch.object(compatibility.subprocess,'run',side_effect=run), patch.object(sdk_surface,'prepare',return_value=clone), patch.object(sdk_surface,'compare',side_effect=compare):
                result=compatibility.evaluate_contract_snapshots(BASE,paths,clone,{}, {'dockerImage':'fixture','breakOn':'WARN'},Path('/fixture/oasdiff'),mvp_review=True,target=BASE)
            self.assertEqual('CONTRACT_BREAKING_REVIEWED',result['reason'])
            self.assertEqual(2,sum(command[0]=='docker' for command in calls))
            self.assertEqual(1,sum(command[:2]==['bash','tests/ClientConsumers/run.sh'] for command in calls))
            self.assertEqual(False,result['contactReview']['independentVerification'])

    def test_diverged_target_with_reviewed_merge_base_is_not_approved(self):
        with tempfile.TemporaryDirectory() as temporary:
            clone=Path(temporary)/'repo'
            subprocess.run(['git','clone','--shared','--no-checkout',str(ROOT),str(clone)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            subprocess.run(['git','-C',str(clone),'checkout','--detach',REVIEWED_FIXTURE],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            sibling=subprocess.check_output(['git','-C',str(clone),'-c','user.name=Fixture','-c','user.email=fixture@example.invalid',
                'commit-tree',BASE+'^{tree}','-p',BASE,'-m','unrelated target']).decode().strip()
            actual=subprocess.check_output(['git','-C',str(clone),'merge-base','HEAD',sibling]).decode().strip()
            self.assertEqual(BASE,actual)
            context={}
            with patch.object(compatibility,'evaluate_contract_snapshots',return_value={'reason':'SHOULD_NOT_REACH'}):
                with self.assertRaises(compatibility.GateError) as denied:
                    compatibility.run(sibling,clone,context,True)
            self.assertEqual('CONTRACT_POLICY_REVIEW_REQUIRED',str(denied.exception))
            self.assertEqual(sibling,context.get('targetSha'), 'resolved target must remain visible on a closed path')

    def test_policy_review_accepts_only_exact_sdk_surface_change(self):
        fn = getattr(compatibility, 'reviewed_contact_policy', lambda *args, **kwargs: False)
        path = 'scripts/contracts/sdk_surface.py'
        old = {path: 'dff098c30c19efe678a62a8cff4274a714d37a82d1dce1369b41253e9a7f6f3d'}
        new = {path: '460ae0d885a43024d5d2cd340c719902576381ad8229a13b23bf7d324b5a3a12'}
        self.assertTrue(fn(BASE, old, new, True, now=1795737600))
        self.assertFalse(fn('0'*40, old, new, True, now=1795737600))
        self.assertFalse(fn(BASE, old, new, False, now=1795737600))
        self.assertFalse(fn(BASE, old, {path:'0'*64}, True, now=1795737600))
        self.assertFalse(fn(BASE, old, {**new,'tools/contracts/oasdiff.json':'0'*64}, True, now=1795737600))
        self.assertFalse(fn(BASE, old, new, True, now=1802304000))

    def test_policy_exception_also_requires_exact_candidate_evidence(self):
        fn = getattr(compatibility, 'contact_policy_for_scope', lambda *args, **kwargs: False)
        path = 'scripts/contracts/sdk_surface.py'
        old = {path: 'dff098c30c19efe678a62a8cff4274a714d37a82d1dce1369b41253e9a7f6f3d'}
        new = {path: '460ae0d885a43024d5d2cd340c719902576381ad8229a13b23bf7d324b5a3a12'}
        self.assertTrue(fn(BASE, old, new, self.repo, True, now=1795737600))
        target = self.repo / 'src/Platform.Api/Crm/ContactEndpoints.cs'
        target.write_bytes(target.read_bytes()+b'\n')
        self.assertFalse(fn(BASE, old, new, self.repo, True, now=1795737600))

    def test_review_requires_all_historical_and_surface_sdk_checks_to_complete(self):
        fn = getattr(compatibility, 'contact_sdk_checks_completed', lambda *args: False)
        complete = {'typescriptSurface': False, 'typescriptHistoricalConsumer': True,
                    'dotnetSurface': False, 'dotnetHistoricalConsumer': True}
        self.assertTrue(fn({'sdkChecks': complete}))
        self.assertFalse(fn({}))
        for key in complete:
            incomplete = dict(complete)
            incomplete[key] = None
            self.assertFalse(fn({'sdkChecks': incomplete}))
        self.assertFalse(fn({'sdkChecks': {**complete, 'unexpected': True}}))

    def test_review_rejects_unexpected_historical_consumer_or_surface_results(self):
        fn = getattr(compatibility, 'contact_sdk_checks_approved', lambda *args: False)
        expected = {'typescriptSurface': False, 'typescriptHistoricalConsumer': True,
                    'dotnetSurface': False, 'dotnetHistoricalConsumer': True}
        self.assertTrue(fn({'sdkChecks': expected}))
        for key in expected:
            changed = dict(expected)
            changed[key] = not changed[key]
            self.assertFalse(fn({'sdkChecks': changed}))

    def test_contact_consumer_evidence_requires_old_new_and_upsert_checks(self):
        fn = getattr(compatibility, 'contact_consumers_passed', lambda *args: False)
        complete = b'TYPESCRIPT_WIRE_OK\nDOTNET_WIRE_OK\nDOTNET_WIRE_OK\nOLD_201_ONLY_CONSUMER_OK\nNEW_UPSERT_CONSUMER_OK\nSDK_PACKAGE_BOUNDARY_OK\nPACKAGED_CONSUMERS_OK\n'
        self.assertTrue(fn(0, complete))
        self.assertFalse(fn(1, complete))
        self.assertFalse(fn(0, b'PACKAGED_CONSUMERS_OK\n'))
        self.assertFalse(fn(0, complete.replace(b'NEW_UPSERT_CONSUMER_OK\n', b'')))
        self.assertFalse(fn(0, complete.replace(b'OLD_201_ONLY_CONSUMER_OK\n', b'')))
        self.assertFalse(fn(0, complete.replace(b'DOTNET_WIRE_OK\n', b'', 1)))

    def test_typescript_surface_break_does_not_skip_dotnet_surface_comparison(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            old, new, source, work = (root / item for item in ('old', 'new', 'source', 'work'))
            for snapshot in (old, new):
                path = snapshot / 'sdks/typescript/crm'
                path.mkdir(parents=True)
                (path / 'provenance.json').write_text('{"configurationSha256":"a","corvusPackageSha256":"b","openapiTypescript":"c"}')
                config = snapshot / 'tools/contracts/toolchain.json'
                config.parent.mkdir(parents=True)
                config.write_text('{"dotnetImage":"pinned"}')
            package = source / '.local/plan3-tools/corvus.json.cli.5.8.8.nupkg'
            package.parent.mkdir(parents=True)
            package.write_bytes(b'fixture')
            shutil.copytree(ROOT / 'tools/contracts/surface/dotnet', source / 'tools/contracts/surface/dotnet')
            work.mkdir()
            calls = []
            def simulated_run(command, cwd, timeout=300):
                calls.append(command)
                if command[0] == 'docker' and '/work/scanner' in command[-1]:
                    return b'{"compatible":true}\n{"compatible":true}\n'
                return b''
            report = {}
            with patch.object(sdk_surface, 'run', side_effect=simulated_run), patch.object(sdk_surface, 'scan_typescript', return_value=False):
                self.assertFalse(sdk_surface.compare(old, new, source, work, False, report))
            self.assertIn('sdkChecks', report, 'dotnet surface was skipped after TypeScript break')
            self.assertEqual(False, report['sdkChecks']['typescriptSurface'])
            self.assertEqual(True, report['sdkChecks']['dotnetSurface'])
            self.assertTrue(any(command[0] == 'docker' and '/work/scanner' in command[-1] for command in calls))

    def test_unauthorized_new_schema_cannot_be_approved(self):
        self.hashes['current']['docs/contracts/other.schema.json'] = '1' * 64
        self.assertFalse(self.approved())
