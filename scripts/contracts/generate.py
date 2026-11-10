#!/usr/bin/env python3
"""Generate independent clients from the checked-in canonical OpenAPI documents."""
import sys
sys.dont_write_bytecode = True
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import uuid
import zipfile


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from scripts.contracts.check import ref_files

TOOLCHAIN = ROOT / 'tools/contracts/toolchain.json'
CONTRACTS = ROOT / 'docs/contracts'
CLIENTS = (
    ('crm-api', 'crm', 'Crm.Client'),
    ('platform-operations', 'operations', 'Platform.Operations.Client'),
)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(command, *, cwd=None, reason='SDK_GENERATION_UNSUPPORTED'):
    completed = subprocess.run(command, cwd=cwd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    if completed.returncode:
        raise RuntimeError(reason)


def generate(output_root):
    try:
        import yaml
    except ImportError:
        raise RuntimeError('SDK_TOOLCHAIN_UNAVAILABLE') from None
    config = json.loads(TOOLCHAIN.read_text())
    package = ROOT / '.local/plan3-tools/corvus.json.cli.5.8.8.nupkg'
    if not package.is_file() or digest(package) != config['corvusPackageSha256']:
        raise RuntimeError('SDK_TOOLCHAIN_UNAVAILABLE')
    for program, expected in (('node', 'v' + config['node']), ('npm', config['npm'])):
        actual = subprocess.run([program, '--version'], capture_output=True, text=True, check=False)
        if actual.returncode or actual.stdout.strip() != expected:
            raise RuntimeError('SDK_TOOLCHAIN_UNAVAILABLE')
    input_files = [CONTRACTS / f'{name}.openapi.yaml' for name, _, _ in CLIENTS]
    inputs = {path: ref_files(path, CONTRACTS) for path in input_files}
    config_hash = hashlib.sha256(b''.join(p.read_bytes() for p in (TOOLCHAIN, ROOT / 'package.json', ROOT / 'package-lock.json', ROOT / '.npmrc', Path(__file__), ROOT / 'scripts/contracts/check.py', *(ROOT / 'tools/contracts/locks' / f'{namespace}.json' for _, _, namespace in CLIENTS)))).hexdigest()
    with tempfile.TemporaryDirectory(prefix='crm-sdk-') as temporary:
        temp = Path(temporary)
        ts_tool = temp / 'ts-tool'
        ts_tool.mkdir()
        for filename in ('package.json', 'package-lock.json', '.npmrc'):
            shutil.copyfile(ROOT / filename, ts_tool / filename)
        run(['npm', 'ci', '--offline', '--ignore-scripts', '--no-audit', '--no-fund'], cwd=ts_tool, reason='SDK_TOOLCHAIN_UNAVAILABLE')
        executable = ts_tool / 'node_modules/.bin/openapi-typescript'
        tool = temp / 'tool'
        tool.mkdir()
        with zipfile.ZipFile(package) as archive:
            for member in archive.infolist():
                path = Path(member.filename)
                if path.is_absolute() or '..' in path.parts:
                    raise RuntimeError('SDK_TOOLCHAIN_UNAVAILABLE')
            archive.extractall(tool)
        stage = temp / 'out'
        stage.mkdir()
        os.chmod(stage, 0o777)
        for name, short, namespace in CLIENTS:
            ts = stage / 'sdks/typescript' / short
            ts.mkdir(parents=True)
            run([str(executable), str(CONTRACTS / f'{name}.openapi.yaml'), '--empty-objects-unknown', '-o', str(ts / 'schema.d.ts')])
            (ts / 'index.ts').write_text("import createClient from 'openapi-fetch';\nimport type { paths } from './schema.js';\nexport type { components, operations, paths } from './schema.js';\nexport function createApiClient(baseUrl: string) {\n  return createClient<paths>({ baseUrl, credentials: 'omit' });\n}\n")
            (ts / 'package.json').write_text(json.dumps({'name': f'@crm-centric/{short}-client', 'version': '0.1.0', 'type': 'module', 'main': 'dist/index.js', 'types': 'dist/index.d.ts', 'files': ['dist'], 'scripts': {'build': "tsc -p tsconfig.json && node -e \"require('node:fs').copyFileSync('schema.d.ts','dist/schema.d.ts')\""}, 'dependencies': {'openapi-fetch': config['openapiFetch']}}, indent=2) + '\n')
            (ts / 'tsconfig.json').write_text(json.dumps({'compilerOptions': {'target': 'ES2022', 'module': 'NodeNext', 'moduleResolution': 'NodeNext', 'strict': True, 'declaration': True, 'outDir': 'dist', 'skipLibCheck': False}, 'include': ['index.ts', 'schema.d.ts']}, indent=2) + '\n')
            cs = stage / 'sdks/dotnet' / namespace
            cs.parent.mkdir(parents=True, exist_ok=True)
            relative = cs.relative_to(stage).as_posix()
            run(['docker', 'run', '--rm', '--network', 'none', '--user', f'{os.getuid()}:{os.getgid()}', '-e', 'DOTNET_CLI_HOME=/tmp', '-v', f'{CONTRACTS}:/input:ro', '-v', f'{stage}:/output', '-v', f'{tool}:/tool:ro', config['dotnetImage'], 'dotnet', '/tool/tools/net10.0/any/Corvus.Json.Cli.dll', 'openapi-client', f'/input/{name}.openapi.yaml', '--rootNamespace', namespace, '--outputPath', f'/output/{relative}'])
            for generated_file in cs.rglob('*.cs'):
                generated_file.write_bytes(generated_file.read_bytes().replace(b'\r\n', b'\n'))
            lock = cs / 'corvusjson-openapi.lock'
            lock_data = json.loads(lock.read_text())
            lock_data.pop('generatedAt', None)
            lock.write_text(json.dumps(lock_data, indent=2, sort_keys=True) + '\n')
            (cs / f'{namespace}.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk">\n  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AllowUnsafeBlocks>true</AllowUnsafeBlocks><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup>\n  <ItemGroup><PackageReference Include="Corvus.Text.Json" Version="[5.8.8]" /><PackageReference Include="Corvus.Text.Json.OpenApi.HttpTransport" Version="[5.8.8]" /></ItemGroup>\n</Project>\n')
            shutil.copyfile(ROOT / 'tools/contracts/locks' / f'{namespace}.json', cs / 'packages.lock.json')
            contract = CONTRACTS / f'{name}.openapi.yaml'
            provenance = {'contractSha256': digest(contract), 'inputHashes': {p.relative_to(CONTRACTS.resolve()).as_posix(): digest(p) for p in inputs[contract]}, 'configurationSha256': config_hash, 'corvusPackageSha256': config['corvusPackageSha256'], 'openapiTypescript': config['openapiTypescript']}
            for directory in (ts, cs):
                (directory / 'provenance.json').write_text(json.dumps(provenance, sort_keys=True, indent=2) + '\n')
        destination = Path(output_root).resolve() / 'sdks'
        destination.parent.mkdir(parents=True, exist_ok=True)
        if destination.is_symlink():
            raise RuntimeError('SDK_OUTPUT_INVALID')
        for _, short, namespace in CLIENTS:
            for relative in (Path('typescript') / short, Path('dotnet') / namespace):
                source = stage / 'sdks' / relative
                target = destination / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                if target.parent.is_symlink() or target.is_symlink() or (target.exists() and not (target / 'provenance.json').is_file()):
                    raise RuntimeError('SDK_OUTPUT_INVALID')
                if target.exists():
                    shutil.rmtree(target)
                shutil.copytree(source, target)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output-root', type=Path, default=ROOT)
    args = parser.parse_args()
    try:
        generate(args.output_root)
    except (ImportError, KeyError, TypeError):
        print(json.dumps({'reason': 'SDK_TOOLCHAIN_UNAVAILABLE', 'correlationId': str(uuid.uuid4())}))
        return 1
    except (OSError, ValueError, RuntimeError, RecursionError, zipfile.BadZipFile) as error:
        reason = str(error) if type(error) is RuntimeError else 'SDK_GENERATION_ERROR'
        print(json.dumps({'reason': reason, 'correlationId': str(uuid.uuid4())}))
        return 1
    print(json.dumps({'reason': 'SDK_GENERATED', 'correlationId': str(uuid.uuid4())}))
    return 0


if __name__ == '__main__':
    sys.exit(main())
