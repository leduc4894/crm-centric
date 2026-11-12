"""Fail-closed scanner adapters. Never serialize scanner-controlled strings.

This module is candidate-owned until independent CI policy provisioning exists.
It does not authenticate policy approvals or claim complete security coverage.
"""
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import uuid

from scripts.ci.run_safe import private_command, SafeProcessError
from scripts.ci.summary import RunIdentity

class ScanError(Exception):
    def __init__(self):
        self.reason_token='CI_SCAN_INCOMPLETE'
        self.correlation_id=str(uuid.uuid4())
        super().__init__(self.reason_token)

@dataclass(frozen=True)
class ScannerConfig:
    kind: str
    executable: Path
    sha256: str
    version: str
    rules: Path | None = None
    cache: Path | None = None

@dataclass(frozen=True)
class ScanInput:
    root: Path
    expected_inventory: frozenset[str] = frozenset()
    expected_packages: dict = field(default_factory=dict)

@dataclass(frozen=True)
class ScanSummary:
    identity: RunIdentity
    status: str
    reason_token: str
    correlation_id: str
    findings: tuple = ()
    scanned_count: int = 0
    deferred_findings: tuple = ()
    def to_dict(self): return asdict(self)


def verdict(kind, severity, verification=None):
    if kind == 'secret': return 'blocked'
    if kind not in ('vulnerability','sast') or severity not in ('LOW','MEDIUM','HIGH','CRITICAL','INFO'):
        return 'incomplete'
    return 'blocked' if severity in ('HIGH','CRITICAL') else 'pass'


def license_verdict(licenses):
    allowed={'MIT','Apache-2.0','BSD-2-Clause','BSD-3-Clause','ISC','0BSD','CC0-1.0','Unlicense','PSF-2.0','Python-2.0'}
    return 'pass' if licenses and all(item in allowed for item in licenses) else 'review-required'


def require_fresh(metadata, now=None):
    try:
        now=now or datetime.now(timezone.utc)
        updated=datetime.fromisoformat(metadata['UpdatedAt'].replace('Z','+00:00'))
        age=(now-updated).total_seconds()
        if not 0 <= age <= 86400: raise ScanError()
    except (ValueError, TypeError, KeyError, AttributeError, OverflowError): raise ScanError() from None


def require_inventory(expected, actual):
    if not expected or set(expected) != set(actual): raise ScanError()


def exception_applies(finding_id, exceptions, trusted_approvals, now):
    # trusted_approvals MUST come from independent verifier; candidate JSON is insufficient.
    for item in exceptions:
        try:
            if (re.fullmatch('[0-9a-f]{64}', finding_id) and item['finding_id']==finding_id
                    and item['owner'].strip() and item['reason'].strip()
                    and re.fullmatch('[0-9a-f]{64}',item['approval'])
                    and set(item)=={'finding_id','owner','reason','expires','approval'}
                    and item['approval']==hashlib.sha256(json.dumps({k:v for k,v in item.items() if k!='approval'},sort_keys=True,separators=(',',':')).encode()).hexdigest()
                    and item['approval'] in trusted_approvals
                    and datetime.fromisoformat(item['expires'].replace('Z','+00:00')) > now):
                return True
        except (KeyError, TypeError, ValueError, AttributeError): pass
    return False


def _finding(kind, record, severity, licenses=None):
    # Stable opaque ID includes complete canonical record but exports no paths/messages.
    identifier=hashlib.sha256(json.dumps([kind,record],sort_keys=True,separators=(',',':')).encode()).hexdigest()
    return {'finding_id':identifier, 'kind':kind,
            'verdict':license_verdict(licenses) if kind=='license' else verdict(kind,severity)}


