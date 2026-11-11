"""Provision the reviewed Trivy release; failed network/hash/version closes setup.

This candidate-owned downloader does not establish independent policy authority.
"""
import hashlib
import io
import json
from pathlib import Path
import tarfile
import tempfile
import os
import uuid
from scripts.ci.scan import ScanError,ScannerConfig,_verify_tool


def unpack_trivy(data,archive_sha,binary_sha,target):
    try:
        if hashlib.sha256(data).hexdigest()!=archive_sha: raise ScanError()
        with tarfile.open(fileobj=io.BytesIO(data),mode='r:gz') as archive:
            members=[m for m in archive.getmembers() if m.name=='trivy']
            if len(members)!=1 or not members[0].isfile() or members[0].size>256*1024*1024: raise ScanError()
            binary=archive.extractfile(members[0]).read()
        if hashlib.sha256(binary).hexdigest()!=binary_sha: raise ScanError()
        target=Path(target);target.parent.mkdir(parents=True,exist_ok=True,mode=0o700)
        with tempfile.NamedTemporaryFile(dir=target.parent,delete=False) as stream:
            temporary=Path(stream.name)
            try:
                stream.write(binary);stream.flush();os.fsync(stream.fileno());os.chmod(temporary,0o700)
                os.replace(temporary,target)
            finally: temporary.unlink(missing_ok=True)
    except (OSError,ValueError,TypeError,tarfile.TarError): raise ScanError() from None


def provision(root,target,archive=None):
    pin=json.loads((Path(root)/'tools/ci/toolchain.json').read_text())['scanners']['trivy']
    # URL and both hashes come from the checked-in reviewed release lock.
    if archive is None:
        from scripts.ci.run_safe import private_command
        with tempfile.TemporaryDirectory(prefix='ci-scanner-download-') as directory:
            payload=Path(directory)/'release.tar.gz'
            with private_command(['curl','--disable','--fail','--silent','--show-error','--location',
                    '--proto','=https','--proto-redir','=https','--max-time','120',
                    '--max-filesize',str(128*1024*1024),'--output',str(payload),pin['url']],timeout=125): pass
            data=payload.read_bytes()
    else:
        data=Path(archive).read_bytes()
    if len(data)>128*1024*1024: raise ScanError()
    executable=Path(target).resolve()/'bin/trivy'
    unpack_trivy(data,pin['archiveSha256'],pin['executableSha256'],executable)
    config=ScannerConfig('trivy',executable,pin['executableSha256'],pin['version'],cache=Path(target).resolve()/'trivy-cache')
    _verify_tool(config)
    return config


def main():
    import argparse
    from scripts.ci.run_safe import SafeProcessError
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,required=True);parser.add_argument('--archive',type=Path)
    try:
        args=parser.parse_args();provision(Path.cwd(),args.output,args.archive)
        print(json.dumps({'reasonToken':'CI_IMAGE_SCANNER_PROVISIONED','correlationId':str(uuid.uuid4())}))
        return 0
    except (ScanError,SafeProcessError,OSError,ValueError,TypeError,KeyError):
        print(json.dumps({'reasonToken':'CI_IMAGE_SCANNER_UNAVAILABLE','correlationId':str(uuid.uuid4())}))
        return 1

if __name__=='__main__': raise SystemExit(main())
