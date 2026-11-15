"""Allowlisted Docker identity evidence. Config IDs are not OCI manifest digests."""
from dataclasses import dataclass, asdict
import re
from scripts.ci.scan import ScanError, _run_json

@dataclass(frozen=True)
class ImageIdentity:
    job: str
    runId: str
    imageId: str
    platform: str
    manifestDigest: str | None


def validate(item):
    try:
        if (item.job not in ('backend','compose','contracts-sdk','web-browser','security')
                or not re.fullmatch('[0-9]+',item.runId)
                or not re.fullmatch('sha256:[a-f0-9]{64}',item.imageId)
                or not re.fullmatch('(linux|windows)/[a-z0-9_]+(?:/[a-z0-9_]+)?',item.platform)
                or (item.manifestDigest is not None and not re.fullmatch('sha256:[a-f0-9]{64}',item.manifestDigest))):
            raise ScanError()
        return item
    except (TypeError,AttributeError): raise ScanError() from None


def from_inspect(job, run, raw):
    try:
        # RepoDigests may refer to a multiarch index; do not assert a platform
        # manifest relationship without verified registry/OCI descriptor data.
        return validate(ImageIdentity(job,run,raw['Id'],raw['Os']+'/'+raw['Architecture']+('/'+raw['Variant'] if raw.get('Variant') else ''),None))
    except (KeyError, TypeError): raise ScanError() from None


def observe_container(job,run,container):
    if not re.fullmatch('[a-f0-9]{64}',container): raise ScanError()
    raw=_run_json(['docker','container','inspect',container])
    if len(raw)!=1 or raw[0]['Id']!=container or not raw[0]['State']['StartedAt'] or raw[0]['State']['StartedAt'].startswith('0001-'): raise ScanError()
    image=raw[0]['Image']
    if not re.fullmatch('sha256:[a-f0-9]{64}',image): raise ScanError()
    inspected=_run_json(['docker','image','inspect',image])
    if len(inspected)!=1 or inspected[0]['Id']!=image: raise ScanError()
    return from_inspect(job,run,inspected[0])


def require_evidence(expected,reports):
    try:
        if not expected or len(reports)!=len(expected) or len(set(expected))!=len(expected): raise ScanError()
        actual=[]
        for report in reports:
            item=validate(ImageIdentity(**report['identity']))
            if report['status']!='pass' or report['packages']<=0 or not re.fullmatch('[a-f0-9]{64}',report['sbomSha256']): raise ScanError()
            import hashlib,json
            from scripts.ci.sbom import normalize_sbom
            from scripts.ci.scan import require_fresh
            sbom=report['sbom']
            normalized=normalize_sbom(sbom,item,sbom['metadata']['tools']['components'][0]['version'])
            if normalized!=sbom: raise ScanError()
            digest=hashlib.sha256(json.dumps(sbom,sort_keys=True,separators=(',',':')).encode()).hexdigest()
            if digest!=report['sbomSha256']: raise ScanError()
            require_fresh({'UpdatedAt':report['databaseUpdatedAt']})
            require_fresh({'UpdatedAt':report['javaDatabaseUpdatedAt']})
            actual.append(item)
        if set(actual)!=set(expected) or len(set(actual))!=len(actual): raise ScanError()
    except (KeyError,TypeError,ValueError,IndexError,AttributeError): raise ScanError() from None


def verify_job_evidence(records,job,run):
    try:
        roles={r.get('role') for r in records}
        required={'runtime'} if job=='backend' else {'api','migrator','postgres','keycloak'}
        if not records or not required.issubset(roles) or not roles.issubset(required|{'runtime'}): raise ScanError()
        for record in records:
            expected=validate(ImageIdentity(**record['testedIdentity']))
            if expected.job!=job or expected.runId!=run: raise ScanError()
            require_evidence([expected],[record['scan']])
    except (KeyError,TypeError,ValueError,AttributeError): raise ScanError() from None


