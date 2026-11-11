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


def source_sbom(expected_packages,run,version):
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


def write_source_evidence(root,run,output):
    """Produce run-bound source inventory even when unsupported tool locks block."""
    from scripts.ci.scan import candidate_tree,dependency_inputs
    from scripts.ci.jobs import atomic_write
    import hashlib,json
    with candidate_tree(root,run) as tree, dependency_inputs(tree) as inputs:
        sbom=source_sbom(inputs.expected_packages,run,'1.0.0')
        record={'identity':asdict(run),'status':'incomplete' if inputs.gaps else 'inventory-only',
                'coverageGaps':list(inputs.gaps),'lockCount':len(inputs.expected_packages),'sbom':sbom,
                'sbomSha256':hashlib.sha256(json.dumps(sbom,sort_keys=True,separators=(',',':')).encode()).hexdigest()}
        atomic_write(output,record)
        return record


def main():
    import argparse,json,os,uuid
    from pathlib import Path
    from scripts.ci.summary import RunIdentity
    from scripts.ci.run_safe import SafeProcessError
    parser=argparse.ArgumentParser();parser.add_argument('--revision',type=Path,required=True);parser.add_argument('--output',type=Path,required=True)
    try:
        args=parser.parse_args();revision=json.loads(args.revision.read_text())
        run=RunIdentity(os.environ['GITHUB_RUN_ID'],os.environ['GITHUB_RUN_ATTEMPT'],revision['candidate_sha'],revision['base_sha'],revision['merge_sha'])
        record=write_source_evidence(Path.cwd(),run,args.output)
        token='CI_SOURCE_SBOM_INCOMPLETE' if record['coverageGaps'] else 'CI_SOURCE_SBOM_INVENTORIED'
        print(json.dumps({'reasonToken':token,'correlationId':str(uuid.uuid4())}))
        return 1 if record['coverageGaps'] else 0
    except (ScanError,SafeProcessError,OSError,ValueError,TypeError,KeyError):
        print(json.dumps({'reasonToken':'CI_SOURCE_SBOM_INCOMPLETE','correlationId':str(uuid.uuid4())}))
        return 1

if __name__=='__main__': raise SystemExit(main())