def parse_findings(kind, raw):
    try:
        findings=[]; inventory=set()
        if kind=='gitleaks':
            if not isinstance(raw,list): raise ScanError()
            for record in raw:
                if not isinstance(record,dict) or not {'RuleID','File','StartLine','Secret'}.issubset(record): raise ScanError()
                findings.append(_finding('secret',record,'HIGH'))
        elif kind=='semgrep':
            if raw['errors'] or not isinstance(raw['results'],list): raise ScanError()
            inventory=set(raw['paths']['scanned'])
            for record in raw['results']:
                severity={'ERROR':'HIGH','WARNING':'MEDIUM','INFO':'LOW'}.get(record['extra']['severity'],'UNKNOWN')
                findings.append(_finding('sast',record,severity))
        elif kind=='trivy':
            if raw['SchemaVersion'] != 2 or not isinstance(raw['Results'],list): raise ScanError()
            for result in raw['Results']:
                if result.get('Class') in ('license','license-file'):
                    for record in result.get('Licenses') or []:
                        findings.append(_finding('license',record,None,[record['Name']]))
                    continue
                if result.get('Class') != 'lang-pkgs': raise ScanError()
                if not isinstance(result['Packages'],list) or not result['Packages']: raise ScanError()
                inventory.add(result['Target'])
                for record in result.get('Vulnerabilities') or []:
                    findings.append(_finding('vulnerability',record,record['Severity']))
                for package in result['Packages']:
                    if not package.get('Name') or not package.get('Version'): raise ScanError()
                    findings.append(_finding('license',package,None,package.get('Licenses',[])))
        else: raise ScanError()
        return findings,inventory
    except (KeyError,ValueError,TypeError,AttributeError,RecursionError): raise ScanError() from None


def _json(path):
    def unique(pairs):
        result={}
        for key,value in pairs:
            if key in result: raise ScanError()
            result[key]=value
        return result
    try: return json.loads(path.read_text(), object_pairs_hook=unique)
    except (OSError,ValueError,TypeError,RecursionError): raise ScanError() from None


def _run_json(command, **kwargs):
    with private_command(command,**kwargs) as result: return _json(result.stdout)


def _verify_tool(scanner):
    if not re.fullmatch('[a-f0-9]{64}',scanner.sha256): raise ScanError()
    if hashlib.sha256(scanner.executable.read_bytes()).hexdigest() != scanner.sha256: raise ScanError()
    command=[str(scanner.executable),'--version']
    if scanner.kind=='gitleaks': command=[str(scanner.executable),'version']
    with private_command(command) as result:
        text=result.stdout.read_text().strip()
        if scanner.kind=='trivy':
            if not text.startswith('Version: '+scanner.version+'\n') and text != 'Version: '+scanner.version: raise ScanError()
        elif scanner.kind=='semgrep':
            if text != scanner.version: raise ScanError()
        elif text != scanner.version: raise ScanError()


def _git(root,*args):
    with private_command(['/usr/bin/git','-C',str(root),*args]) as result:
        return result.stdout.read_text().strip()


def _history(root,run):
    if not re.fullmatch('[a-f0-9]{40}',run.candidate_sha) or not re.fullmatch('[a-f0-9]{40}',run.base_sha): raise ScanError()
    if _git(root,'rev-parse','HEAD') != run.candidate_sha or _git(root,'rev-parse','--is-shallow-repository') != 'false': raise ScanError()
    _git(root,'merge-base','--is-ancestor',run.base_sha,run.candidate_sha)
    # Ask Git to traverse every reachable object in the range, not just commits.
    objects=_git(root,'rev-list','--objects','--missing=error',run.base_sha+'..'+run.candidate_sha)
    for row in objects.splitlines():
        object_id=row.split(' ',1)[0]
        if not re.fullmatch('[a-f0-9]{40}',object_id): raise ScanError()
        _git(root,'cat-file','-e',object_id)