def _record_stack(containers,job,run,directory,scanner,secrets=()):
    """Observe started containers independently of mutable Compose image tags."""
    from pathlib import Path
    import hashlib
    import json
    from scripts.ci.scan import scan_image
    from scripts.ci.jobs import atomic_write
    from scripts.ci.sbom import require_no_secrets
    directory=Path(directory)
    directory.mkdir(mode=0o700,parents=True,exist_ok=True)
    roles={'crm-api':'api','crm-migrate':'migrator','crm-db':'postgres','keycloak':'keycloak'}
    for service,role in roles.items():
        identity=observe_container(job,run,containers[service]['Id'])
        name=hashlib.sha256(json.dumps([role,asdict(identity)],sort_keys=True).encode()).hexdigest()+'.json'
        output=directory/name
        if output.exists():
            saved=json.loads(output.read_text())
            if saved.get('testedIdentity')!=asdict(identity): raise ScanError()
            require_no_secrets(saved,secrets)
            require_evidence([identity],[saved['scan']])
            continue
        scan=scan_image(scanner,identity)
        record={'role':role,'testedIdentity':asdict(identity),'scan':scan}
        require_no_secrets(record,secrets)
        atomic_write(output,record)
        require_evidence([identity],[scan])


def _record_container(container,job,run,directory,scanner,secrets=()):
    from pathlib import Path
    import json,hashlib
    from scripts.ci.scan import scan_image
    from scripts.ci.jobs import atomic_write
    from scripts.ci.sbom import require_no_secrets
    identity=observe_container(job,run,container)
    directory=Path(directory);directory.mkdir(mode=0o700,parents=True,exist_ok=True)
    key=hashlib.sha256(json.dumps(asdict(identity),sort_keys=True).encode()).hexdigest()
    output=directory/(key+'.json')
    if output.exists():
        record=json.loads(output.read_text())
        if record['testedIdentity']!=asdict(identity): raise ScanError()
    else:
        record={'role':'runtime','testedIdentity':asdict(identity),'scan':scan_image(scanner,identity)}
    require_no_secrets(record,secrets)
    atomic_write(output,record)
    require_evidence([identity],[record['scan']])


from contextlib import contextmanager

@contextmanager
def scan_lock(directory):
    import fcntl,os
    from pathlib import Path
    directory=Path(directory);directory.mkdir(mode=0o700,parents=True,exist_ok=True)
    fd=os.open(directory/'scan.lock',os.O_CREAT|os.O_RDWR|os.O_NOFOLLOW,0o600)
    try:
        fcntl.flock(fd,fcntl.LOCK_EX)
        yield
    finally: os.close(fd)


def record_stack(containers,job,run,directory,scanner,secrets=()):
    with scan_lock(directory):
        return _record_stack(containers,job,run,directory,scanner,secrets)


def record_container(container,job,run,directory,scanner,secrets=()):
    with scan_lock(directory):
        return _record_container(container,job,run,directory,scanner,secrets)


def main():
    """Private JSON stdin from local lifecycle; only reason/correlation on stdout."""
    import json,os,sys,uuid
    import signal
    def cancelled(signum,frame): raise ScanError()
    signal.signal(signal.SIGTERM,cancelled)
    signal.signal(signal.SIGINT,cancelled)
    from pathlib import Path
    from scripts.ci.scan import ScannerConfig
    try:
        root=Path.cwd(); tools=Path(os.environ['CI_SCANNER_ROOT']).resolve()
        pin=json.loads((root/'tools/ci/toolchain.json').read_text())['scanners']['trivy']
        scanner=ScannerConfig('trivy',tools/'bin/trivy',pin['executableSha256'],pin['version'],cache=tools/'trivy-cache')
        payload=json.load(sys.stdin)
        # Lifecycle supplies only paths; values never enter argv/environment/report.
        secrets=[Path(path).read_text().strip() for path in payload.get('secretFiles',[])]+payload.get('secretValues',[])
        if any(not value for value in secrets): raise ScanError()
        if 'singleContainer' in payload:
            if os.environ['CI_IMAGE_JOB'] not in ('backend','compose','web-browser','contracts-sdk'): raise ScanError()
            record_container(payload['singleContainer'],os.environ['CI_IMAGE_JOB'],os.environ['GITHUB_RUN_ID'],os.environ['CI_IMAGE_EVIDENCE'],scanner,secrets)
        else:
            if not secrets: raise ScanError()
            record_stack(payload['containers'],os.environ['CI_IMAGE_JOB'],os.environ['GITHUB_RUN_ID'],os.environ['CI_IMAGE_EVIDENCE'],scanner,secrets)
        return 0
    except (ScanError,OSError,KeyError,TypeError,ValueError):
        print(json.dumps({'reasonToken':'CI_IMAGE_EVIDENCE_INCOMPLETE','correlationId':str(uuid.uuid4())}))
        return 1

if __name__=='__main__':
    raise SystemExit(main())
