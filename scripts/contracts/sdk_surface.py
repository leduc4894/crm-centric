"""Build and compare SDK public surfaces from verified contract snapshots."""
import json
import os
from pathlib import Path
import shutil
import subprocess

from scripts.contracts.runtime_packages import runtime_packages


def run(command, cwd, timeout=300):
    try:
        return subprocess.run(command, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                              timeout=timeout, check=True).stdout
    except (OSError, subprocess.SubprocessError):
        raise RuntimeError('SDK_TOOLCHAIN_REVIEW_REQUIRED') from None


def prepare(root, source, contracts, git, base=None):
    root.mkdir(parents=True)
    prefixes = ('scripts/contracts/', 'tools/contracts/', 'tests/ClientConsumers/')
    names = {'package.json', 'package-lock.json', '.npmrc', '.node-version', 'global.json'}
    if base:
        paths = git(source, 'ls-tree', '-r', '--name-only', base).decode().splitlines()
        paths = [p for p in paths if p in names or p.startswith(prefixes)]
        for name in paths:
            target = root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(git(source, 'show', f'{base}:{name}'))
    else:
        for name in names:
            path = source / name
            if path.is_file():
                shutil.copyfile(path, root / name)
        for name in ('scripts/contracts', 'tools/contracts', 'tests/ClientConsumers'):
            if (source / name).is_dir():
                shutil.copytree(source / name, root / name, ignore=shutil.ignore_patterns('__pycache__', '*.pyc'))
    shutil.copytree(contracts, root / 'docs/contracts')
    return root


def scan_typescript(scanner, base, candidate, cwd):
    """Return True when the candidate surface stays compatible with the base surface."""
    try:
        proc = subprocess.run(['node', str(scanner), str(base), str(candidate)], cwd=cwd,
                              stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=300)
    except (OSError, subprocess.SubprocessError):
        raise RuntimeError('SDK_TOOLCHAIN_REVIEW_REQUIRED') from None
    try:
        report = json.loads(proc.stdout)
    except (ValueError, TypeError):
        raise RuntimeError('SDK_TOOLCHAIN_REVIEW_REQUIRED') from None
    if proc.returncode == 0:
        return bool(report.get('compatible'))
    if report.get('cause') == 'no-declarations':
        # The candidate declares no public surface at all: a genuine break, not a tool failure.
        return False
    raise RuntimeError('SDK_TOOLCHAIN_REVIEW_REQUIRED') from None