def run_scanner(scanner: ScannerConfig, input: ScanInput, run: RunIdentity, mode="hardening") -> ScanSummary:
    correlation=str(uuid.uuid4())
    try:
        if mode not in ("mvp","hardening"): raise ScanError()
        _verify_tool(scanner)
        root=input.root.resolve()
        if scanner.kind=='gitleaks':
            _history(root,run)
            if scanner.rules is None: raise ScanError()
            common=['--config',str(scanner.rules.resolve()),'--no-banner','--redact=100','--ignore-gitleaks-allow','--gitleaks-ignore-path=/dev/null','--exit-code=0','--report-format=json','--report-path=/dev/stdout']
            findings=[]
            with candidate_tree(root,run) as tree:
                parsed,_=parse_findings('gitleaks',_run_json([str(scanner.executable),'dir',str(tree),*common]))
                findings.extend(parsed)
            parsed,_=parse_findings('gitleaks',_run_json([str(scanner.executable),'git',str(root),'--log-opts=--full-history --diff-merges=separate '+run.base_sha+'..'+run.candidate_sha,*common]))
            findings.extend(parsed)
            count=2
        elif scanner.kind=='semgrep':
            if scanner.rules is None: raise ScanError()
            command=[str(scanner.executable),'scan','--config',str(scanner.rules.resolve()),'--json','--metrics=off','--disable-version-check','--no-git-ignore','--disable-nosem','--include=*.cs','--include=*.ts','--include=*.tsx','--max-target-bytes=0','.']
            with source_inputs(root,input.expected_inventory) as sources:
                findings,inventory=parse_findings('semgrep',_run_json(command,cwd=sources))
            require_inventory(input.expected_inventory, inventory)
            count=len(inventory)
        elif scanner.kind=='trivy':
            if scanner.cache is None: raise ScanError()
            # Real update failure cannot be hidden by a previously populated cache.
            with private_command([str(scanner.executable),'fs','--cache-dir',str(scanner.cache.resolve()),'--download-db-only'],timeout=300): pass
            require_fresh(_json(scanner.cache/'db/metadata.json'))
            command=[str(scanner.executable),'fs','--cache-dir',str(scanner.cache.resolve()),'--skip-db-update','--scanners=vuln,license','--list-all-pkgs','--include-dev-deps','--format=json','--disable-telemetry','--license-full','.']
            raw=_run_json(command,cwd=root)
            findings,inventory=parse_findings('trivy',raw)
            require_inventory(input.expected_inventory,inventory)
            if input.expected_packages: require_package_inventory(raw,input.expected_packages)
            count=len(inventory)
        else: raise ScanError()
        deferred=tuple(f for f in findings if mode=='mvp' and f['kind']=='license')
        findings=[f for f in findings if f not in deferred]
        states={f['verdict'] for f in findings}
        status=('incomplete' if 'incomplete' in states else 'blocked' if states & {'blocked','review-required'} else 'pass')
        token={'incomplete':'CI_SCAN_INCOMPLETE','blocked':'CI_SCAN_FINDINGS','pass':'CI_SCAN_OK'}[status]
        return ScanSummary(run,status,token,correlation,tuple(findings),count,deferred)
    except (ScanError,SafeProcessError,OSError,ValueError,TypeError,KeyError,AttributeError,RecursionError):
        return ScanSummary(run,'incomplete','CI_SCAN_INCOMPLETE',correlation)


from contextlib import contextmanager
import os
import tarfile
import tempfile

@contextmanager
def candidate_tree(root, run):
    """Materialize exact committed blobs; untracked files never become scan input.

    Reject links, submodules, export-ignore loss, and unsafe tar members rather than
    following external content or silently losing committed paths.
    """
    _history(root,run)
    expected={}
    for entry in _git(root,'ls-tree','-rz','--full-tree',run.candidate_sha).split('\0'):
        if not entry: continue
        header,name=entry.split('\t',1)
        mode,kind,object_id=header.split(' ')
        if mode not in ('100644','100755') or kind!='blob' or not re.fullmatch('[A-Za-z0-9_.@/+-]+',name) or '..' in Path(name).parts or name.startswith('/'):
            raise ScanError()
        if not re.fullmatch('[a-f0-9]{40}',object_id): raise ScanError()
        expected[name]=object_id
    with tempfile.TemporaryDirectory(prefix='ci-tree-') as directory:
        target=Path(directory)
        with private_command(['/usr/bin/git','-C',str(root),'archive','--format=tar',run.candidate_sha]) as result:
            try:
                with tarfile.open(result.stdout) as archive:
                    files=set()
                    for member in archive.getmembers():
                        if member.isdir(): continue
                        if not member.isfile() or member.name not in expected or member.name in files or member.size>32*1024*1024:
                            raise ScanError()
                        content=archive.extractfile(member).read()
                        blob=hashlib.sha1(b'blob '+str(len(content)).encode()+b'\0'+content).hexdigest()
                        if blob!=expected[member.name]: raise ScanError()
                        files.add(member.name)
                    require_inventory(expected,files)
                    archive.extractall(target, filter='data')
            except (tarfile.TarError,OSError,ValueError): raise ScanError() from None
        yield target

@dataclass(frozen=True)
class DependencyInput:
    root: Path
    expected_inventory: frozenset[str]
    gaps: tuple[str,...]
    expected_packages: dict = field(default_factory=dict)

