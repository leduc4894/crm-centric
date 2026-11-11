"""Generated files must be complete and local-only without altering inputs."""
import hashlib
import tempfile
import json
import os
import subprocess
import shutil
import sys
import unittest
from pathlib import Path

from scripts.contracts.check import check_generated_tree, check_local_refs

import prerequisites


def tree_hash(root):
    digest = hashlib.sha256()
    for path in sorted(root.rglob('*')):
        if path.is_file():
            digest.update(path.relative_to(root).as_posix().encode())
            digest.update(path.read_bytes())
    return digest.hexdigest()


# The generation and the gate need the pinned corvus.json.cli package. A
# checkout without the pinned local tooling skips this module with the reason
# of the missing prerequisite; it never fails for a missing tool it never
# provisioned.
@unittest.skipUnless(prerequisites.CORVUS[0], prerequisites.CORVUS[1])
class GeneratedCheckTests(unittest.TestCase):
    def test_generated_code_uses_checkout_stable_newlines(self):
        root = Path(__file__).resolve().parents[2]
        sample = root / 'sdks/dotnet/Crm.Client/Models/AutomationRule.cs'
        self.assertNotIn(b'\r\n', sample.read_bytes())

    def test_generated_check_rejects_missing_edited_extra_and_nonlocal_refs(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            expected = root / 'expected'
            actual = root / 'actual'
            expected.mkdir()
            actual.mkdir()
            (expected / 'client.txt').write_text('generated\n')
            (actual / 'client.txt').write_text('generated\n')
            self.assertIsNone(check_generated_tree(actual, expected))
            for mutation in ('missing', 'edited', 'extra'):
                file = actual / 'client.txt'
                file.write_text('generated\n')
                extra = actual / 'extra.txt'
                extra.unlink(missing_ok=True)
                if mutation == 'missing':
                    file.unlink()
                elif mutation == 'edited':
                    file.write_text('changed\n')
                else:
                    extra.write_text('unexpected\n')
                before = tree_hash(root)
                with self.subTest(mutation=mutation):
                    self.assertEqual(check_generated_tree(actual, expected), 'SDK_GENERATED_MISMATCH')
                    self.assertEqual(tree_hash(root), before)
            self.assertIsNone(check_local_refs({'$ref': '#/components/schemas/Local'}))
            self.assertEqual(check_local_refs({'$dynamicRef': 'https://example.invalid/schema'}), 'CONTRACT_NONLOCAL_REF')
            for ref in ('https://example.invalid/schema', '//example.invalid/schema', '%68ttps%3a%2f%2fexample.invalid/schema', '../outside.json', 'local.json?x=1', r'..\outside.json'):
                with self.subTest(ref=ref):
                    self.assertEqual(check_local_refs({'items': {'$ref': ref}}), 'CONTRACT_NONLOCAL_REF')


# The generation and the gate need the pinned corvus.json.cli package. A
# checkout without the pinned local tooling skips this module with the reason
# of the missing prerequisite; it never fails for a missing tool it never
# provisioned.
@unittest.skipUnless(prerequisites.CORVUS[0], prerequisites.CORVUS[1])
class GeneratedGateIntegrationTests(unittest.TestCase):
    def test_actual_gate_is_read_only_and_rejects_tampering(self):
        root = Path(__file__).resolve().parents[2]
        with tempfile.TemporaryDirectory() as tmp:
            candidate = Path(tmp)
            generate = subprocess.run([sys.executable, str(root / 'scripts/contracts/generate.py'), '--output-root', tmp], capture_output=True, text=True)
            self.assertEqual(generate.returncode, 0, 'fixture generation must succeed')
            def check(expected_reason, exit_code):
                before = tree_hash(candidate)
                result = subprocess.run([sys.executable, str(root / 'scripts/contracts/check.py'), '--output-root', tmp], capture_output=True, text=True)
                self.assertEqual(result.returncode, exit_code)
                payload = json.loads(result.stdout)
                self.assertEqual(payload['reason'], expected_reason)
                self.assertTrue(payload['correlationId'])
                self.assertEqual(tree_hash(candidate), before)
            check('SDK_GENERATED_CURRENT', 0)
            source = candidate / 'sdks/typescript/operations/index.ts'
            original = source.read_bytes()
            for mutation in ('missing', 'edited', 'extra'):
                source.write_bytes(original)
                extra = source.parent / 'unexpected.txt'
                extra.unlink(missing_ok=True)
                if mutation == 'missing':
                    source.unlink()
                elif mutation == 'edited':
                    source.write_bytes(b'changed')
                else:
                    extra.write_bytes(b'extra')
                with self.subTest(mutation=mutation):
                    check('SDK_GENERATED_MISMATCH', 1)


    def test_check_rejects_nonlocal_ref_without_mutating_source(self):
        root = Path(__file__).resolve().parents[2]
        with tempfile.TemporaryDirectory() as tmp:
            clone = Path(tmp)
            for name in ('package.json', 'package-lock.json', '.npmrc'):
                shutil.copyfile(root / name, clone / name)
            for directory in ('scripts/contracts', 'docs/contracts', 'tools/contracts'):
                shutil.copytree(root / directory, clone / directory, ignore=shutil.ignore_patterns('__pycache__'))
            tool = clone / '.local/plan3-tools'
            tool.mkdir(parents=True)
            shutil.copyfile(root / '.local/plan3-tools/corvus.json.cli.5.8.8.nupkg', tool / 'corvus.json.cli.5.8.8.nupkg')
            generate = subprocess.run([sys.executable, str(clone / 'scripts/contracts/generate.py')], capture_output=True, text=True)
            self.assertEqual(generate.returncode, 0, 'fixture generation must succeed')
            contract = clone / 'docs/contracts/platform-operations.openapi.yaml'
            contract.write_text(contract.read_text() + '\nx-test: \n  $ref: https://example.invalid/private\n')
            before = tree_hash(clone)
            result = subprocess.run([sys.executable, str(clone / 'scripts/contracts/check.py')], capture_output=True, text=True)
            self.assertEqual(result.returncode, 1)
            payload = json.loads(result.stdout)
            self.assertEqual(payload['reason'], 'CONTRACT_NONLOCAL_REF')
            self.assertTrue(payload['correlationId'])
            self.assertNotIn('example.invalid', result.stdout + result.stderr)
            self.assertEqual(tree_hash(clone), before)


    def test_changed_referenced_file_updates_provenance(self):
        root = Path(__file__).resolve().parents[2]
        with tempfile.TemporaryDirectory() as tmp:
            clone = Path(tmp) / 'clone'
            clone.mkdir()
            for name in ('package.json', 'package-lock.json', '.npmrc'):
                shutil.copyfile(root / name, clone / name)
            for directory in ('scripts/contracts', 'docs/contracts', 'tools/contracts'):
                shutil.copytree(root / directory, clone / directory, ignore=shutil.ignore_patterns('__pycache__'))
            tool = clone / '.local/plan3-tools'
            tool.mkdir(parents=True)
            shutil.copyfile(root / '.local/plan3-tools/corvus.json.cli.5.8.8.nupkg', tool / 'corvus.json.cli.5.8.8.nupkg')
            contract = clone / 'docs/contracts/platform-operations.openapi.yaml'
            contract.write_text(contract.read_text() + "\nx-provenance-test:\n  $ref: ./fixture.yaml#/Thing\n")
            fixture = clone / 'docs/contracts/fixture.yaml'
            fixture.write_text('Thing:\n  $ref: ./leaf.yaml#/Thing\n')
            leaf = clone / 'docs/contracts/leaf.yaml'
            leaf.write_text('Thing:\n  type: string\n')
            snapshots = []
            for i in range(2):
                if i:
                    leaf.write_text('Thing:\n  type: integer\n')
                out = Path(tmp) / f'out{i}'
                result = subprocess.run([sys.executable, str(clone / 'scripts/contracts/generate.py'), '--output-root', str(out)], capture_output=True, text=True)
                self.assertEqual(result.returncode, 0, 'fixture generation must succeed')
                ts = json.loads((out / 'sdks/typescript/operations/provenance.json').read_text())
                cs = json.loads((out / 'sdks/dotnet/Platform.Operations.Client/provenance.json').read_text())
                self.assertEqual(ts['inputHashes'], cs['inputHashes'])
                snapshots.append(ts)
            self.assertIn('inputHashes', snapshots[0])
            self.assertIn('fixture.yaml', snapshots[0]['inputHashes'])
            self.assertIn('leaf.yaml', snapshots[0]['inputHashes'])
            self.assertNotEqual(snapshots[0]['inputHashes']['leaf.yaml'], snapshots[1]['inputHashes']['leaf.yaml'])
            self.assertEqual(snapshots[0]['inputHashes']['fixture.yaml'], snapshots[1]['inputHashes']['fixture.yaml'])
            self.assertEqual(snapshots[0]['inputHashes']['platform-operations.openapi.yaml'], snapshots[1]['inputHashes']['platform-operations.openapi.yaml'])


# The generation and the gate need the pinned corvus.json.cli package. A
# checkout without the pinned local tooling skips this module with the reason
# of the missing prerequisite; it never fails for a missing tool it never
# provisioned.
@unittest.skipUnless(prerequisites.CORVUS[0], prerequisites.CORVUS[1])
class SafeToolFailuresTests(unittest.TestCase):
    def test_missing_python_dependencies_are_safe_json(self):
        root = Path(__file__).resolve().parents[2]
        for name in ('generate.py', 'check.py'):
            with self.subTest(name=name):
                result = subprocess.run([sys.executable, '-S', str(root / 'scripts/contracts' / name)], capture_output=True, text=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertTrue(result.stdout, 'expected structured JSON output')
                payload = json.loads(result.stdout)
                self.assertEqual(payload['reason'], 'SDK_TOOLCHAIN_UNAVAILABLE')
                self.assertTrue(payload['correlationId'])
                self.assertEqual(result.stderr, '')

    def test_missing_toolchain_config_key_is_safe_json(self):
        root = Path(__file__).resolve().parents[2]
        with tempfile.TemporaryDirectory() as tmp:
            clone = Path(tmp)
            for directory in ('scripts/contracts', 'tools/contracts'):
                shutil.copytree(root / directory, clone / directory, ignore=shutil.ignore_patterns('__pycache__'))
            config = clone / 'tools/contracts/toolchain.json'
            data = json.loads(config.read_text())
            del data['corvusPackageSha256']
            config.write_text(json.dumps(data))
            package = clone / '.local/plan3-tools/corvus.json.cli.5.8.8.nupkg'
            package.parent.mkdir(parents=True)
            package.write_bytes(b'x')
            for name in ('generate.py', 'check.py'):
                with self.subTest(name=name):
                    result = subprocess.run([sys.executable, str(clone / 'scripts/contracts' / name)], capture_output=True, text=True)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertTrue(result.stdout, 'expected structured JSON output')
                    payload = json.loads(result.stdout)
                    self.assertEqual(payload['reason'], 'SDK_TOOLCHAIN_UNAVAILABLE')
                    self.assertTrue(payload['correlationId'])
                    self.assertEqual(result.stderr, '')


# The generation and the gate need the pinned corvus.json.cli package. A
# checkout without the pinned local tooling skips this module with the reason
# of the missing prerequisite; it never fails for a missing tool it never
# provisioned.
@unittest.skipUnless(prerequisites.CORVUS[0], prerequisites.CORVUS[1])
class LocalReferenceTests(unittest.TestCase):
    def test_local_file_refs_cannot_escape_or_hide_remote_refs(self):
        from scripts.contracts.check import check_ref_files
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            main = root / 'main.yaml'
            nested = root / 'nested.yaml'
            main.write_text('value:\n  $ref: nested.yaml#/safe\n')
            nested.write_text('safe:\n  type: string\n')
            self.assertIsNone(check_ref_files(main, root))
            nested.write_text('safe:\n  $ref: https://example.invalid/private\n')
            self.assertEqual(check_ref_files(main, root), 'CONTRACT_NONLOCAL_REF')
            nested.unlink()
            self.assertEqual(check_ref_files(main, root), 'CONTRACT_NONLOCAL_REF')


# The generation and the gate need the pinned corvus.json.cli package. A
# checkout without the pinned local tooling skips this module with the reason
# of the missing prerequisite; it never fails for a missing tool it never
# provisioned.
@unittest.skipUnless(prerequisites.CORVUS[0], prerequisites.CORVUS[1])
class CanonicalRuleFixtureTests(unittest.TestCase):
    def test_rule_variants_and_invalid_action_use_canonical_schema(self):
        import yaml
        from jsonschema import Draft202012Validator, FormatChecker
        root = Path(__file__).resolve().parents[2]
        schema = yaml.safe_load((root / 'docs/contracts/crm-api.openapi.yaml').read_text())
        schema['$ref'] = '#/components/schemas/AutomationRule'
        validator = Draft202012Validator(schema, format_checker=FormatChecker())
        rule = {'ruleId': 'sample', 'version': 1, 'triggerEventType': 'crm.contact.created.v1',
                'condition': {'field': '/data/score', 'op': 'gt', 'value': 100},
                'actions': [{'type': 'crm.createNote.v1', 'input': {'text': 'sample'}},
                            {'type': 'module.send.v1', 'installationId': '00000000-0000-4000-8000-000000000001',
                             'input': {'message': 'sample'}}]}
        self.assertTrue(validator.is_valid(rule))
        rule['actions'][1].pop('installationId')
        self.assertFalse(validator.is_valid(rule))