def compare(base_root, candidate_root, current_source, work, has_base_toolchain, report):
    package = current_source / '.local/plan3-tools/corvus.json.cli.5.8.8.nupkg'
    if not package.is_file():
        raise RuntimeError('SDK_TOOLCHAIN_REVIEW_REQUIRED')
    for snapshot in (base_root, candidate_root):
        destination = snapshot / '.local/plan3-tools'
        destination.mkdir(parents=True)
        shutil.copyfile(package, destination / package.name)
        run(['python3', str(snapshot / 'scripts/contracts/generate.py')], snapshot)
        run(['npm', 'ci', '--offline', '--ignore-scripts', '--no-audit', '--no-fund'], snapshot)
    report['sdkToolchain'] = {}
    for label, snapshot in (('base', base_root), ('current', candidate_root)):
        provenance = json.loads((snapshot / 'sdks/typescript/crm/provenance.json').read_text())
        report['sdkToolchain'][label] = {key: provenance[key] for key in ('configurationSha256', 'corvusPackageSha256', 'openapiTypescript')}
    report['sdkToolchainChanged'] = report['sdkToolchain']['base'] != report['sdkToolchain']['current']
    scanner = current_source / 'tools/contracts/surface/compare-typescript.cjs'
    for client in ('crm', 'operations'):
        for filename in ('schema.d.ts', 'index.ts'):
            old = base_root / 'sdks/typescript' / client / filename
            new = candidate_root / 'sdks/typescript' / client / filename
            if not scan_typescript(scanner, old, new, current_source):
                return False
    config = json.loads((candidate_root / 'tools/contracts/toolchain.json').read_text())
    base_config = json.loads((base_root / 'tools/contracts/toolchain.json').read_text())
    if has_base_toolchain:
        ts_fixture = base_root / 'tests/ClientConsumers/typescript/types.ts'
        cs_fixture = base_root / 'tests/ClientConsumers/dotnet/Program.cs'
        if not ts_fixture.is_file() or not cs_fixture.is_file():
            raise RuntimeError('SDK_TOOLCHAIN_REVIEW_REQUIRED')
        consumer = work / 'ts-consumer'
        feed = work / 'npm-feed'
        consumer.mkdir()
        feed.mkdir()
        (consumer / 'package.json').write_text('{"name":"baseline-consumer","version":"1.0.0","private":true,"type":"module"}\n')
        shutil.copyfile(ts_fixture, consumer / 'types.ts')
        for client in ('crm', 'operations'):
            package_root = candidate_root / 'sdks/typescript' / client
            run([str(candidate_root / 'node_modules/.bin/tsc'), '-p', 'tsconfig.json'], package_root)
            shutil.copyfile(package_root / 'schema.d.ts', package_root / 'dist/schema.d.ts')
            run(['npm', 'pack', '--ignore-scripts', '--pack-destination', str(feed), '--silent'], package_root)
        # The clean npm ci cache has locked tarballs, not guaranteed packuments.
        # Feed the independent consumer the complete exact runtime closure too.
        try: dependencies=runtime_packages(candidate_root)
        except ValueError: raise RuntimeError('SDK_TOOLCHAIN_REVIEW_REQUIRED') from None
        run(['npm','pack','--ignore-scripts','--pack-destination',str(feed),'--silent',
             *(str(path) for path in dependencies)],candidate_root)
        packages = sorted(str(p) for p in feed.glob('*.tgz'))
        if len(packages) != 2+len(dependencies):
            raise RuntimeError('SDK_TOOLCHAIN_REVIEW_REQUIRED')
        run(['npm', 'install', '--offline', '--ignore-scripts', '--no-audit', '--no-fund', *packages], consumer)
        compiler = candidate_root / 'node_modules/.bin/tsc'
        try:
            result = subprocess.run([str(compiler), '--noEmit', '--strict', '--module', 'NodeNext', '--moduleResolution', 'NodeNext', '--target', 'ES2022', 'types.ts'], cwd=consumer, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=300)
        except (OSError, subprocess.TimeoutExpired):
            raise RuntimeError('SDK_TOOLCHAIN_REVIEW_REQUIRED') from None
        if result.returncode:
            return False
        cs_consumer = work / 'cs-consumer'
        cs_consumer.mkdir()
        shutil.copyfile(cs_fixture, cs_consumer / 'Program.cs')
        (cs_consumer / 'Consumer.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><ProjectReference Include="../new/sdks/dotnet/Crm.Client/Crm.Client.csproj"/><ProjectReference Include="../new/sdks/dotnet/Platform.Operations.Client/Platform.Operations.Client.csproj"/></ItemGroup></Project>')
    image = config['dotnetImage']
    scanner_src = current_source / 'tools/contracts/surface/dotnet'
    shutil.copytree(scanner_src, work / 'scanner')
    # Build from temporary snapshots. The CLI suppresses tool output and never uploads contracts.
    def container(image_name, command):
        return run(['docker', 'run', '--rm', '--pull=never', '-u', f'{os.getuid()}:{os.getgid()}', '-e', 'HOME=/work', '-e', 'DOTNET_CLI_HOME=/work', '-e', 'DOTNET_CLI_TELEMETRY_OPTOUT=1', '-v', f'{work}:/work', '-w', '/work', image_name, 'sh', '-ec', command], work, timeout=600)
    container(base_config['dotnetImage'], '''set -eu
    for name in Crm.Client Platform.Operations.Client; do
      dotnet build "/work/old/sdks/dotnet/$name/$name.csproj" -p:RestoreLockedMode=true -p:CopyLocalLockFileAssemblies=true -o "/work/old/out/$name" >/dev/null
    done''')
    command = '''set -eu
    dotnet build /work/scanner/Surface.csproj -o /work/scanner/out >/dev/null
    for name in Crm.Client Platform.Operations.Client; do
      dotnet build "/work/new/sdks/dotnet/$name/$name.csproj" -p:RestoreLockedMode=true -p:CopyLocalLockFileAssemblies=true -o "/work/new/out/$name" >/dev/null
    done
    for name in Crm.Client Platform.Operations.Client; do
      dotnet /work/scanner/out/Surface.dll "/work/old/out/$name/$name.dll" "/work/new/out/$name/$name.dll"
    done
    if [ -f /work/cs-consumer/Consumer.csproj ]; then
      if dotnet build /work/cs-consumer/Consumer.csproj > /work/cs-consumer/build.log 2>&1; then echo '{"consumer":true}';
      elif grep -q 'error CS' /work/cs-consumer/build.log; then echo '{"consumer":false,"kind":"compile"}';
      else echo '{"consumer":false,"kind":"tool"}'; fi
    fi'''
    output = container(image, command)
    try:
        results = [json.loads(line) for line in output.decode().splitlines() if line.startswith('{')]
        if len(results) != (3 if has_base_toolchain else 2):
            raise ValueError()
    except (ValueError, KeyError, TypeError):
        raise RuntimeError('SDK_TOOLCHAIN_REVIEW_REQUIRED') from None
    if not all(item.get('compatible', False) for item in results[:2]):
        return False
    if has_base_toolchain:
        consumer = results[2]
        if consumer.get('consumer'):
            return True
        if consumer.get('kind') == 'compile':
            return False
        raise RuntimeError('SDK_TOOLCHAIN_REVIEW_REQUIRED') from None
    return True