@contextmanager
def dependency_inputs(tree):
    """Stage all recognized locks, exposing unsupported tool locks as blockers.

    Rename complete pinned pip lockfiles to Trivy's supported requirements.txt
    convention. Do not resolve/install candidate packages or execute hooks.
    """
    import shutil
    with tempfile.TemporaryDirectory(prefix='ci-deps-') as directory:
        target=Path(directory);expected=set();gaps=dependency_gaps(tree);packages={}
        for source in tree.rglob('*'):
            if not source.is_file(): continue
            relative=source.relative_to(tree)
            name=source.name
            output=None
            if name in ('package-lock.json','packages.lock.json','requirements.txt'):
                output=relative
            elif 'requirements' in name and name.endswith('.lock'):
                output=Path('ci-normalized')/relative.parent/source.stem/'requirements.txt'
            elif name in ('dotnet-tools.json','toolchain.json'):
                gaps.add('CI_TOOL_DEPENDENCY_COVERAGE')
            elif (name.endswith(('.lock','.lockb','-lock.yaml','.lock.json')) or name=='npm-shrinkwrap.json') and name != 'corvusjson-openapi.lock':
                gaps.add('CI_ECOSYSTEM_COVERAGE')
            if output is not None:
                destination=target/output
                if destination.exists(): raise ScanError()
                destination.parent.mkdir(parents=True,exist_ok=True)
                shutil.copyfile(source,destination)
                expected.add(output.as_posix())
                packages[output.as_posix()]=lock_packages(source)
        if not expected: raise ScanError()
        yield DependencyInput(target,frozenset(expected),tuple(sorted(gaps)),packages)


def _package_name(name, target):
    # PyPI folds punctuation; npm/NuGet only fold case, retaining distinct IDs.
    if not isinstance(name,str) or not name: raise ScanError()
    return (re.sub('[-_.]+','-',name) if Path(target).name=='requirements.txt' else name).lower()


def lock_packages(source):
    try:
        packages=set()
        if source.name=='package-lock.json':
            raw=_json(source)
            if raw['lockfileVersion'] not in (2,3): raise ScanError()
            for path,package in raw['packages'].items():
                if not path or package.get('link'): continue
                name=package.get('name') or path.rsplit('node_modules/',1)[-1]
                if 'version' not in package: continue
                packages.add((_package_name(name,source.name),package['version']))
        elif source.name=='packages.lock.json':
            for framework in _json(source)['dependencies'].values():
                for name,package in framework.items():
                    if package.get('type')=='Project': continue
                    packages.add((_package_name(name,source.name),package['resolved']))
        else:
            for line in source.read_text().splitlines():
                line=line.strip()
                if not line or line.startswith('#'): continue
                match=re.fullmatch(r'([A-Za-z0-9_.-]+)==([^ ;]+)(?: --hash=sha256:[a-f0-9]{64})*',line)
                if not match: raise ScanError()
                packages.add((_package_name(match[1],'requirements.txt'),match[2]))
        if not packages: raise ScanError()
        return frozenset(packages)
    except (KeyError,TypeError,ValueError,OSError): raise ScanError() from None


def require_package_inventory(raw, expected):
    try:
        actual={}
        for result in raw['Results']:
            if result.get('Class')!='lang-pkgs': continue
            if result['Target'] in actual: raise ScanError()
            actual[result['Target']]={(_package_name(p['Name'],result['Target']),p['Version']) for p in result['Packages']}
        require_inventory(set(expected),set(actual))
        for target,packages in expected.items(): require_inventory(packages,actual[target])
    except (KeyError,TypeError,ValueError): raise ScanError() from None


def safe_pass_findings(rows):
    """Accept only normalized, non-secret pass records at every success boundary."""
    return isinstance(rows,(list,tuple)) and all(
        isinstance(row,dict) and set(row)=={'finding_id','kind','verdict'}
        and isinstance(row['finding_id'],str) and re.fullmatch('[a-f0-9]{64}',row['finding_id'])
        and row['kind'] in ('license','vulnerability','sast') and row['verdict']=='pass'
        for row in rows)


