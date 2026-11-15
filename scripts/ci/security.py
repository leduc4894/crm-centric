"""Real local security gate. Candidate code cannot establish independent trust."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import time
import signal
import uuid
from scripts.ci.jobs import atomic_write, execute, JobError
from scripts.ci.scan import ScannerConfig, run_repository, run_repository_mvp, ScanError
from scripts.ci.summary import RunIdentity
from scripts.ci.sbom import write_source_evidence
from scripts.ci.run_safe import SafeProcessError


def scanner_configs(root, tools, mode="hardening"):
    pins=json.loads((root/'tools/ci/toolchain.json').read_text())['scanners']
    configs={}
    for kind,pin in pins.items():
        if mode=="mvp" and kind=="semgrep": continue
        executable=tools/('semgrep/bin/semgrep' if kind=='semgrep' else 'bin/'+kind)
        # Launcher paths vary with the dedicated venv location. This measured
        # hash is provenance only; independent provisioning remains mandatory.
        digest=hashlib.sha256(executable.read_bytes()).hexdigest() if kind=='semgrep' else pin['executableSha256']
        configs[kind]=ScannerConfig(kind,executable,digest,pin['version'],
            root/('tools/ci/semgrep.yml' if kind=='semgrep' else 'tools/ci/gitleaks.toml'),tools/'trivy-cache')
    return configs


def main():
    class SafeParser(argparse.ArgumentParser):
        def error(self,message): raise ValueError()
    parser=SafeParser();parser.add_argument('--revision',type=Path,required=True);parser.add_argument('--output',type=Path,required=True)
    parser.add_argument("--mode",choices=["mvp","hardening"],default="hardening")
    try: args=parser.parse_args()
    except ValueError:
        print(json.dumps({'reasonToken':'CI_JOB_INPUT_INVALID','correlationId':str(uuid.uuid4())}))
        return 1
    def cancelled(signum,frame): raise JobError('CI_COMMAND_CANCELLED')
    signal.signal(signal.SIGTERM,cancelled);signal.signal(signal.SIGINT,cancelled)
    args.output.unlink(missing_ok=True)
    (Path.cwd()/'.local/ci-sbom/source.json').unlink(missing_ok=True)
    started=time.monotonic();correlation=str(uuid.uuid4());token='CI_SCAN_INCOMPLETE'
    report={'job':'security','command_id':'security','status':'failure','tools':{},'artifacts':[],
            'suites':[{'name':'security','tests':[{'id':'repository-security','outcome':'failed'}]}]}
    try:
        root=Path.cwd();revision=json.loads(args.revision.read_text())
        run=RunIdentity(os.environ['GITHUB_RUN_ID'],os.environ['GITHUB_RUN_ATTEMPT'],revision['candidate_sha'],revision['base_sha'],revision['merge_sha'])
        report['identity']=vars(run)
        if execute(['git','rev-parse','HEAD'])!=run.candidate_sha: raise JobError('CI_CANDIDATE_UNAVAILABLE')
        configs=scanner_configs(root,Path(os.environ.get('CI_SCANNER_ROOT',root/'.local/ci-scanners')).resolve(),mode=args.mode)
        report['tools']={kind:config.version for kind,config in configs.items()}
        if args.mode=='mvp':
            scanned,evidence=run_repository_mvp(configs,root,run)
            report['mvp_security']=evidence
        else: scanned=run_repository(configs,root,run)
        report['security_evidence']=scanned.to_dict()
        source=None
        if args.mode=='hardening':
            source=write_source_evidence(root,run,root/'.local/ci-sbom/source.json',configs['gitleaks'])
            report['source_evidence']=source
        if scanned.status=='pass' and (args.mode=='mvp' or source['status']=='inventory-only'):
            report['status']='success';report['suites'][0]['tests'][0]['outcome']='passed';token='CI_SCAN_OK'
        else: token=scanned.reason_token if scanned.status!='pass' else 'CI_SCAN_INCOMPLETE'
    except (JobError,ScanError,SafeProcessError,OSError,ValueError,TypeError,KeyError) as failure:
        token=failure.token if isinstance(failure,JobError) else 'CI_SCAN_INCOMPLETE'
    passed=int(report['status']=='success')
    report.update(passed=passed,failed=1-passed,total=1,skipped=0,reason_token=token,
                  correlation_id=correlation,elapsed_seconds=time.monotonic()-started)
    atomic_write(args.output,report)
    print(json.dumps({'reasonToken':token,'correlationId':correlation,'status':report['status']}))
    return 0 if passed else 1

if __name__=='__main__': raise SystemExit(main())
