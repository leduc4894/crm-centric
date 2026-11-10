#!/usr/bin/env python3
"""Fail-closed, local wire, security and conservative schema compatibility gate."""
import sys
sys.dont_write_bytecode = True
import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path
import subprocess
import tempfile
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

METHODS = {'get', 'put', 'post', 'delete', 'options', 'head', 'patch', 'trace'}
POLICY = ROOT / 'tools/contracts/oasdiff.json'

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

def run(base_ref, repo, context):
    from scripts.contracts.check import ref_files
    import yaml
    from jsonschema import Draft202012Validator
    from jsonschema.exceptions import SchemaError
    try:
        base = git(repo, 'merge-base', 'HEAD', base_ref).decode().strip()
        paths = git(repo, 'ls-tree', '-r', '--name-only', base, '--', 'docs/contracts').decode().splitlines()
    except (UnicodeError, ValueError):
        raise GateError('CONTRACT_BASE_UNAVAILABLE') from None
    context['baseSha'] = base
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
                target = oldroot / path
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(git(repo, 'show', f'{base}:{path}'))
        for path in sorted(base_inputs):
            hashes['base'][path] = sha(oldroot / path)
        for path in sorted(current_inputs):
            hashes['current'][path] = sha(newroot / path)
        result = {'baseSha': base, 'inputHashes': hashes}
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
        if any(security_changed(old_docs[p], new_docs[p], Path(p).name) for p in openapis):
            result['reason'] = 'SECURITY_REVIEW_REQUIRED'
            return result
        config = Path(tmp) / 'config.yaml'
        config.write_text('{}\n')
        for path in sorted(openapis):
            command = ['docker', 'run', '--rm', '--pull=never', '--network', 'none', '-v', f'{binary}:/oasdiff:ro', '-v', f'{tmp}:/work:ro', policy['dockerImage'], '/oasdiff', 'breaking', f'/work/base/{path}', f'/work/current/{path}', '--allow-external-refs=true', '--fail-on', policy['breakOn'], '--format', 'json', '--config', '/work/config.yaml']
            proc = subprocess.run(command, cwd=tmp, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
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
        result['reason'] = 'CONTRACT_COMPATIBLE'
        return result

def main():
    context = {}
    try:
        parser = SafeParser()
        parser.add_argument('--base-ref', default='origin/main')
        args = parser.parse_args()
        repo = Path(git(Path.cwd(), 'rev-parse', '--show-toplevel').decode().strip())
        report = run(args.base_ref, repo, context)
    except GateError as error:
        report = {**context, 'reason': str(error)}
    except (OSError, ImportError, KeyError, TypeError, ValueError, RecursionError):
        report = {**context, 'reason': 'CONTRACT_TOOL_ERROR'}
    report['correlationId'] = str(uuid.uuid4())
    classifications = {'CONTRACT_COMPATIBLE': 'compatible', 'CONTRACT_BREAKING': 'breaking', 'SECURITY_REVIEW_REQUIRED': 'security-review-required', 'SCHEMA_COMPATIBILITY_REVIEW_REQUIRED': 'schema-review-required', 'CONTRACT_POLICY_REVIEW_REQUIRED': 'policy-review-required'}
    report['classification'] = classifications.get(report['reason'], 'tool-or-input-error')
    print(json.dumps(report, sort_keys=True))
    return 0 if report['reason'] == 'CONTRACT_COMPATIBLE' else 1

if __name__ == '__main__':
    sys.exit(main())