def combine_scans(reports, gaps, run):
    correlation=str(uuid.uuid4())
    if (gaps or set(reports)!={'semgrep','gitleaks','trivy'}
            or any(r.identity!=run or r.status not in ('pass','blocked') or r.scanned_count<=0
                   or (r.status=='pass' and not safe_pass_findings(r.findings))
                   for r in reports.values())):
        return ScanSummary(run,'incomplete','CI_SCAN_INCOMPLETE',correlation)
    findings=tuple(f for report in reports.values() for f in report.findings)
    blocked=any(r.status=='blocked' for r in reports.values())
    return ScanSummary(run,'blocked' if blocked else 'pass','CI_SCAN_FINDINGS' if blocked else 'CI_SCAN_OK',correlation,findings,sum(r.scanned_count for r in reports.values()))


def run_repository(scanners, root, run):
    """Use all scanner classes and propagate every discovered coverage gap.

    Passing this local adapter is NOT passing the independently trusted CI job.
    The workflow's security placeholder and trust blocker remain authoritative.
    """
    reports={};gaps=[]
    try:
        if set(scanners)!={'semgrep','gitleaks','trivy'}: raise ScanError()
        with candidate_tree(root,run) as tree:
            sources=frozenset(p.relative_to(tree).as_posix() for p in tree.rglob('*') if p.suffix in ('.cs','.ts','.tsx') and p.is_file())
            if not any(p.endswith('.cs') for p in sources) or not any(p.endswith(('.ts','.tsx')) for p in sources): raise ScanError()
            reports['semgrep']=run_scanner(scanners['semgrep'],ScanInput(tree,sources),run)
            with dependency_inputs(tree) as inputs:
                gaps.extend(inputs.gaps)
                reports['trivy']=run_scanner(scanners['trivy'],ScanInput(inputs.root,inputs.expected_inventory,inputs.expected_packages),run)
        reports['gitleaks']=run_scanner(scanners['gitleaks'],ScanInput(root),run)
        return combine_scans(reports,gaps,run)
    except (ScanError,SafeProcessError,OSError,ValueError,TypeError,KeyError,AttributeError,RecursionError):
        return ScanSummary(run,'incomplete','CI_SCAN_INCOMPLETE',str(uuid.uuid4()))


@contextmanager
def source_inputs(root, expected):
    """Neutralize candidate/default ignore config while retaining exact paths."""
    import shutil
    if not expected: raise ScanError()
    with tempfile.TemporaryDirectory(prefix='ci-source-') as directory:
        target=Path(directory)
        (target/'.semgrepignore').write_text('')
        for name in expected:
            path=Path(name)
            if path.is_absolute() or '..' in path.parts or path.suffix not in ('.cs','.ts','.tsx'): raise ScanError()
            source=root/path
            if not source.is_file() or source.is_symlink() or not source.resolve().is_relative_to(root.resolve()): raise ScanError()
            destination=target/path
            destination.parent.mkdir(parents=True,exist_ok=True)
            shutil.copyfile(source,destination)
        yield target



def dependency_gaps(tree):
    """Account for manifests even when an unrelated supported lock is present.

    Unsupported ecosystems close the gate rather than pretending their manifests
    were covered by another project's lock. npm workspace manifests require an
    exact corresponding package entry, with the same declared dependency sets.
    """
    gaps=set()
    unsupported={'pyproject.toml','setup.py','setup.cfg','Pipfile','go.mod','go.sum',
                 'Cargo.toml','composer.json','Gemfile','pom.xml','build.gradle',
                 'build.gradle.kts','packages.config','pnpm-workspace.yaml',
                 'deno.json','deno.jsonc','Package.swift','Package.resolved','pubspec.yaml'}
    for manifest in tree.rglob('*'):
        if not manifest.is_file(): continue
        if manifest.name in unsupported or (manifest.name.startswith('requirements') and manifest.suffix in ('.in','.txt') and manifest.name!='requirements.txt'):
            gaps.add('CI_ECOSYSTEM_COVERAGE')
        elif manifest.suffix in ('.csproj','.fsproj','.vbproj'):
            if not (manifest.parent/'packages.lock.json').is_file():
                gaps.add('CI_UNLOCKED_DEPENDENCIES')
        elif manifest.name=='package.json' and not node_manifest_locked(tree,manifest):
            gaps.add('CI_UNLOCKED_DEPENDENCIES')
    return gaps


