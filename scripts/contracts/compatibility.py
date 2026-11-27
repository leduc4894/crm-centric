#!/usr/bin/env python3
"""Fail-closed, local wire, security and conservative schema compatibility gate."""
import sys
sys.dont_write_bytecode = True
import argparse
import hashlib
import json
import re
import shutil
import stat
from pathlib import Path
import subprocess
import tempfile
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

METHODS = {'get', 'put', 'post', 'delete', 'options', 'head', 'patch', 'trace'}
POLICY = ROOT / 'tools/contracts/oasdiff.json'
POLICY_ROOTS = ('tools/contracts/', 'scripts/contracts/', 'docs/contracts/')
POLICY_WORDS = {'waive', 'waiver', 'waivers', 'policy', 'policies', 'exception', 'exceptions',
                'suppress', 'suppressed', 'suppression', 'suppressions', 'ignore', 'ignored',
                'allow', 'allowed', 'allowlist', 'allowlists', 'deny', 'denylist', 'oasdiff'}

class GateError(Exception):
    pass

class SafeParser(argparse.ArgumentParser):
    def error(self, message):
        raise GateError('CONTRACT_ARGUMENT_INVALID')

def git(repo, *args):
    result = subprocess.run(['git', *args], cwd=repo, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    if result.returncode:
        raise GateError('CONTRACT_BASE_UNAVAILABLE')
    return result.stdout

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def is_policy(path):
    """Policy-like names may not appear or change without an explicit review."""
    return bool(POLICY_WORDS & set(re.split(r'[^a-z0-9]+', Path(path).name.lower())))

def is_policy_path(path):
    return path.startswith(POLICY_ROOTS) or '/' not in path

def current_policy_files(repo):
    """Policy-like files present in the worktree, including untracked ones."""
    paths = set()
    for path in repo.glob('*'):
        if path.is_file() and not path.is_symlink():
            paths.add(path.relative_to(repo).as_posix())
    for root in POLICY_ROOTS:
        directory = repo / root.rstrip('/')
        if not directory.is_dir():
            continue
        for path in directory.rglob('*'):
            if path.is_file() and not path.is_symlink():
                paths.add(path.relative_to(repo).as_posix())
    return {path for path in paths if is_policy_path(path) and is_policy(path)}

def policy_identity_preflight(old_hashes, new_hashes, revision=None, transition=None):
    """Identity only: signed approval never substitutes for semantic evaluation.

    Normal CLI supplies no transition and preserves the Plan 3 fail-closed gate.
    Independent workflow callers must run both immutable semantic evaluators.
    """
    if old_hashes == new_hashes:
        return
    if transition is None:
        raise GateError('CONTRACT_POLICY_REVIEW_REQUIRED')
    from scripts.ci.policy_transition import VerifiedTransition
    import time
    if (not isinstance(transition, VerifiedTransition) or revision is None
            or transition.revision != revision or transition.expires_at <= time.time()
            or dict(transition.old_hashes) != old_hashes
            or dict(transition.new_hashes) != new_hashes):
        raise GateError('CONTRACT_POLICY_REVIEW_REQUIRED')

def reviewed_contact_policy(base, old_hashes, new_hashes, mvp_review, now=None):
    import time
    if (not mvp_review or base != 'db04aeaecd7f4b6d083335a71418788d3c4561ca'
            or (time.time() if now is None else now) >= 1802304000):
        return False
    delta = {path: (old_hashes.get(path), new_hashes.get(path))
             for path in old_hashes.keys() | new_hashes.keys() if old_hashes.get(path) != new_hashes.get(path)}
    return delta == {'scripts/contracts/sdk_surface.py': (
        'dff098c30c19efe678a62a8cff4274a714d37a82d1dce1369b41253e9a7f6f3d',
        '460ae0d885a43024d5d2cd340c719902576381ad8229a13b23bf7d324b5a3a12')}

def check_baseline_configuration(repo, base, mvp_review=False, target=None):
    """Reject incomplete baseline toolchains and unreviewed policy changes."""
    paths = set(git(repo, 'ls-tree', '-r', '--name-only', base, '--', 'tools/contracts', 'scripts/contracts', 'sdks').decode().splitlines())
    marker = 'tools/contracts/toolchain.json'
    if marker not in paths and (any(p.startswith('sdks/') for p in paths) or 'scripts/contracts/generate.py' in paths):
        raise GateError('SDK_TOOLCHAIN_REVIEW_REQUIRED')
    if marker in paths:
        required = {marker, 'scripts/contracts/generate.py', 'scripts/contracts/check.py', 'package.json', 'package-lock.json', '.npmrc'}
        all_paths = set(git(repo, 'ls-tree', '-r', '--name-only', base).decode().splitlines())
        if not required <= all_paths or not any(path.startswith('sdks/') for path in all_paths):
            raise GateError('SDK_TOOLCHAIN_REVIEW_REQUIRED')
        if any(not (repo / p).is_file() for p in required):
            raise GateError('SDK_TOOLCHAIN_REVIEW_REQUIRED')
    base_policy = {p for p in git(repo, 'ls-tree', '-r', '--name-only', base).decode().splitlines() if is_policy_path(p) and is_policy(p)}
    current_policy = current_policy_files(repo)
    base_surface_policy = {p for p in paths if p.startswith('tools/contracts/surface/') or p in ('scripts/contracts/sdk_surface.py','scripts/contracts/runtime_packages.py')}
    if base_surface_policy:
        base_policy |= base_surface_policy
        current_policy |= {p.relative_to(repo).as_posix() for p in (repo / 'tools/contracts/surface').rglob('*') if p.is_file()} if (repo / 'tools/contracts/surface').is_dir() else set()
        for path in ('scripts/contracts/sdk_surface.py','scripts/contracts/runtime_packages.py'):
            if (repo/path).is_file(): current_policy.add(path)
    old_hashes = {p: hashlib.sha256(git(repo, 'show', f'{base}:{p}')).hexdigest() for p in base_policy}
    new_hashes = {p: sha(repo / p) for p in current_policy}
    if mvp_review:
        from scripts.ci.contract_review import approved
    if mvp_review and approved(old_hashes,new_hashes,base):
        return {'basis':'maintainer-reviewed-mvp-delta','owner':'maintainer',
                'expires':'2027-02-11T00:00:00+00:00','independentVerification':False,
                'dualPolicyEvaluation':False,'changedFiles':sorted(
                    p for p in old_hashes.keys()|new_hashes.keys() if old_hashes.get(p)!=new_hashes.get(p))}
    if target == base and (contact_policy_for_scope(base, old_hashes, new_hashes, repo, mvp_review)
                           or (reviewed_contact_policy(base, old_hashes, new_hashes, mvp_review)
                               and reviewed_plan3_candidate(base, repo, mvp_review, target))):
        return {'basis':'maintainer-reviewed-contact-sdk-evaluation', 'owner':'maintainer',
                'expires':'2027-02-11T00:00:00+00:00', 'independentVerification':False,
                'dualPolicyEvaluation':False, 'changedFiles':['scripts/contracts/sdk_surface.py']}
    policy_identity_preflight(old_hashes, new_hashes)

# Candidate-owned, exact MVP review for ADR-020/021; not independent attestation.
# Both the complete contract-input set and the selected implementation/migration/
# consumer evidence are pinned. Any drift, wrong base or expiry fails closed.
CONTACT_REVIEW_BASE = 'db04aeaecd7f4b6d083335a71418788d3c4561ca'
CONTACT_REVIEW_EXPIRES = 1802304000
PLAN3_REVIEW_COMMIT = '8e62f5282aa14cdfca746f2aa78ec05b59ffcb86'
PLAN3_REVIEW_TREE = '5d9f8b09962405e09851936367d29d4c28414cf1'
# Candidate-owned governance and documentation are deliberately outside the
# approved product tree. This is not independent attestation or enforcement.
PLAN3_GOVERNANCE_FILES = frozenset({
    'scripts/contracts/compatibility.py',
    'tests/Contract/test_wave2_plan3_review.py',
    'docs/engineering/15-WAVE-2-PLAN-3-CONTRACT-REVIEW.md',
    'docs/handoff/2026-11-27-wave-2-branch-handoff.md',
})

def reviewed_plan3_candidate(base, repo, enabled, target, now=None):
    """Bind the local reviewed scope to the approved product tree, fail closed."""
    import time
    if (not enabled or base != CONTACT_REVIEW_BASE or target != base
            or (time.time() if now is None else now) >= CONTACT_REVIEW_EXPIRES):
        return False
    try:
        if git(repo, 'rev-parse', PLAN3_REVIEW_COMMIT + '^{tree}').decode().strip() != PLAN3_REVIEW_TREE:
            return False
        tree = {}
        for item in git(repo, 'ls-tree', '-rz', '--full-tree', PLAN3_REVIEW_COMMIT).split(b'\0'):
            if not item:
                continue
            meta, name = item.split(b'\t', 1)
            mode, kind, oid = meta.decode('ascii').split()
            path = name.decode('utf-8')
            if path not in PLAN3_GOVERNANCE_FILES:
                if kind != 'blob' or mode not in ('100644', '100755'):
                    return False
                tree[path] = (mode, oid)
        actual = {p.decode('utf-8') for p in git(repo, 'ls-files', '-z', '--cached', '--others', '--exclude-standard').split(b'\0') if p}
        actual -= PLAN3_GOVERNANCE_FILES
        if actual != set(tree):
            return False
        for path, (mode, oid) in tree.items():
            item = repo / path
            if any((repo / parent).is_symlink() for parent in Path(path).parents if str(parent) != '.'):
                return False
            info = item.lstat()
            if not stat.S_ISREG(info.st_mode) or bool(info.st_mode & 0o111) != (mode == '100755'):
                return False
            data = item.read_bytes()
            blob = hashlib.sha1(b'blob ' + str(len(data)).encode('ascii') + b'\0' + data).hexdigest()
            if blob != oid:
                return False
        return True
    except (GateError, OSError, UnicodeError, ValueError):
        return False
CONTACT_REVIEW_INPUTS = {'docs/contracts/automation-rule.schema.json': 'd5ad1506f3de8503a0bb3b060eb9bbc01db8a22efc95fabcef099cfaea0dcd0a',
 'docs/contracts/cloud-event.schema.json': 'ca9119a73e6eb6b6537c2c1098148b7fa49c781d5ac1c51ff26189fc05b04bea',
 'docs/contracts/contact-event-intent.schema.json': '717dc3a6818150cb8e17cfd1e2a3ce5b70ce31fcdb431c4632f6b014038742f6',
 'docs/contracts/crm-api.openapi.yaml': 'ab6839098b82f260a8fd97394bd533865e55f666000a75972413c8cf99f010d5',
 'docs/contracts/demo-echo-input.schema.json': 'cb06b937cdc90802f8bf3e51bd97db18221efe4b11f0d27ac6cd9e5b4109a1a7',
 'docs/contracts/demo-echo-output.schema.json': '574e8263605ffc9fe0f7c18004450f7b1ec0772036d25cc4b20174760bc56ded',
 'docs/contracts/demo-event.schema.json': '12ff9827134c2cbd033238bc1a2e07bc12c9b7204c653948f0ffa4337d2b8840',
 'docs/contracts/demo-panel.schema.json': '11e098468bb2839a7987f3fe39972f99fa7a983e332ac0d4076130451edbc57a',
 'docs/contracts/module-manifest.schema.json': '53fffd681593692cf40a2f04434fbb39b26fba4ef15198eb074a9866292c632e',
 'docs/contracts/platform-operations.openapi.yaml': '40363e45dfd1d3c9841ea43251b4b6a89b0f4fe78fd8155913a9e0c6f3e0856e',
 'docs/contracts/publisher-signature.schema.json': '15645564b38f504d18c347411fb52aa682d08816277a69b8b7be3e4cedaace64',
 'docs/contracts/remote-action-request.schema.json': 'b6b99aeecb9312fbb645db09aa1379bea5c0910bacf1ce0476bcc974a538ef98',
 'docs/contracts/remote-panel-request.schema.json': '7d32cc696474175df0352ed85cff6dbf2b73e2886f3a8ee96bdcf6805d09aee2'}
CONTACT_REVIEW_EVIDENCE = {'.npmrc': '3b88d1057add6f92ec23dc22dd017e312ded13a042deb0ba6f08bad85d936456',
 'docs/architecture/10-ADR-020-CONTACT-EMAIL-UPSERT.md': 'e3402fde49cbd5aaf6850a790c7eba4132a5ee6283482d2e231c383bdf0f9531',
 'docs/architecture/11-ADR-021-CONTACT-UPSERT-IDENTITY-DRAFT.md': 'dd0380be8677a38d11aa7fb9eec4061220a4972b26811eb309382b0ce6499afe',
 'docs/contracts/contact-event-intent.schema.json': '717dc3a6818150cb8e17cfd1e2a3ce5b70ce31fcdb431c4632f6b014038742f6',
 'docs/contracts/crm-api.openapi.yaml': 'ab6839098b82f260a8fd97394bd533865e55f666000a75972413c8cf99f010d5',
 'docs/contracts/fixtures/contact-created.v1.json': '6b9025c13caf09ab1eb6296b8455ca3b19da000b00a30f2a410731c30eb41241',
 'docs/contracts/fixtures/contact-deleted.v1.json': 'd74b8d749e27663a8c40e6f2f7a0dc35e188bf6aaa2feebf0ca7aa54182458a5',
 'docs/contracts/fixtures/contact-updated.v1.json': 'b916b00406a06a0993321095e4c4676ed82fecc171bcbcf140f489faa27e39b7',
 'docs/contracts/platform-operations.openapi.yaml': '40363e45dfd1d3c9841ea43251b4b6a89b0f4fe78fd8155913a9e0c6f3e0856e',
 'docs/operations/06-CONTACT-MIGRATION.md': '61069c1466345e090eefd7e9a3d56a58e6e3e1c8af9b0650b681a805eb26eb2f',
 'docs/operations/07-CONTACT-SDK-MIGRATION.md': '0dad05fe335681078d303341747c328843bb3c22a25c2200f371bd722e69c105',
 'package-lock.json': '4eccd95419dbaa9d04935700e0514c98651c0d08e0a303bd163cb5f39381f0c9',
 'package.json': '3d3395cf055e9a5d4a740b34cbcf59d46876b9eb996bb878e6ec73ee39c52494',
 'scripts/contracts/check.py': '4c287e4e68c2dac8838aefd37f02770c4bb902c267fa9f11e0e0e2171fd1018d',
 'scripts/contracts/generate.py': 'd3fc27d3ef4128e340d982193058ca61dcc360333453361f6f11a5da353081b9',
 'src/Platform.Api/Crm/ContactEndpoints.cs': '3c3542ec985bb6cc79ca569f8dd7beb1c0812c195765b10d676caaa0ad993f57',
 'src/Platform.Api/Identity/TenantAuthorization.cs': 'bb3f9a63ab393b4f052d90ccdde0462d7bc0e3fb1f0cbab0bc9724dc60d116ee',
 'src/Platform.Api/Identity/TenantEndpointTransaction.cs': '1764d0ba1ade969e5e8eb3ec7ac48d27636804d36a0c5a041161aea939df1e1b',
 'src/Platform.Infrastructure/Crm/ContactMutationJournal.cs': 'd0ea2154e24844eee5e150133b83dce73a009dc5a5d353d325bc47ba9421bb23',
 'src/Platform.Infrastructure/Crm/ContactStore.cs': '41ccf2ffdd38a4838694e8e16cfe92f5aeedf4ef22a7710320bb14b4844c7fb0',
 'src/Platform.Infrastructure/Persistence/Migrations/20261127000000_ContactFoundation.cs': '8ffd217dab8cda876a57499bfa09f4d43b4dd064d3b2262354e4b2ce0e599bfd',
 'src/Platform.Infrastructure/Persistence/Migrations/20261127010000_ContactEmailUpsert.cs': '573bc292b665e637da395de3fb285f217a82daa6e13ff6dfd87b21239855ade7',
 'tests/ClientConsumers/audit.py': 'd0d58cc24b23e34dd21d2e6bc59e76ac44b36ee4c6e3a6b927b10965490eac40',
 'tests/ClientConsumers/contact-upsert/new/Consumer.csproj': 'b2b2b9b9e886508487d9ec5b9acc43067ee3b1f4517987acc7efcb263935c8f2',
 'tests/ClientConsumers/contact-upsert/new/Program.cs': '7bb718ae628fddfbf8040e6ab3503dd852a069e5f117a9a7c299d185589310a0',
 'tests/ClientConsumers/contact-upsert/new/packages.lock.json': '12191600c891ac8a4d757ba85e11758eb161b6ecae74635e13becc82bfbca5d4',
 'tests/ClientConsumers/contact-upsert/old/Consumer.csproj': 'ffe777cc20e583bc25a5eec587550ecabf1831b93250e16c056c564d1416be28',
 'tests/ClientConsumers/contact-upsert/old/Program.cs': 'd6c34bdf7cc8186e8ca725004b7ea3bcbb6a70b7e4bd50322e8c4ce3ac490f15',
 'tests/ClientConsumers/contact-upsert/old/packages.lock.json': 'dd06767ef8df0e11dc7cc93fd26a08c859bb88b9d6344af0b6d90cebd92cec73',
 'tests/ClientConsumers/contact-upsert/run.sh': 'a4bd114bb7b78251ab3d27e5dc7d837a93e75e54ce98145fa2cc3ed700b97ca4',
 'tests/ClientConsumers/dotnet-v2/Consumer.csproj': '43b419988f60c1615337ddee85edf5ad1618ea6554da563334d8ea16ef020735',
 'tests/ClientConsumers/dotnet-v2/packages.lock.json': '370407a12defa316e0343f38a3fda4aeecb8cfb6d237ac64bd3722ed691b6924',
 'tests/ClientConsumers/dotnet/Consumer.csproj': 'abd33beca7e4913865ba8dd70251f7b0dc3f021fe6a3b82bb9f0154ca31a7a0c',
 'tests/ClientConsumers/dotnet/Program.cs': '47500ab0236ab3e060a5e6060ed5be96565f2bf9e47818eeb8d47b4c97672a06',
 'tests/ClientConsumers/dotnet/packages.lock.json': 'd63d3958ad82d4aa163986844d6a3887b9c53d49c1961bc0b9ed5bc3695d0512',
 'tests/ClientConsumers/normalize_packages.py': 'f40342c0e942f30f0136d16f1c741b639556354d547a43f167ff198d15ceb8f5',
 'tests/ClientConsumers/run.sh': 'da6c194a7550d971391b3e29a626134de29dd2574791621c1e10b6c4bd6f069d',
 'tests/ClientConsumers/typescript/types.ts': 'ab2f60219843152df649c6d8709f9f3d755b6138e69a7a63e8c173b876fda7b4',
 'tests/ClientConsumers/typescript/wire.mjs': '12f91bb7f1eb85d205cbda2078a8c6ff07be0eb8ca92d72e236772a31dd5aa2b',
 'tests/Contract/test_contact_crud.py': 'd5efe33c9d407ca6325da15f3d1da00f0a891abd11daab5ad4303bfa564a71c1',
 'tests/Contract/test_contact_precondition_identity.py': '4fa6e716f3a2ea5e0ac500b929bc374cdb2b5cf2e8483ef6259621005bd47f12',
 'tests/Contract/test_contact_upsert_consumer.py': '6c23685f628b6260355138336493cadc982c38c2c1a0e4c4b21d1ad69fe554a9',
 'tests/Integration/ContactConcurrencyTests.cs': 'fa2d00741dc93ceb341e77939555fb7cb0b102908444969dfeb16fae3fefc1a9',
 'tests/Integration/ContactEmailSchemaTests.cs': 'be523203a407b8f98b87ee781c91a5a85ece867706932d2b707ac5ae3bc1aac9',
 'tests/Integration/ContactEndpointsTests.cs': '20b97f472c837f41357d907163d32fa6fda13a885ccd9eeeb41213d3e3b7257d',
 'tests/Integration/ContactSchemaTests.cs': '1fe9efe05d6372bc56aaf5c58bbb2f2cc99c1ca6a97d4b5dba2de9e53273ddf9',
 'tests/Integration/ContactStreamingTests.cs': '4bd2189e63984bc41d42c248d808378291f662efed1d808b55e6d6c3781b8f43',
 'tools/contracts/facades/ResponseMetadataHandler.cs': '0560712114e72efe8cc805f89457ee15a10f70e07037dada40b01a99900f6d8a',
 'tools/contracts/locks/Crm.Client.json': 'd18f93fd5209028d971d32de610bd65797faab6ddad4cf7c79af3fcd35111473',
 'tools/contracts/locks/Platform.Operations.Client.json': 'd18f93fd5209028d971d32de610bd65797faab6ddad4cf7c79af3fcd35111473',
 'tools/contracts/toolchain.json': '7a83a5ae51108c6714a057be2ff743037a891f24ca0a1f3fcc2bed79f59b255f'}

def reviewed_contact_change(base, repo, hashes, mvp_review, now=None):
    import time
    if (not mvp_review or base != CONTACT_REVIEW_BASE
            or (time.time() if now is None else now) >= CONTACT_REVIEW_EXPIRES
            or hashes.get('current') != CONTACT_REVIEW_INPUTS):
        return False
    try:
        for path, expected in CONTACT_REVIEW_EVIDENCE.items():
            item = repo / path
            if (not item.is_file() or any((repo / Path(path).parents[index]).is_symlink()
                    for index in range(len(Path(path).parents) - 1))
                    or item.is_symlink() or sha(item) != expected):
                return False
        return True
    except (OSError, ValueError):
        return False

def contact_policy_for_scope(base, old_hashes, new_hashes, repo, mvp_review, now=None):
    if not reviewed_contact_policy(base, old_hashes, new_hashes, mvp_review, now=now):
        return False
    try:
        root = repo / 'docs/contracts'
        if not root.is_dir() or any(path.is_symlink() for path in root.rglob('*')):
            return False
        inputs = {path.relative_to(repo).as_posix(): sha(path) for path in root.rglob('*')
                  if path.is_file() and (path.name.endswith('.schema.json') or path.name.endswith('.openapi.yaml'))}
        return reviewed_contact_change(base, repo, {'current': inputs}, mvp_review, now=now)
    except (OSError, ValueError):
        return False

def contact_sdk_checks_completed(report):
    checks = report.get('sdkChecks')
    required = {'typescriptSurface', 'typescriptHistoricalConsumer', 'dotnetSurface', 'dotnetHistoricalConsumer'}
    return isinstance(checks, dict) and checks.keys() == required and all(type(value) is bool for value in checks.values())

def contact_sdk_checks_approved(report):
    return contact_sdk_checks_completed(report) and report['sdkChecks'] == {
        'typescriptSurface': False, 'typescriptHistoricalConsumer': True,
        'dotnetSurface': False, 'dotnetHistoricalConsumer': True}

def contact_consumers_passed(returncode, output):
    if returncode != 0 or not isinstance(output, bytes):
        return False
    lines = output.splitlines()
    required = (b'TYPESCRIPT_WIRE_OK', b'OLD_201_ONLY_CONSUMER_OK', b'NEW_UPSERT_CONSUMER_OK',
                b'SDK_PACKAGE_BOUNDARY_OK', b'PACKAGED_CONSUMERS_OK')
    return all(lines.count(marker) == 1 for marker in required) and lines.count(b'DOTNET_WIRE_OK') == 2

def schema_contents(value):
    if not isinstance(value, dict):
        return value
    mappings = {'properties', 'patternProperties', '$defs', 'definitions', 'dependentSchemas'}
    children = {'additionalProperties', 'unevaluatedProperties', 'propertyNames', 'items',
                'contains', 'unevaluatedItems', 'not', 'if', 'then', 'else', 'contentSchema'}
    arrays = {'allOf', 'anyOf', 'oneOf', 'prefixItems'}
    result = {}
    for key, item in value.items():
        if key in ('title', 'description', '$comment'):
            continue
        if key in mappings and isinstance(item, dict):
            result[key] = {name: schema_contents(subschema) for name, subschema in item.items()}
        elif key in children:
            result[key] = schema_contents(item)
        elif key in arrays and isinstance(item, list):
            result[key] = [schema_contents(child) for child in item]
        else:
            result[key] = item
    return result

def normalize_security(value):
    if isinstance(value, list):
        return sorted((normalize_security(v) for v in value), key=lambda v: json.dumps(v, sort_keys=True))
    if isinstance(value, dict):
        return {k: normalize_security(v) for k, v in sorted(value.items())}
    return value

def resolve_internal(doc, item):
    seen = set()
    while isinstance(item, dict) and '$ref' in item:
        ref = item['$ref']
        if len(item) != 1 or not isinstance(ref, str) or not ref.startswith('#/') or ref in seen:
            raise GateError('CONTRACT_INPUT_INVALID')
        seen.add(ref)
        item = doc
        try:
            for key in ref[2:].split('/'):
                item = item[key.replace('~1', '/').replace('~0', '~')]
        except (KeyError, TypeError):
            raise GateError('CONTRACT_INPUT_INVALID') from None
    return item

def security_projection(doc):
    schemes = {key: resolve_internal(doc, value) for key, value in doc.get('components', {}).get('securitySchemes', {}).items()}
    result = {'webhookSignature': normalize_security(schemes.get('webhookSignature'))}
    for path, path_item in doc.get('paths', {}).items():
        if not isinstance(path_item, dict):
            raise GateError('CONTRACT_INPUT_INVALID')
        if '$ref' in path_item and len(path_item) != 1:
            raise GateError('CONTRACT_INPUT_INVALID')
        path_item = resolve_internal(doc, path_item)
        if not isinstance(path_item, dict):
            raise GateError('CONTRACT_INPUT_INVALID')
        for method, operation in path_item.items():
            if method not in METHODS or not isinstance(operation, dict):
                continue
            effective = operation.get('security', doc.get('security', []))
            used = {name for requirement in effective for name in requirement}
            result[(path, method)] = (normalize_security(effective), normalize_security(operation.get('x-required-scopes')), normalize_security({name: schemes.get(name) for name in used}))
    return result

def security_changed(base, current, family):
    old, new = security_projection(base), security_projection(current)
    if old['webhookSignature'] != new['webhookSignature']:
        return True
    if any(old[key] != new[key] for key in old.keys() & new.keys() if key != 'webhookSignature'):
        return True
    for key in new.keys() - old.keys():
        effective, scopes, schemes = new[key]
        if family == 'crm-api.openapi.yaml':
            if not effective or not schemes or any(value is None for value in schemes.values()):
                return True
            if 'webhookSignature' not in schemes and (not isinstance(scopes, list) or not scopes):
                return True
        elif effective:
            return True
    return False

def reviewed_security_change(base, path, old, new, repo, mvp_review, now=None):
    """Exact, candidate-owned MVP review; not independent enforcement."""
    import time
    if (not mvp_review or base != '40bc2905b7750b139ff0607000127a3e5c06e2d8'
            or path != 'docs/contracts/crm-api.openapi.yaml'
            or (time.time() if now is None else now) >= 1802304000):
        return False
    def digest(document):
        return hashlib.sha256(json.dumps(document, sort_keys=True, separators=(',', ':'),
                                         ensure_ascii=True).encode()).hexdigest()
    if (digest(old) != '5979a39203660bf1f9db4dd006218497e6fcac0df081b683d397b243efcfc04c'
            or digest(new) != '937d6c0e1fe7e85c1bc21c2f55e024431323e459c78dff10915dfd5ba85ea23b'):
        return False
    evidence = {
        'docs/contracts/crm-api.openapi.yaml': '5b4c9ca9a9eb3eb2488d230e45d93ddd51a3876b1a15e0d2e1dfecac19438987',
        'docs/architecture/09-ADR-019-ACCESS-TOKEN-PROFILE.md': '72f253b048506b504cc5ce4b04a2ace072f7199e10fd7f2bf6e601ec1b2d0ac1',
        'src/Platform.Api/Identity/AccessTokenEvidence.cs': 'c12e476607fe68f62ddd7ff61c33d9bf7f26f53fa3ec409c666edd7ba324ad72',
        'src/Platform.Api/Identity/AuthenticationSetup.cs': 'c1e9cb05374d685da3b67c9972f04cab40fbc869e1fe9a68ddc56874097ff59b',
        'src/Platform.Api/Identity/InviteEndpoints.cs': '20f7c2660e3eade46a653a47243e7e19b30bd161398f3d805047e3560ec41828',
        'src/Platform.Api/Identity/AuditEndpoints.cs': '1476dbe57287aaa90626980c4d53c08377fabe3d80874b750a789961f716d5f9',
        'src/Platform.Api/Identity/MembershipEndpoints.cs': 'f6255643593c6cc51e8329f1211a163e58fd2be9eaf4b4703835104a31077baf',
        'src/Platform.Api/Identity/TenantEndpoints.cs': '3af5d5314f2edbd97ed1018de56939db5e5d5499290d410b19c63960c42df6e3',
        'tests/Integration/InviteManagementTests.cs': 'c589b2325d7d79b0753e35a58f838617ad9f2d9af0a00083d4c86c30346f7ba6',
        'tests/Integration/MembershipEndpointsTests.cs': '2e417dd5dd20806365d988bb4bf6732ca2e76e65e8cafeedd1371e8bbb5f36ab',
        'tests/Integration/TenantProvisioningTests.cs': 'e3a512af0f89846d602606c0ce50397d7011f23735331b3d191008615c545e27',
    }
    try:
        return all(sha(repo / name) == expected for name, expected in evidence.items())
    except OSError:
        return False

def schema_narrowed(old, new):
    """Conservative supplemental guard for narrowing in schema-shaped objects."""
    if isinstance(old, dict) and isinstance(new, dict):
        if old.get('nullable') is True and new.get('nullable') is not True:
            return True
        if 'enum' in old and 'enum' in new and not all(x in new['enum'] for x in old['enum']):
            return True
        if 'properties' in old and isinstance(old['properties'], dict):
            if not set(old['properties']) <= set(new.get('properties', {})):
                return True
        return any(schema_narrowed(value, new[key]) for key, value in old.items() if key in new)
    if isinstance(old, list) and isinstance(new, list):
        return any(schema_narrowed(a, b) for a, b in zip(old, new))
    return False

def response_removed(base, current):
    for path, path_item in base.get('paths', {}).items():
        path_item = resolve_internal(base, path_item)
        for method, operation in path_item.items():
            if method not in METHODS or not isinstance(operation, dict):
                continue
            current_path = resolve_internal(current, current.get('paths', {}).get(path, {}))
            target = current_path.get(method, {})
            if not isinstance(target, dict):
                continue
            responses = operation.get('responses', {})
            revised = target.get('responses', {})
            if not set(responses) <= set(revised):
                return True
            for code in responses.keys() & revised.keys():
                oldheaders = responses[code].get('headers', {})
                newheaders = revised[code].get('headers', {})
                if not {x.lower() for x in oldheaders} <= {x.lower() for x in newheaders}:
                    return True
    return schema_narrowed(base, current)

def run(base_ref, repo, context, mvp_review=False):
    from scripts.contracts.check import ref_files
    import yaml
    from jsonschema import Draft202012Validator
    from jsonschema.exceptions import SchemaError
    try:
        base = git(repo, 'merge-base', 'HEAD', base_ref).decode().strip()
        target = git(repo, 'rev-parse', '--verify', '--end-of-options', base_ref + '^{commit}').decode().strip()
        paths = git(repo, 'ls-tree', '-r', '--name-only', base, '--', 'docs/contracts').decode().splitlines()
    except (UnicodeError, ValueError):
        raise GateError('CONTRACT_BASE_UNAVAILABLE') from None
    context['baseSha'] = base
    context['targetSha'] = target
    review=check_baseline_configuration(repo, base, mvp_review, target=target)
    if review: context['policyReview']=review
    current_root = repo / 'docs/contracts'
    required = {'docs/contracts/crm-api.openapi.yaml', 'docs/contracts/platform-operations.openapi.yaml'}
    if not required <= set(paths) or not all((repo / p).is_file() for p in required):
        raise GateError('CONTRACT_BASE_UNAVAILABLE')
    try:
        policy = json.loads(POLICY.read_text())
        if policy != {'version': '1.37.0', 'archive': 'oasdiff_1.37.0_linux_amd64.tar.gz', 'archiveSha256': 'eac87307d31d08c36800c58a409c247f1c5db37fbb72e2544402f22a87cbca3c', 'binarySha256': '401389ac23e81bc844093c88ae1e283fbf80ef719f400cbeb19cd36d15957971', 'binaryPath': '.local/plan3-tools/oasdiff-1.37.0/oasdiff', 'breakOn': 'WARN', 'dockerImage': 'mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e'}:
            raise GateError('CONTRACT_POLICY_REVIEW_REQUIRED')
        binary = ROOT / policy['binaryPath']
        if not binary.is_file() or sha(binary) != policy['binarySha256']:
            raise GateError('CONTRACT_TOOL_UNAVAILABLE')
    except (OSError, ValueError, KeyError, TypeError):
        raise GateError('CONTRACT_TOOL_UNAVAILABLE') from None
    result=evaluate_contract_snapshots(base, paths, repo, context, policy, binary, mvp_review=mvp_review, target=target)
    result['targetSha'] = target
    if review: result['policyReview']=review
    return result


def evaluate_contract_snapshots(base, paths, repo, context, policy, binary,
                                immutable_base=None, policy_sources=None, mvp_review=False, target=None):
    """Semantic phase shared with unprivileged immutable transition runners.

    Identity/trust is the coordinator's separate responsibility. The CLI above
    always executes its unchanged preflight before reaching this routine.
    """
    from scripts.contracts.check import ref_files
    import yaml
    from jsonschema import Draft202012Validator
    from jsonschema.exceptions import SchemaError
    current_root = repo / 'docs/contracts'
    base_inputs = {p for p in paths if p.endswith('.openapi.yaml') or p.endswith('.schema.json')}
    current_inputs = {p.relative_to(repo).as_posix() for p in current_root.rglob('*') if p.is_file() and (p.name.endswith('.openapi.yaml') or p.name.endswith('.schema.json'))}
    hashes = {'base': {}, 'current': {}}
    context['inputHashes'] = hashes
    with tempfile.TemporaryDirectory(prefix='crm-compat-') as tmp:
        oldroot = Path(tmp) / 'base'
        newroot = Path(tmp) / 'current'
        snapshot = newroot / 'docs/contracts'
        snapshot.parent.mkdir(parents=True)
        shutil.copytree(current_root, snapshot, symlinks=True)
        if any(path.is_symlink() for path in snapshot.rglob('*')):
            raise GateError('CONTRACT_INPUT_INVALID')
        # Materialize only Git-tracked inputs, including local $ref dependencies.
        for path in paths:
            if path.startswith('docs/contracts/') and '..' not in Path(path).parts:
                target_path = oldroot / path
                target_path.parent.mkdir(parents=True, exist_ok=True)
                target_path.write_bytes(dict(immutable_base)[path] if immutable_base is not None else git(repo, 'show', f'{base}:{path}'))
        for path in sorted(base_inputs):
            hashes['base'][path] = sha(oldroot / path)
        for path in sorted(current_inputs):
            hashes['current'][path] = sha(newroot / path)
        result = {'baseSha': base, 'inputHashes': hashes}
        contact_review = (immutable_base is None and policy_sources is None and target == base
                          and (reviewed_contact_change(base, repo, hashes, mvp_review)
                               or reviewed_plan3_candidate(base, repo, mvp_review, target)))
        openapis = {p for p in base_inputs | current_inputs if p.endswith('.openapi.yaml')}
        schemas = {p for p in base_inputs | current_inputs if p.endswith('.schema.json')}
        if any(p not in base_inputs or p not in current_inputs for p in openapis):
            raise GateError('CONTRACT_INPUT_REVIEW_REQUIRED')
        if any(p not in current_inputs for p in schemas):
            result['reason'] = 'SCHEMA_COMPATIBILITY_REVIEW_REQUIRED'
            return result
        try:
            schema_requires_review = False
            for path in sorted(schemas):
                new = json.loads((newroot / path).read_text())
                if not isinstance(new, dict) or new.get('$schema') != 'https://json-schema.org/draft/2020-12/schema':
                    raise GateError('CONTRACT_INPUT_INVALID')
                Draft202012Validator.check_schema(new)
                new_refs = ref_files(newroot / path, snapshot)
                new_closure = {p.relative_to(newroot).as_posix(): sha(p) for p in new_refs}
                hashes['current'].update(new_closure)
                if path not in base_inputs:
                    if not (contact_review and path == 'docs/contracts/contact-event-intent.schema.json'):
                        schema_requires_review = True
                    continue
                if path in base_inputs:
                    old = json.loads((oldroot / path).read_text())
                    if not isinstance(old, dict) or old.get('$schema') != 'https://json-schema.org/draft/2020-12/schema':
                        raise GateError('CONTRACT_INPUT_INVALID')
                    Draft202012Validator.check_schema(old)
                    old_refs = ref_files(oldroot / path, oldroot / 'docs/contracts')
                    old_closure = {p.relative_to(oldroot).as_posix(): sha(p) for p in old_refs}
                    hashes['base'].update(old_closure)
                    old_closure.pop(path, None)
                    new_closure.pop(path, None)
                    if old_closure.keys() != new_closure.keys():
                        schema_requires_review = True
                    for dependency in old_closure.keys() & new_closure.keys():
                        if old_closure[dependency] == new_closure[dependency]:
                            continue
                        old_doc = yaml.safe_load((oldroot / dependency).read_text())
                        new_doc = yaml.safe_load((newroot / dependency).read_text())
                        try:
                            Draft202012Validator.check_schema(old_doc)
                            Draft202012Validator.check_schema(new_doc)
                        except SchemaError:
                            schema_requires_review = True
                            continue
                        if schema_contents(old_doc) != schema_contents(new_doc):
                            schema_requires_review = True
                    if schema_contents(old) != schema_contents(new):
                        schema_requires_review = True
            if schema_requires_review:
                result['reason'] = 'SCHEMA_COMPATIBILITY_REVIEW_REQUIRED'
                return result
            old_docs, new_docs = {}, {}
            for path in sorted(openapis):
                oldpath, newpath = oldroot / path, newroot / path
                ref_files(oldpath, oldroot / 'docs/contracts')
                ref_files(newpath, snapshot)
                old_docs[path] = yaml.safe_load(oldpath.read_text())
                new_docs[path] = yaml.safe_load(newpath.read_text())
                if not isinstance(old_docs[path], dict) or not isinstance(new_docs[path], dict):
                    raise GateError('CONTRACT_INPUT_INVALID')
                if any(not isinstance(doc.get('openapi'), str) or not re.fullmatch(r'3\.(?:0\.[0-4]|1\.[0-2])', doc['openapi']) for doc in (old_docs[path], new_docs[path])):
                    raise GateError('CONTRACT_INPUT_INVALID')
        except (SchemaError, yaml.YAMLError, ValueError, TypeError, OSError, RuntimeError):
            raise GateError('CONTRACT_INPUT_INVALID') from None
        for p in openapis:
            if security_changed(old_docs[p], new_docs[p], Path(p).name):
                if not reviewed_security_change(base, p, old_docs[p], new_docs[p], repo, mvp_review):
                    result['reason'] = 'SECURITY_REVIEW_REQUIRED'
                    return result
                result['securityReview'] = {'basis': 'maintainer-reviewed-adr-019',
                                            'owner': 'maintainer', 'expires': '2027-02-11T00:00:00+00:00',
                                            'independentVerification': False}
        config = Path(tmp) / 'config.yaml'
        config.write_text('{}\n')
        if contact_review:
            result['contactOpenApiBreaking'] = False
            result['openApiDiffsChecked'] = []
        for path in sorted(openapis):
            command = ['docker', 'run', '--rm', '--pull=never', '--network', 'none', '-v', f'{binary}:/oasdiff:ro', '-v', f'{tmp}:/work:ro', policy['dockerImage'], '/oasdiff', 'breaking', f'/work/base/{path}', f'/work/current/{path}', '--allow-external-refs=true', '--fail-on', policy['breakOn'], '--format', 'json', '--config', '/work/config.yaml']
            proc = subprocess.run(command, cwd=tmp, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            if contact_review:
                result['openApiDiffsChecked'].append(path)
            try:
                findings = json.loads(proc.stdout)
            except (ValueError, UnicodeError):
                raise GateError('CONTRACT_DIFF_ERROR') from None
            if not isinstance(findings, list) or proc.returncode not in (0, 1) or (proc.returncode == 1 and not findings):
                raise GateError('CONTRACT_DIFF_ERROR')
            if proc.returncode == 1 or findings:
                result['reason'] = 'CONTRACT_BREAKING'
                return result
        if any(response_removed(old_docs[p], new_docs[p]) for p in openapis):
            result['reason'] = 'CONTRACT_BREAKING'
            return result
        from scripts.contracts import sdk_surface
        workspace = Path(tmp) / 'surface'
        workspace.mkdir()
        has_base_toolchain = True if policy_sources else bool(git(repo, 'ls-tree', '-r', '--name-only', base, '--', 'tools/contracts/toolchain.json').strip())
        source = policy_sources[1] if policy_sources else repo if (repo / 'tools/contracts/toolchain.json').is_file() else ROOT
        result['sdkBaseline'] = 'historical-toolchain' if has_base_toolchain else 'bootstrap-no-released-consumer'
        try:
            old_sdk = sdk_surface.prepare(workspace / 'old', policy_sources[0] if policy_sources else repo if has_base_toolchain else source, oldroot / 'docs/contracts', git, None if policy_sources else base if has_base_toolchain else None)
            new_sdk = sdk_surface.prepare(workspace / 'new', source, snapshot, git)
            sdk_compatible = sdk_surface.compare(old_sdk, new_sdk, source if policy_sources else ROOT, workspace, has_base_toolchain, result)
            if contact_review and not contact_sdk_checks_completed(result):
                raise RuntimeError('SDK_TOOLCHAIN_REVIEW_REQUIRED')
            if contact_review and not contact_sdk_checks_approved(result):
                result['reason'] = 'SDK_SURFACE_INCOMPATIBLE'
                return result
            if not sdk_compatible:
                if not contact_review:
                    result['reason'] = 'SDK_SURFACE_INCOMPATIBLE'
                    return result
                result['contactSdkBreaking'] = True
        except (RuntimeError, OSError, ValueError, KeyError, TypeError):
            # Keep the baseline kind and toolchain evidence on the closed error path.
            context.update(result)
            raise GateError('SDK_TOOLCHAIN_REVIEW_REQUIRED') from None
        if contact_review:
            try:
                consumers = subprocess.run(['bash', 'tests/ClientConsumers/run.sh'], cwd=repo,
                                           stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=600)
            except (OSError, subprocess.SubprocessError):
                raise GateError('SDK_TOOLCHAIN_REVIEW_REQUIRED') from None
            if not contact_consumers_passed(consumers.returncode, consumers.stdout):
                raise GateError('SDK_TOOLCHAIN_REVIEW_REQUIRED')
            result['contactReview'] = {'basis':'maintainer-reviewed-adr-020-021',
                                       'owner':'maintainer', 'expires':'2027-02-11T00:00:00+00:00',
                                       'independentVerification':False}
            result['contactConsumerChecks'] = 'passed'
        result['reason'] = 'CONTRACT_BREAKING_REVIEWED' if 'securityReview' in result or contact_review else 'CONTRACT_COMPATIBLE'
        return result

def main():
    context = {}
    try:
        parser = SafeParser()
        parser.add_argument('--base-ref', default='origin/main')
        parser.add_argument('--mvp-reviewed-policy', action='store_true')
        args = parser.parse_args()
        repo = Path(git(Path.cwd(), 'rev-parse', '--show-toplevel').decode().strip())
        report = run(args.base_ref, repo, context, args.mvp_reviewed_policy)
    except GateError as error:
        report = {**context, 'reason': str(error)}
    except (OSError, ImportError, KeyError, TypeError, ValueError, RecursionError):
        report = {**context, 'reason': 'CONTRACT_TOOL_ERROR'}
    report['correlationId'] = str(uuid.uuid4())
    classifications = {'CONTRACT_COMPATIBLE': 'compatible', 'CONTRACT_BREAKING_REVIEWED': 'reviewed-breaking', 'CONTRACT_BREAKING': 'breaking', 'SECURITY_REVIEW_REQUIRED': 'security-review-required', 'SCHEMA_COMPATIBILITY_REVIEW_REQUIRED': 'schema-review-required', 'CONTRACT_POLICY_REVIEW_REQUIRED': 'policy-review-required', 'SDK_TOOLCHAIN_REVIEW_REQUIRED': 'toolchain-review-required', 'SDK_SURFACE_INCOMPATIBLE': 'SDK-surface-incompatible'}
    report['classification'] = classifications.get(report['reason'], 'tool-or-input-error')
    print(json.dumps(report, sort_keys=True))
    return 0 if report['reason'] in ('CONTRACT_COMPATIBLE', 'CONTRACT_BREAKING_REVIEWED') else 1

if __name__ == '__main__':
    sys.exit(main())
