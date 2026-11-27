"""Candidate-specific reviewed scope cannot authorize unrelated changes."""
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from scripts.contracts import compatibility as gate

ROOT = Path(__file__).resolve().parents[2]
BASE = 'db04aeaecd7f4b6d083335a71418788d3c4561ca'
CANDIDATE = '8e62f5282aa14cdfca746f2aa78ec05b59ffcb86'

def reviewed_fixture_available():
    try:
        return subprocess.run(['git', '-C', str(ROOT), 'cat-file', '-e', CANDIDATE + '^{commit}'],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10).returncode == 0
    except (OSError, subprocess.SubprocessError):
        return False


@unittest.skipUnless(reviewed_fixture_available(), 'the immutable reviewed plan3 fixture is unavailable')
class Plan3ReviewTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.repo = Path(cls.tmp.name) / 'repo'
        subprocess.run(['git', 'clone', '--shared', '--no-checkout', str(ROOT), str(cls.repo)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        subprocess.run(['git', '-C', str(cls.repo), 'checkout', '--detach', CANDIDATE], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        (cls.repo / 'tools/ci/coverage.json').write_bytes((ROOT / 'tools/ci/coverage.json').read_bytes())

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def approved(self, **kwargs):
        args = dict(base=BASE, repo=self.repo, enabled=True, target=BASE, now=1795737600)
        args.update(kwargs)
        return gate.reviewed_plan3_candidate(**args)

    def test_only_exact_candidate_base_mode_target_and_time_are_approved(self):
        self.assertTrue(self.approved())
        for args in ({'base': '0'*40}, {'target': '0'*40}, {'enabled': False}, {'now': 1802304000}):
            with self.subTest(args=args): self.assertFalse(self.approved(**args))

    def test_evidence_and_untracked_drift_are_rejected(self):
        for name in ('src/Platform.Api/Crm/ContactEndpoints.cs', 'src/Platform.Infrastructure/Persistence/Migrations/20261127035000_ExactFieldNumbers.cs', 'docs/contracts/crm-api.openapi.yaml'):
            path = self.repo / name
            old = path.read_bytes()
            try:
                path.write_bytes(old + b'\n')
                self.assertFalse(self.approved())
                path.unlink()
                self.assertFalse(self.approved())
            finally: path.write_bytes(old)
        path = self.repo / 'docs/contracts/forged.schema.json'
        try:
            path.write_text('{}')
            self.assertFalse(self.approved())
        finally: path.unlink()

    def test_symlinked_evidence_rejected(self):
        path = self.repo / 'docs/contracts/crm-api.openapi.yaml'
        copy = path.with_name('copy.yaml')
        path.rename(copy)
        try:
            path.symlink_to(copy.name)
            self.assertFalse(self.approved())
        finally:
            path.unlink()
            copy.rename(path)

    def test_git_failure_rejected(self):
        with patch.object(gate, 'git', side_effect=gate.GateError('CONTRACT_BASE_UNAVAILABLE')):
            self.assertFalse(self.approved())

    def test_only_reviewed_ci_inventory_is_accepted(self):
        path = self.repo / 'tools/ci/coverage.json'
        previous = path.read_bytes()
        try:
            self.assertTrue(self.approved())
            path.write_bytes(path.read_bytes() + b'\n')
            self.assertFalse(self.approved())
        finally:
            path.write_bytes(previous)

    def test_policy_preflight_accepts_only_candidate_scope(self):
        with patch('time.time', return_value=1795737600):
            result = gate.check_baseline_configuration(self.repo, BASE, True, target=BASE)
            self.assertFalse(result['independentVerification'])
            with self.assertRaises(gate.GateError) as failure:
                gate.check_baseline_configuration(self.repo, BASE, True, target='0' * 40)
            self.assertEqual('CONTRACT_POLICY_REVIEW_REQUIRED', str(failure.exception))

    def evaluate(self, diff_fails=False, sdk_bad=False, consumers_bad=False, observed=None):
        from types import SimpleNamespace
        from scripts.contracts import sdk_surface
        paths = gate.git(self.repo, 'ls-tree', '-r', '--name-only', BASE, '--', 'docs/contracts').decode().splitlines()
        calls = []
        original = subprocess.run
        def fake_run(command, *args, **kwargs):
            if command[0] == 'git':
                return original(command, *args, **kwargs)
            calls.append(command)
            if command[0] == 'docker':
                return SimpleNamespace(returncode=1 if diff_fails else 0, stdout=b'[{"id":"breaking"}]' if diff_fails else b'[]')
            if command[:2] == ['npm', 'ci']:
                if observed is not None:
                    observed['npm'] = (command, Path(kwargs['cwd']))
                return SimpleNamespace(returncode=0, stdout=b'')
            if command[:2] == ['bash', 'tests/ClientConsumers/run.sh']:
                if observed is not None:
                    observed['consumer'] = Path(kwargs['cwd'])
                    observed['consumer_injected'] = (observed['consumer'] / 'tests/ClientConsumers/typescript/injected.txt').exists()
                markers = b'TYPESCRIPT_WIRE_OK\nDOTNET_WIRE_OK\nDOTNET_WIRE_OK\nOLD_201_ONLY_CONSUMER_OK\nNEW_UPSERT_CONSUMER_OK\nSDK_PACKAGE_BOUNDARY_OK\nPACKAGED_CONSUMERS_OK\n'
                return SimpleNamespace(returncode=0, stdout=markers if not consumers_bad else b'PACKAGED_CONSUMERS_OK\n')
            raise AssertionError(command)
        def compare(old, new, source, workspace, toolchain, report):
            if observed is not None:
                observed['npm_before_compare'] = 'npm' in observed
                observed['scanner_injected'] = (source / 'tools/contracts/surface/injected.txt').exists()
            report['sdkChecks'] = {'typescriptSurface': False, 'typescriptHistoricalConsumer': not sdk_bad,
                                   'dotnetSurface': False, 'dotnetHistoricalConsumer': True}
            return False
        def prepare(root, source, contracts, git, base=None):
            if observed is not None and base is None:
                observed['sdk_injected'] = (source / 'tests/ClientConsumers/typescript/injected.txt').exists()
            return self.repo
        with patch('time.time', return_value=1795737600), patch.object(gate.subprocess, 'run', side_effect=fake_run), patch.object(sdk_surface, 'prepare', side_effect=prepare), patch.object(sdk_surface, 'compare', side_effect=compare):
            result = gate.evaluate_contract_snapshots(BASE, paths, self.repo, {}, {'dockerImage': 'fixture', 'breakOn': 'WARN'}, Path('/fixture/oasdiff'), mvp_review=True, target=BASE)
        return result, calls

    def test_evaluation_runs_real_comparison_paths_and_labels_reviewed_break(self):
        result, calls = self.evaluate()
        self.assertEqual('CONTRACT_BREAKING_REVIEWED', result['reason'])
        self.assertEqual(2, sum(c[0] == 'docker' for c in calls))
        self.assertEqual(1, sum(c[:2] == ['bash', 'tests/ClientConsumers/run.sh'] for c in calls))
        self.assertFalse(result['contactReview']['independentVerification'])

    def test_actual_breaking_openapi_result_does_not_pass(self):
        result, _ = self.evaluate(diff_fails=True)
        self.assertEqual('CONTRACT_BREAKING', result['reason'])

    def test_unexpected_sdk_surface_or_consumer_failure_does_not_pass(self):
        result, _ = self.evaluate(sdk_bad=True)
        self.assertEqual('SDK_SURFACE_INCOMPATIBLE', result['reason'])
        with self.assertRaises(gate.GateError) as failure:
            self.evaluate(consumers_bad=True)
        self.assertEqual('SDK_TOOLCHAIN_REVIEW_REQUIRED', str(failure.exception))

    def test_ignored_evaluator_inputs_are_not_consumed(self):
        exclude = self.repo / '.git/info/exclude'
        original = exclude.read_bytes()
        files = [self.repo / 'tests/ClientConsumers/typescript/injected.txt',
                 self.repo / 'tools/contracts/surface/injected.txt',
                 self.repo / 'docs/contracts/injected.schema.json']
        try:
            exclude.write_bytes(original + b'\ninjected.txt\ninjected.schema.json\n')
            for path in files: path.write_text('unreviewed input')
            observed = {}
            result, _ = self.evaluate(observed=observed)
            self.assertEqual('CONTRACT_BREAKING_REVIEWED', result['reason'])
            self.assertEqual(False, observed['consumer_injected'])
            self.assertEqual(False, observed['sdk_injected'])
            self.assertEqual(False, observed['scanner_injected'])
            self.assertTrue(observed['npm_before_compare'])
            self.assertEqual(['npm', 'ci', '--offline', '--ignore-scripts', '--no-audit', '--no-fund'], observed['npm'][0])
            self.assertNotEqual(self.repo, observed['npm'][1])
        finally:
            exclude.write_bytes(original)
            for path in files: path.unlink(missing_ok=True)

    def test_snapshot_drift_cannot_be_approved_even_if_worktree_is_restored(self):
        import shutil
        original = shutil.copytree
        def changed_copy(src, dst, *args, **kwargs):
            result = original(src, dst, *args, **kwargs)
            if Path(src).parts[-2:] == ('docs', 'contracts') and Path(dst).parts[-2:] == ('docs', 'contracts'):
                target = Path(dst) / 'crm-api.openapi.yaml'
                target.write_bytes(target.read_bytes() + b'\n# drift\n')
            return result
        with patch.object(gate.shutil, 'copytree', side_effect=changed_copy):
            with self.assertRaises(gate.GateError) as failure:
                self.evaluate()
        self.assertEqual('CONTRACT_POLICY_REVIEW_REQUIRED', str(failure.exception))