def node_manifest_locked(tree, manifest):
    declared=_json(manifest)
    directory=manifest.parent
    while directory.is_relative_to(tree):
        lock=directory/'package-lock.json'
        if lock.is_file():
            raw=_json(lock)
            if raw.get('lockfileVersion') not in (2,3) or not isinstance(raw.get('packages'),dict): raise ScanError()
            relative=manifest.parent.relative_to(directory).as_posix()
            entry=raw['packages'].get('' if relative=='.' else relative)
            if entry is not None:
                if not isinstance(entry,dict): raise ScanError()
                for key in ('dependencies','devDependencies','optionalDependencies','peerDependencies'):
                    if declared.get(key,{}) != entry.get(key,{}): return False
                return all(key not in declared or declared[key]==entry.get(key) for key in ('name','version'))
        if directory==tree: break
        directory=directory.parent
    return False


def scan_image(scanner, identity):
    """Scan Docker config ID only, never a mutable tag or registry fallback.

    Return normalized evidence and an allowlisted CycloneDX inventory. All raw
    reports/inspect remain in private_command temporary storage and are deleted.
    """
    from dataclasses import asdict
    from scripts.ci.image_identity import validate, from_inspect
    from scripts.ci.sbom import normalize_sbom
    result={'identity':asdict(identity),'status':'incomplete','reason_token':'CI_IMAGE_SCAN_INCOMPLETE',
            'correlation_id':str(uuid.uuid4()),'packages':0,'sbomSha256':None}
    try:
        validate(identity)
        if scanner.kind!='trivy' or scanner.cache is None: raise ScanError()
        _verify_tool(scanner)
        inspected=_run_json(['docker','image','inspect',identity.imageId])
        # The CLI loads image_identity as __main__; compare the complete bound
        # fields across module instances rather than nominal dataclass types.
        if len(inspected)!=1 or asdict(from_inspect(identity.job,identity.runId,inspected[0]))!=asdict(identity): raise ScanError()
        with private_command([str(scanner.executable),'image','--cache-dir',str(scanner.cache.resolve()),'--download-db-only'],timeout=300): pass
        metadata=_json(scanner.cache/'db/metadata.json'); require_fresh(metadata)
        # Java archives in Keycloak need the independent Java index too.
        with private_command([str(scanner.executable),'image','--cache-dir',str(scanner.cache.resolve()),'--download-java-db-only'],timeout=300): pass
        java_metadata=_json(scanner.cache/'java-db/metadata.json'); require_fresh(java_metadata)
        common=[str(scanner.executable),'image','--image-src=docker','--cache-dir',str(scanner.cache.resolve()),'--skip-db-update','--skip-java-db-update','--offline-scan','--disable-telemetry']
        raw=_run_json(common+['--scanners=vuln','--list-all-pkgs','--format=json',identity.imageId],timeout=600)
        if raw['SchemaVersion']!=2 or raw['Metadata']['ImageID']!=identity.imageId or not raw['Results']: raise ScanError()
        findings=[]; packages=0
        for section in raw['Results']:
            if section.get('Class') not in ('os-pkgs','lang-pkgs'): raise ScanError()
            for package in section.get('Packages',[]):
                if not package.get('Name') or not package.get('Version'): raise ScanError()
                packages+=1
            for record in section.get('Vulnerabilities') or []:
                findings.append(_finding('vulnerability',record,record['Severity']))
        if packages<=0: raise ScanError()
        sbom=normalize_sbom(_run_json(common+['--format=cyclonedx',identity.imageId],timeout=600),identity,scanner.version)
        require_fresh(java_metadata)
        require_fresh(metadata) # long scans cannot silently age out their DB
        states={finding['verdict'] for finding in findings}
        status='incomplete' if 'incomplete' in states else 'blocked' if 'blocked' in states else 'pass'
        result.update(status=status,reason_token={'pass':'CI_IMAGE_SCAN_OK','blocked':'CI_IMAGE_FINDINGS','incomplete':'CI_IMAGE_SCAN_INCOMPLETE'}[status],packages=packages,
                      findings=findings,sbom=sbom,sbomSha256=hashlib.sha256(json.dumps(sbom,sort_keys=True,separators=(',',':')).encode()).hexdigest(),databaseUpdatedAt=metadata['UpdatedAt'],javaDatabaseUpdatedAt=java_metadata['UpdatedAt'])
    except (ScanError,SafeProcessError,OSError,KeyError,TypeError,ValueError,AttributeError): pass
    return result


