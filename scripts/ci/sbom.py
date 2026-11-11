"""Small machine-readable inventory; scanner descriptions/properties never exported.

Raw SBOM stays private. Names/versions use a strict grammar; arbitrary matched
strings, paths, metadata, URLs and environment are never copied to artifacts.
"""
import re
from dataclasses import asdict
from scripts.ci.scan import ScanError


def normalize_sbom(raw, identity, version):
    from scripts.ci.image_identity import validate
    validate(identity)
    try:
        if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+',version): raise ScanError()
        if raw['bomFormat']!='CycloneDX' or not raw['components']: raise ScanError()
        components=[]
        for component in raw['components']:
            if component.get('type')=='application' and not component.get('version'): continue
            name=component['name']; release=component['version']
            if not re.fullmatch('[A-Za-z0-9@_.+:/~-]{1,256}',name) or not re.fullmatch('[A-Za-z0-9_.+:/~!()=-]{1,256}',release): raise ScanError()
            components.append({'type':'library','name':name,'version':release})
        if not components: raise ScanError()
        return {'bomFormat':'CycloneDX','specVersion':'1.6','version':1,
                'components':components,
                'metadata':{'tools':{'components':[{'type':'application','name':'trivy','version':version}]},
                            'properties':[{'name':'crm:image:'+key,'value':value} for key,value in asdict(identity).items() if value is not None]}}
    except (KeyError,TypeError,ValueError,AttributeError,RecursionError): raise ScanError() from None


def require_no_secrets(payload, secrets):
    """Check known test/runtime sentinels without returning matched values."""
    if isinstance(payload,dict):
        for key,value in payload.items():
            require_no_secrets(key,secrets); require_no_secrets(value,secrets)
    elif isinstance(payload,(list,tuple)):
        for value in payload: require_no_secrets(value,secrets)
    elif isinstance(payload,str):
        if any(secret and secret in payload for secret in secrets): raise ScanError()


def source_sbom(expected_packages,run,version,secrets=()):
    """Inventory exact lock package identities, explicitly retaining coverage gaps.

    This records inventory only: it does not claim vulnerability/license clearance.
    """
    if not expected_packages: raise ScanError()
    try:
        from pathlib import Path
        from urllib.parse import quote
        packages=set()
        for target,values in expected_packages.items():
            kind={'package-lock.json':'npm','packages.lock.json':'nuget','requirements.txt':'pypi'}.get(Path(target).name)
            if kind is None: raise ScanError()
            packages.update((kind,name,release) for name,release in values)
        packages=sorted(packages)
        require_no_secrets(packages,secrets)
        if not packages or any(not values for values in expected_packages.values()): raise ScanError()
        for kind,name,release in packages:
            if not re.fullmatch('[A-Za-z0-9@_.+:/~-]{1,256}',name) or not re.fullmatch('[A-Za-z0-9_.+:/~!()=-]{1,256}',release): raise ScanError()
        return {'bomFormat':'CycloneDX','specVersion':'1.6','version':1,
                'components':[{'type':'library','name':name,'version':release,
                               'purl':'pkg:'+kind+'/'+quote(name,safe='/')+'@'+quote(release,safe=''),
                               'bom-ref':'pkg:'+kind+'/'+quote(name,safe='/')+'@'+quote(release,safe='')} for kind,name,release in packages],
                'metadata':{'tools':{'components':[{'type':'application','name':'ci-lock-inventory','version':version}]},
                            'properties':[{'name':'crm:source:'+key,'value':str(value)} for key,value in asdict(run).items() if value is not None]}}
    except (KeyError,ValueError,TypeError,AttributeError): raise ScanError() from None


