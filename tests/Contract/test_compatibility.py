"""Run the actual local compatibility gate on versioned valid input mutations."""
import copy
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import yaml

import prerequisites

ROOT = Path(__file__).resolve().parents[2]
CLI = ROOT / 'scripts/contracts/compatibility.py'
FIXTURE = ROOT / 'tests/Contract/fixtures/compatibility/base.openapi.yaml'

# The compatibility gate runs the pinned oasdiff binary. A checkout without the
# pinned local tooling skips this module with the reason of the missing
# prerequisite; it never fails for a missing tool it never provisioned.
@unittest.skipUnless(prerequisites.OASDIFF[0], prerequisites.OASDIFF[1])
class CompatibilityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.repo = Path(self.temp.name)
        (self.repo / 'docs/contracts').mkdir(parents=True)
        self.base = yaml.safe_load(FIXTURE.read_text())
        self.save(self.base)
        (self.repo / 'docs/contracts/event.schema.json').write_text(json.dumps({'$schema':'https://json-schema.org/draft/2020-12/schema', 'type':'string', 'maxLength':32, 'properties': {'title': {'type': 'string'}}, 'default': {'title': 'old'}}))
        self.git('init', '-q')
        self.git('config', 'user.name', 'Fixture')
        self.git('config', 'user.email', 'fixture@example.invalid')
        self.git('add', '.')
        self.git('commit', '-qm', 'base')
        self.sha = self.git('rev-parse', 'HEAD').strip()

    def tearDown(self):
        self.temp.cleanup()

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.repo, text=True)

    def save(self, value):
        (self.repo / 'docs/contracts/crm-api.openapi.yaml').write_text(yaml.safe_dump(value, sort_keys=False))
        (self.repo / 'docs/contracts/platform-operations.openapi.yaml').write_text(yaml.safe_dump(value, sort_keys=False))

    def gate(self, ref='HEAD'):
        p = subprocess.run(['python3', str(CLI), '--base-ref', ref], cwd=self.repo, capture_output=True, text=True, timeout=600)
        try:
            report = json.loads(p.stdout)
        except ValueError:
            self.fail('gate did not emit JSON')
        self.assertIn('reason', report)
        self.assertIn('correlationId', report)
        return p.returncode, report

    def assert_reason(self, reason):
        status, report = self.gate()
        self.assertEqual(report['reason'], reason)
        classes = {'CONTRACT_COMPATIBLE': 'compatible', 'CONTRACT_BREAKING': 'breaking', 'SECURITY_REVIEW_REQUIRED': 'security-review-required', 'SCHEMA_COMPATIBILITY_REVIEW_REQUIRED': 'schema-review-required'}
        self.assertEqual(report['classification'], classes[reason])
        self.assertEqual(status == 0, reason == 'CONTRACT_COMPATIBLE')
        self.assertEqual(report['baseSha'], self.sha)

    def test_unchanged_and_documentation_and_additive_are_compatible(self):
        self.assert_reason('CONTRACT_COMPATIBLE')
        value = copy.deepcopy(self.base)
        value['info']['description'] = 'only documentation'
        value['components']['schemas'] = {'Extra': {'type':'string'}}
        self.save(value)
        self.assert_reason('CONTRACT_COMPATIBLE')

    def test_wire_breaks_fail(self):
        def remove_op(v): del v['paths']['/things']['get']
        def remove_response(v): del v['paths']['/things']['patch']['responses']['412']
        def remove_property(v): del v['paths']['/things']['get']['responses']['200']['content']['application/json']['schema']['properties']['id']
        def require_request(v): v['paths']['/things']['patch']['requestBody']['content']['application/json']['schema']['required'] = ['name']
        def narrow_enum(v): v['paths']['/things']['get']['responses']['200']['content']['application/json']['schema']['properties']['status']['enum'] = ['one']
        def narrow_nullable(v): v['paths']['/things']['get']['responses']['200']['content']['application/json']['schema']['properties']['comment']['nullable'] = False
        def change_type(v): v['paths']['/things']['get']['responses']['200']['content']['application/json']['schema']['properties']['id']['type'] = 'integer'
        def remove_etag(v): del v['paths']['/things']['get']['responses']['200']['headers']['ETag']
        def change_match(v): v['paths']['/things']['patch']['parameters'][0]['name'] = 'If-None-Match'
        for mutation in (remove_op, remove_response, remove_property, require_request, narrow_enum, narrow_nullable, change_type, remove_etag, change_match):
            with self.subTest(mutation=mutation.__name__):
                value = copy.deepcopy(self.base)
                mutation(value)
                self.save(value)
                self.assert_reason('CONTRACT_BREAKING')

    def test_security_changes_require_review(self):
        def root(v): v['security'] = []
        def operation(v): v['paths']['/things']['get']['security'] = []
        def scope(v): v['paths']['/things']['get']['x-required-scopes'] = ['other']
        def bearer(v): v['components']['securitySchemes']['bearer']['scheme'] = 'basic'
        def signature(v): v['components']['securitySchemes']['webhookSignature']['name'] = 'other'
        for mutation in (root, operation, scope, bearer, signature):
            with self.subTest(mutation=mutation.__name__):
                value = copy.deepcopy(self.base)
                mutation(value)
                self.save(value)
                self.assert_reason('SECURITY_REVIEW_REQUIRED')

    def test_schema_validation_change_is_review_required_but_docs_are_not(self):
        path = self.repo / 'docs/contracts/event.schema.json'
        v = json.loads(path.read_text())
        v['title'] = 'Document only'
        path.write_text(json.dumps(v))
        self.assert_reason('CONTRACT_COMPATIBLE')
        v['maxLength'] = 10
        path.write_text(json.dumps(v))
        self.assert_reason('SCHEMA_COMPATIBILITY_REVIEW_REQUIRED')

    def test_missing_base_and_removed_schema_fail_closed(self):
        code, report = self.gate('not-a-real-ref')
        self.assertNotEqual(code, 0)
        self.assertEqual(report['reason'], 'CONTRACT_BASE_UNAVAILABLE')
        (self.repo / 'docs/contracts/event.schema.json').unlink()
        self.assert_reason('SCHEMA_COMPATIBILITY_REVIEW_REQUIRED')

    def test_property_named_title_remains_schema_semantics(self):
        path = self.repo / 'docs/contracts/event.schema.json'
        value = json.loads(path.read_text())
        value['properties']['title']['type'] = 'integer'
        path.write_text(json.dumps(value))
        self.assert_reason('SCHEMA_COMPATIBILITY_REVIEW_REQUIRED')

    def test_invalid_and_nonlocal_inputs_fail(self):
        path = self.repo / 'docs/contracts/crm-api.openapi.yaml'
        for contents in ('openapi: [broken', 'openapi: 9.0.0\n', yaml.safe_dump({'openapi':'3.0.3', 'info':{'title':'x','version':'1'}, 'paths':{}, 'components':{'schemas':{'X':{'$ref':'https://example.invalid/x'}}}})):
            with self.subTest(contents=contents):
                path.write_text(contents)
                code, report = self.gate()
                self.assertNotEqual(code, 0)
                self.assertNotEqual(report['reason'], 'CONTRACT_COMPATIBLE')

    def test_additional_schema_must_be_valid_and_existing_renamed_fails(self):
        path = self.repo / 'docs/contracts/event.schema.json'
        renamed = self.repo / 'docs/contracts/event.new.json'
        path.rename(renamed)
        self.assert_reason('SCHEMA_COMPATIBILITY_REVIEW_REQUIRED')

    def test_security_scope_order_is_not_a_change(self):
        # Set the fixture baseline to a valid two-scope extension in a child commit.
        value = copy.deepcopy(self.base)
        value['paths']['/things']['get']['x-required-scopes'] = ['a', 'b']
        self.save(value)
        self.git('add', '.')
        self.git('commit', '-qm', 'scope fixture')
        value['paths']['/things']['get']['x-required-scopes'] = ['b', 'a']
        self.save(value)
        status, report = self.gate()
        self.assertEqual(status, 0)
        self.assertEqual(report['reason'], 'CONTRACT_COMPATIBLE')

    def test_new_business_operations_require_security_metadata(self):
        value = copy.deepcopy(self.base)
        value['paths']['/new'] = {'get': {'operationId': 'newThing', 'responses': {'200': {'description': 'ok'}}}}
        self.save(value)
        self.assert_reason('SECURITY_REVIEW_REQUIRED')

    def test_new_business_operation_with_security_metadata_is_additive(self):
        value = copy.deepcopy(self.base)
        value['paths']['/new'] = {'get': {'operationId': 'newThing', 'x-required-scopes':['things.read'], 'responses': {'200': {'description': 'ok'}}}}
        (self.repo / 'docs/contracts/crm-api.openapi.yaml').write_text(yaml.safe_dump(value, sort_keys=False))
        self.assert_reason('CONTRACT_COMPATIBLE')

    def test_missing_tool_fails_closed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for directory in ('scripts/contracts', 'tools/contracts'):
                (root / directory).mkdir(parents=True)
            for name in ('compatibility.py', 'check.py'):
                shutil.copy(ROOT / 'scripts/contracts' / name, root / 'scripts/contracts' / name)
            shutil.copy(ROOT / 'tools/contracts/oasdiff.json', root / 'tools/contracts/oasdiff.json')
            proc = subprocess.run(['python3', str(root / 'scripts/contracts/compatibility.py'), '--base-ref', 'HEAD'], cwd=self.repo, capture_output=True, text=True)
            self.assertNotEqual(proc.returncode, 0)
            self.assertEqual(json.loads(proc.stdout)['reason'], 'CONTRACT_TOOL_UNAVAILABLE')

    def test_unrecognised_schema_dialect_fails_closed(self):
        path = self.repo / 'docs/contracts/event.schema.json'
        schema = json.loads(path.read_text())
        schema['$schema'] = 'https://example.invalid/schema'
        path.write_text(json.dumps(schema))
        code, report = self.gate()
        self.assertNotEqual(code, 0)
        self.assertNotEqual(report['reason'], 'CONTRACT_COMPATIBLE')

    def test_added_schema_requires_review_without_fixture_proof(self):
        path = self.repo / 'docs/contracts/extra.schema.json'
        path.write_text(json.dumps({'$schema': 'https://json-schema.org/draft/2020-12/schema', 'type':'string'}))
        try:
            self.assert_reason('SCHEMA_COMPATIBILITY_REVIEW_REQUIRED')
        finally:
            path.unlink()

    def test_local_refs_can_be_compared_without_network(self):
        value = copy.deepcopy(self.base)
        value['components']['schemas'] = {'Event': {'$ref': 'event.schema.json'}}
        self.save(value)
        self.git('add', '.')
        self.git('commit', '-qm', 'local references')
        code, report = self.gate()
        self.assertEqual(code, 0, report['reason'])

    def test_schema_default_object_title_is_semantic(self):
        path = self.repo / 'docs/contracts/event.schema.json'
        value = json.loads(path.read_text())
        value['default']['title'] = 'new'
        path.write_text(json.dumps(value))
        self.assert_reason('SCHEMA_COMPATIBILITY_REVIEW_REQUIRED')

    def test_candidate_policy_change_cannot_rebaseline(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for directory in ('scripts/contracts', 'tools/contracts', '.local/plan3-tools/oasdiff-1.37.0'):
                (root / directory).mkdir(parents=True)
            for name in ('compatibility.py', 'check.py'):
                shutil.copy(ROOT / 'scripts/contracts' / name, root / 'scripts/contracts' / name)
            policy = json.loads((ROOT / 'tools/contracts/oasdiff.json').read_text())
            policy['archiveSha256'] = '0' * 64
            (root / 'tools/contracts/oasdiff.json').write_text(json.dumps(policy))
            (root / policy['binaryPath']).symlink_to(ROOT / policy['binaryPath'])
            proc = subprocess.run(['python3', str(root / 'scripts/contracts/compatibility.py'), '--base-ref', 'HEAD'], cwd=self.repo, capture_output=True, text=True)
            self.assertNotEqual(proc.returncode, 0)
            self.assertEqual(json.loads(proc.stdout)['reason'], 'CONTRACT_POLICY_REVIEW_REQUIRED')

    def test_invalid_cli_argument_returns_safe_json(self):
        proc = subprocess.run(['python3', str(CLI), '--unknown-option'], cwd=self.repo, capture_output=True, text=True)
        self.assertNotEqual(proc.returncode, 0)
        self.assertTrue(proc.stdout, 'argument error must produce safe JSON')
        report = json.loads(proc.stdout)
        self.assertEqual(report['reason'], 'CONTRACT_ARGUMENT_INVALID')
        self.assertTrue(report['correlationId'])
        self.assertFalse(proc.stderr)

    def test_unchanged_unsupported_dialect_is_invalid(self):
        path = self.repo / 'docs/contracts/event.schema.json'
        schema = json.loads(path.read_text())
        schema['$schema'] = 'https://example.invalid/schema'
        path.write_text(json.dumps(schema))
        self.git('add', '.')
        self.git('commit', '-qm', 'unsupported dialect')
        code, report = self.gate()
        self.assertNotEqual(code, 0)
        self.assertEqual(report['reason'], 'CONTRACT_INPUT_INVALID')

    def test_missing_local_module_returns_safe_json(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / 'scripts/contracts').mkdir(parents=True)
            shutil.copy(CLI, root / 'scripts/contracts/compatibility.py')
            proc = subprocess.run(['python3', str(root / 'scripts/contracts/compatibility.py')], cwd=self.repo, capture_output=True, text=True)
            self.assertNotEqual(proc.returncode, 0)
            self.assertTrue(proc.stdout)
            self.assertEqual(json.loads(proc.stdout)['reason'], 'CONTRACT_TOOL_ERROR')
            self.assertFalse(proc.stderr)

    def test_transitive_schema_ref_change_requires_review_and_hashes(self):
        path = self.repo / 'docs/contracts/event.schema.json'
        referenced = self.repo / 'docs/contracts/defs.json'
        value = json.loads(path.read_text())
        value['$ref'] = 'defs.json#/$defs/thing'
        path.write_text(json.dumps(value))
        referenced.write_text(json.dumps({'$defs': {'thing': {'type':'string'}}}))
        self.git('add', '.')
        self.git('commit', '-qm', 'schema reference')
        status, initial = self.gate()
        self.assertEqual(status, 0, initial['reason'])
        referenced.write_text(json.dumps({'$defs': {'thing': {'type':'string', 'description':'document only'}}}))
        status, documentation = self.gate()
        self.assertEqual(status, 0, documentation['reason'])
        referenced.write_text(json.dumps({'$defs': {'thing': {'type':'integer'}}}))
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'SCHEMA_COMPATIBILITY_REVIEW_REQUIRED')
        key = 'docs/contracts/defs.json'
        self.assertIn(key, report['inputHashes']['base'])
        self.assertIn(key, report['inputHashes']['current'])
        self.assertNotEqual(report['inputHashes']['base'][key], report['inputHashes']['current'][key])

    def test_internal_path_item_ref_scope_change_requires_review(self):
        value = copy.deepcopy(self.base)
        value['openapi'] = '3.1.0'
        value['components']['pathItems'] = {'Thing': value['paths']['/things']}
        value['paths']['/things'] = {'$ref': '#/components/pathItems/Thing'}
        self.save(value)
        self.git('add', '.')
        self.git('commit', '-qm', 'internal path item')
        status, initial = self.gate()
        self.assertEqual(status, 0, initial['reason'])
        value['components']['pathItems']['Thing']['get']['x-required-scopes'] = ['different']
        self.save(value)
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'SECURITY_REVIEW_REQUIRED')

    def test_external_path_item_ref_fails_closed(self):
        value = copy.deepcopy(self.base)
        value['openapi'] = '3.1.0'
        (self.repo / 'docs/contracts/path.yaml').write_text(yaml.safe_dump(value['paths']['/things']))
        value['paths']['/things'] = {'$ref': 'path.yaml'}
        self.save(value)
        self.git('add', '.')
        self.git('commit', '-qm', 'external path item')
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'CONTRACT_INPUT_INVALID')

    def test_unsupported_openapi_version_rejected_even_unchanged(self):
        for version in ('3.999.0', '3.1.999'):
            with self.subTest(version=version):
                value = copy.deepcopy(self.base)
                value['openapi'] = version
                self.save(value)
                self.git('add', '.')
                self.git('commit', '-qm', 'unsupported OpenAPI')
                status, report = self.gate()
                self.assertNotEqual(status, 0)
                self.assertEqual(report['reason'], 'CONTRACT_INPUT_INVALID')

    def test_all_schema_closures_are_hashed_when_one_changes(self):
        root = self.repo / 'docs/contracts'
        (root / 'z.schema.json').write_text(json.dumps({'$schema':'https://json-schema.org/draft/2020-12/schema', '$ref':'z-defs.json'}))
        (root / 'z-defs.json').write_text(json.dumps({'type':'string'}))
        self.git('add', '.')
        self.git('commit', '-qm', 'second schema')
        event = root / 'event.schema.json'
        value = json.loads(event.read_text())
        value['maxLength'] = 9
        event.write_text(json.dumps(value))
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'SCHEMA_COMPATIBILITY_REVIEW_REQUIRED')
        self.assertIn('docs/contracts/z-defs.json', report['inputHashes']['base'])
        self.assertIn('docs/contracts/z-defs.json', report['inputHashes']['current'])

    def test_nested_path_item_ref_siblings_fail_closed(self):
        value = copy.deepcopy(self.base)
        value['openapi'] = '3.1.0'
        value['components']['pathItems'] = {
            'Real': value['paths']['/things'],
            'Alias': {'$ref': '#/components/pathItems/Real', 'get': {'responses': {'200': {'description': 'override'}}}},
        }
        value['paths']['/things'] = {'$ref': '#/components/pathItems/Alias'}
        self.save(value)
        self.git('add', '.')
        self.git('commit', '-qm', 'ambiguous path reference')
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'CONTRACT_INPUT_INVALID')

    def test_missing_base_toolchain_fails_closed(self):
        toolchain = self.repo / 'tools/contracts/toolchain.json'
        toolchain.parent.mkdir(parents=True)
        toolchain.write_text('{}\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'incomplete baseline toolchain')
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'SDK_TOOLCHAIN_REVIEW_REQUIRED')

    def test_policy_or_waiver_edit_cannot_rebaseline(self):
        policy = self.repo / 'tools/contracts/waivers.json'
        policy.parent.mkdir(parents=True)
        policy.write_text('{"ignoreAll":true}\n')
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'CONTRACT_POLICY_REVIEW_REQUIRED')

    def test_unusable_baseline_generator_fails_closed(self):
        files = {
            'tools/contracts/toolchain.json': '{"generator":"v1"}',
            'scripts/contracts/generate.py': '# base generator\n',
            'scripts/contracts/check.py': '# base checker\n',
            'package.json': '{}', 'package-lock.json': '{}', '.npmrc': '',
            'sdks/typescript/crm/provenance.json': '{}',
        }
        for name, contents in files.items():
            path = self.repo / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(contents)
        self.git('add', '.')
        self.git('commit', '-qm', 'complete toolchain baseline')
        (self.repo / 'scripts/contracts/generate.py').write_text('# incompatible candidate generator\n')
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'SDK_TOOLCHAIN_REVIEW_REQUIRED')
        self.assertEqual(report['sdkBaseline'], 'historical-toolchain')

    def test_operation_id_rename_is_sdk_surface_incompatible(self):
        value = copy.deepcopy(self.base)
        value['paths']['/things']['get']['operationId'] = 'renamedListThings'
        self.save(value)
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'SDK_SURFACE_INCOMPATIBLE')
        self.assertFalse(report['sdkToolchainChanged'])

    def test_generator_only_break_is_not_hidden(self):
        for directory in ('scripts/contracts', 'tools/contracts'):
            shutil.copytree(ROOT / directory, self.repo / directory, dirs_exist_ok=True, ignore=shutil.ignore_patterns('__pycache__', '*.pyc'))
        for name in ('package.json', 'package-lock.json', '.npmrc', '.node-version', 'global.json'):
            shutil.copyfile(ROOT / name, self.repo / name)
        sdk = self.repo / 'sdks/typescript/crm/provenance.json'
        sdk.parent.mkdir(parents=True)
        sdk.write_text('{}')
        self.git('add', '.')
        self.git('commit', '-qm', 'real generator baseline')
        generator = self.repo / 'scripts/contracts/generate.py'
        source = generator.read_text()
        self.assertIn('export function createApiClient(', source)
        generator.write_text(source.replace('export function createApiClient(', 'export function renamedClient('))
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'SDK_SURFACE_INCOMPATIBLE')
        self.assertTrue(report['sdkToolchainChanged'])

    def test_baseline_consumer_compile_failure_is_incompatible(self):
        for directory in ('scripts/contracts', 'tools/contracts'):
            shutil.copytree(ROOT / directory, self.repo / directory, dirs_exist_ok=True, ignore=shutil.ignore_patterns('__pycache__', '*.pyc'))
        for name in ('package.json', 'package-lock.json', '.npmrc', '.node-version', 'global.json'):
            shutil.copyfile(ROOT / name, self.repo / name)
        sdk = self.repo / 'sdks/typescript/crm/provenance.json'
        sdk.parent.mkdir(parents=True)
        sdk.write_text('{}')
        types = self.repo / 'tests/ClientConsumers/typescript/types.ts'
        types.parent.mkdir(parents=True)
        types.write_text("import { removedEntryPoint } from '@crm-centric/crm-client'; void removedEntryPoint;\n")
        cs = self.repo / 'tests/ClientConsumers/dotnet/Program.cs'
        cs.parent.mkdir(parents=True)
        cs.write_text('System.Console.WriteLine("fixture");\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'historical consumer')
        # Reproduce hosted npm ci: locked tarballs exist but registry metadata
        # is absent. Warm an isolated install rather than altering this checkout.
        with tempfile.TemporaryDirectory(prefix='baseline-consumer-cache-') as temporary:
            install=Path(temporary)/'install';cache=Path(temporary)/'cache';install.mkdir()
            for name in ('package.json','package-lock.json','.npmrc','.node-version',
                         'apps/crm-web/package.json','sdks/typescript/crm/package.json',
                         'sdks/typescript/operations/package.json'):
                target=install/name;target.parent.mkdir(parents=True,exist_ok=True)
                shutil.copyfile(ROOT/name,target)
            env={**os.environ,'npm_config_cache':str(cache)}
            warm=subprocess.run(['npm','ci','--ignore-scripts','--no-audit','--no-fund'],
                                cwd=install,env=env,capture_output=True,text=True,timeout=300)
            self.assertEqual(0,warm.returncode,'isolated locked install failed (diagnostics suppressed)')
            with patch.dict(os.environ,{'npm_config_cache':str(cache)}):status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'SDK_SURFACE_INCOMPATIBLE')
        self.assertEqual(report['sdkBaseline'], 'historical-toolchain')

    def test_missing_historical_consumer_artifacts_fail_closed(self):
        for directory in ('scripts/contracts', 'tools/contracts'):
            shutil.copytree(ROOT / directory, self.repo / directory, dirs_exist_ok=True, ignore=shutil.ignore_patterns('__pycache__', '*.pyc'))
        for name in ('package.json', 'package-lock.json', '.npmrc', '.node-version', 'global.json'):
            shutil.copyfile(ROOT / name, self.repo / name)
        sdk = self.repo / 'sdks/typescript/crm/provenance.json'
        sdk.parent.mkdir(parents=True)
        sdk.write_text('{}')
        self.git('add', '.')
        self.git('commit', '-qm', 'toolchain baseline without consumer artifacts')
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'SDK_TOOLCHAIN_REVIEW_REQUIRED')
        self.assertEqual(report['sdkBaseline'], 'historical-toolchain')

    def test_existing_surface_policy_change_cannot_rebaseline(self):
        policy = self.repo / 'tools/contracts/surface/policy.json'
        policy.parent.mkdir(parents=True)
        policy.write_text('{"allowBreaking":false}\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'existing surface policy')
        policy.write_text('{"allowBreaking":true}\n')
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'CONTRACT_POLICY_REVIEW_REQUIRED')

    def test_candidate_new_policy_file_cannot_rebaseline(self):
        policy = self.repo / 'tools/contracts/compatibility-policy.json'
        policy.parent.mkdir(parents=True)
        policy.write_text('{"skipSdkSurface":true}\n')
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'CONTRACT_POLICY_REVIEW_REQUIRED')

    def test_policy_like_files_in_every_reviewed_location_are_reviewed(self):
        for relative in ('scripts/contracts/waivers.json', 'waivers.json', 'docs/contracts/waivers.json', 'tools/contracts/exceptions.json'):
            with self.subTest(path=relative):
                path = self.repo / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('{"ignoreAll":true}\n')
                try:
                    status, report = self.gate()
                    self.assertNotEqual(status, 0)
                    self.assertEqual(report['reason'], 'CONTRACT_POLICY_REVIEW_REQUIRED')
                finally:
                    path.unlink()

    def test_bootstrap_baseline_uses_one_pinned_configuration_for_both_sides(self):
        status, report = self.gate()
        self.assertEqual(status, 0, report['reason'])
        self.assertEqual(report['sdkBaseline'], 'bootstrap-no-released-consumer')
        self.assertEqual(report['sdkToolchain']['base'], report['sdkToolchain']['current'])
        self.assertFalse(report['sdkToolchainChanged'])

    def test_generator_only_removal_of_every_declaration_is_surface_incompatible(self):
        for directory in ('scripts/contracts', 'tools/contracts'):
            shutil.copytree(ROOT / directory, self.repo / directory, dirs_exist_ok=True, ignore=shutil.ignore_patterns('__pycache__', '*.pyc'))
        for name in ('package.json', 'package-lock.json', '.npmrc', '.node-version', 'global.json'):
            shutil.copyfile(ROOT / name, self.repo / name)
        sdk = self.repo / 'sdks/typescript/crm/provenance.json'
        sdk.parent.mkdir(parents=True)
        sdk.write_text('{}')
        self.git('add', '.')
        self.git('commit', '-qm', 'real generator baseline')
        generator = self.repo / 'scripts/contracts/generate.py'
        source = generator.read_text()
        needle = "(ts / 'index.ts').write_text(\""
        start = source.index(needle)
        end = source.index('")', start)
        generator.write_text(source[:start] + "(ts / 'index.ts').write_text('')" + source[end + 2:])
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'SDK_SURFACE_INCOMPATIBLE')
        self.assertTrue(report['sdkToolchainChanged'])

    def test_sdk_present_but_toolchain_missing_is_not_bootstrap(self):
        sdk = self.repo / 'sdks/typescript/crm/provenance.json'
        sdk.parent.mkdir(parents=True)
        sdk.write_text('{}')
        self.git('add', '.')
        self.git('commit', '-qm', 'SDK without toolchain')
        status, report = self.gate()
        self.assertNotEqual(status, 0)
        self.assertEqual(report['reason'], 'SDK_TOOLCHAIN_REVIEW_REQUIRED')