def combine_mvp_scans(reports, gaps, run):
    """Only explicit tool inventory hardening is deferred; ecosystem gaps fail."""
    required={'gitleaks','trivy'}
    if (set(reports)!=required or set(gaps)-{'CI_TOOL_DEPENDENCY_COVERAGE'}
            or any(r.identity!=run or r.status not in ('pass','blocked') or r.scanned_count<=0
                   or (r.status=='pass' and not safe_pass_findings(r.findings))
                   or any(f['kind'] not in ('secret','vulnerability') for f in r.findings)
                   for r in reports.values())):
        return ScanSummary(run,'incomplete','CI_SCAN_INCOMPLETE',str(uuid.uuid4()))
    findings=tuple(f for report in reports.values() for f in report.findings)
    blocked=any(r.status=='blocked' for r in reports.values())
    return ScanSummary(run,'blocked' if blocked else 'pass',
                       'CI_SCAN_FINDINGS' if blocked else 'CI_SCAN_OK',str(uuid.uuid4()),
                       findings,sum(r.scanned_count for r in reports.values()))


def mvp_evidence(reports, gaps, run):
    return {'identity':asdict(run),'required':{kind:report.to_dict() for kind,report in reports.items()},
            'coverage_gaps':list(gaps),
            'deferred':{kind:{'status':'OPEN','findings':[
                f for report in reports.values() for f in report.deferred_findings if f['kind']==kind]}
                        for kind in ('sast','license','image','tool_inventory')}}


def run_repository_mvp(scanners, root, run):
    reports={};gaps=[]
    try:
        if set(scanners)!={'gitleaks','trivy'}: raise ScanError()
        # Scan both required classes independently, preserving findings even if
        # another class or package coverage is incomplete.
        reports['gitleaks']=run_scanner(scanners['gitleaks'],ScanInput(root),run,mode='mvp')
        with candidate_tree(root,run) as tree:
            with dependency_inputs(tree) as inputs:
                gaps.extend(inputs.gaps)
                reports['trivy']=run_scanner(scanners['trivy'],ScanInput(
                    inputs.root,inputs.expected_inventory,inputs.expected_packages),run,mode='mvp')
    except (ScanError,SafeProcessError,OSError,ValueError,TypeError,KeyError,AttributeError,RecursionError):
        gaps.append('CI_SCAN_INCOMPLETE')
    return combine_mvp_scans(reports,gaps,run),mvp_evidence(reports,gaps,run)


def validate_mvp_evidence(raw, run, require_pass=False):
    """Validate and reconstruct only safe enums/digests; no candidate waivers."""
    from scripts.ci.publication import findings
    if set(raw)!={'identity','required','coverage_gaps','deferred'} or raw['identity']!=asdict(run): raise ScanError()
    if set(raw['deferred'])!={'sast','license','image','tool_inventory'}: raise ScanError()
    reports={}
    for kind,row in raw['required'].items():
        if kind not in ('gitleaks','trivy') or row['identity']!=asdict(run): raise ScanError()
        if row['status'] not in ('pass','blocked','incomplete') or type(row['scanned_count']) is not int or row['scanned_count']<0: raise ScanError()
        if row['reason_token']!={'pass':'CI_SCAN_OK','blocked':'CI_SCAN_FINDINGS','incomplete':'CI_SCAN_INCOMPLETE'}[row['status']]: raise ScanError()
        if str(uuid.UUID(row['correlation_id']))!=row['correlation_id']: raise ScanError()
        safe=findings(row['findings']);deferred=findings(row.get('deferred_findings',[]))
        if any(f['kind']!='license' for f in deferred): raise ScanError()
        reports[kind]=ScanSummary(run,row['status'],row['reason_token'],row['correlation_id'],tuple(safe),row['scanned_count'],tuple(deferred))
    gaps=raw['coverage_gaps']
    if not isinstance(gaps,list) or any(g not in ('CI_TOOL_DEPENDENCY_COVERAGE','CI_UNLOCKED_DEPENDENCIES','CI_ECOSYSTEM_COVERAGE','CI_SCAN_INCOMPLETE') for g in gaps): raise ScanError()
    clean=mvp_evidence(reports,gaps,run)
    if raw['deferred']!=clean['deferred']: raise ScanError()
    if require_pass and combine_mvp_scans(reports,gaps,run).status!='pass': raise ScanError()
    return clean