def write_source_evidence(root,run,output,secret_scanner=None,secrets=()):
    """Produce run-bound source inventory even when unsupported tool locks block."""
    from scripts.ci.scan import candidate_tree,dependency_inputs
    from pathlib import Path
    import hashlib,json,os
    output=Path(output)
    output.unlink(missing_ok=True) # always-upload cannot reuse stale preflight output
    if secret_scanner is None:
        from scripts.ci.scan import ScannerConfig
        pin=json.loads((Path(root)/'tools/ci/toolchain.json').read_text())['scanners']['gitleaks']
        tools=Path(os.environ.get('CI_SCANNER_ROOT',str(Path(root)/'.local/ci-scanners'))).resolve()
        secret_scanner=ScannerConfig('gitleaks',tools/'bin/gitleaks',pin['executableSha256'],pin['version'],rules=Path(root)/'tools/ci/gitleaks.toml')
    with candidate_tree(root,run) as tree, dependency_inputs(tree) as inputs:
        # Gitleaks defaults omit package-lock paths. Neutral private filenames
        # retain original bytes before package canonicalization/purl escaping.
        import tempfile
        with tempfile.TemporaryDirectory(prefix='ci-sbom-inputs-') as directory:
            for index,name in enumerate(sorted(inputs.expected_inventory)):
                original=inputs.root/name
                staged=Path(directory)/('inventory-'+str(index)+'.json')
                staged.write_bytes(original.read_bytes());staged.chmod(0o600)
            scan_secret_directory(directory,secret_scanner)
        sbom=source_sbom(inputs.expected_packages,run,'1.0.0',secrets)
        record={'identity':asdict(run),'status':'incomplete' if inputs.gaps else 'inventory-only',
                'coverageGaps':list(inputs.gaps),'lockCount':len(inputs.expected_packages),'sbom':sbom,
                'sbomSha256':hashlib.sha256(json.dumps(sbom,sort_keys=True,separators=(',',':')).encode()).hexdigest()}
        publish_source_record(record,output,secret_scanner,secrets)
        return record


def scan_secret_directory(directory,scanner):
    from scripts.ci.scan import _verify_tool,_run_json,parse_findings
    from scripts.ci.run_safe import SafeProcessError
    try:
        if scanner.kind!='gitleaks' or scanner.rules is None: raise ScanError()
        _verify_tool(scanner)
        raw=_run_json([str(scanner.executable),'dir',str(directory),'--config',str(scanner.rules.resolve()),
            '--no-banner','--redact=100','--ignore-gitleaks-allow',
            '--gitleaks-ignore-path=/dev/null','--exit-code=0',
            '--report-format=json','--report-path=/dev/stdout'])
        findings,_=parse_findings('gitleaks',raw)
        if findings: raise ScanError()
    except (SafeProcessError,OSError,ValueError,TypeError,KeyError):
        raise ScanError() from None


def publish_source_record(record,output,scanner,secrets=()):
    """No source inventory reaches always-upload until private secret scan succeeds.

    Findings, scanner absence, malformed output or stale artifacts all close this
    boundary. Known sentinels also cover fixture values beyond scanner patterns.
    """
    from pathlib import Path
    import json,tempfile
    from scripts.ci.jobs import atomic_write
    output=Path(output);output.unlink(missing_ok=True)
    require_no_secrets(record,secrets)
    with tempfile.TemporaryDirectory(prefix='ci-sbom-private-') as directory:
        candidate=Path(directory)/'inventory.json'
        candidate.write_text(json.dumps(record,sort_keys=True,separators=(',',':')))
        candidate.chmod(0o600)
        scan_secret_directory(directory,scanner)
    atomic_write(output,record)


def main():
    import argparse,json,os,uuid
    from pathlib import Path
    from scripts.ci.summary import RunIdentity
    from scripts.ci.run_safe import SafeProcessError
    parser=argparse.ArgumentParser();parser.add_argument('--revision',type=Path,required=True);parser.add_argument('--output',type=Path,required=True)
    try:
        args=parser.parse_args();args.output.unlink(missing_ok=True);revision=json.loads(args.revision.read_text())
        run=RunIdentity(os.environ['GITHUB_RUN_ID'],os.environ['GITHUB_RUN_ATTEMPT'],revision['candidate_sha'],revision['base_sha'],revision['merge_sha'])
        record=write_source_evidence(Path.cwd(),run,args.output)
        token='CI_SOURCE_SBOM_INCOMPLETE' if record['coverageGaps'] else 'CI_SOURCE_SBOM_INVENTORIED'
        print(json.dumps({'reasonToken':token,'correlationId':str(uuid.uuid4())}))
        return 1 if record['coverageGaps'] else 0
    except (ScanError,SafeProcessError,OSError,ValueError,TypeError,KeyError):
        print(json.dumps({'reasonToken':'CI_SOURCE_SBOM_INCOMPLETE','correlationId':str(uuid.uuid4())}))
        return 1

if __name__=='__main__': raise SystemExit(main())
