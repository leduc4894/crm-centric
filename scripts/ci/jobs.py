"""Isolated locked job runner. Raw subprocess diagnostics never leave this process."""
import argparse
from contextlib import contextmanager
import json
import os
from pathlib import Path
import platform
import signal
import subprocess
import sys
import tempfile
import time
import uuid

from scripts.ci.summary import ReportError, RunIdentity, parse_report

VERSIONS = {'node':'24.22.2','npm':'11.19.1','python':'3.15.2','dotnet':'10.0.403','docker':'29.9.3','compose':'5.7.0'}

def verify_versions(actual):
    if actual != VERSIONS:
        raise ValueError('CI_TOOLCHAIN_MISMATCH')

class JobError(Exception):
    def __init__(self, token): self.token = token

def stop_process_group(process, grace=1):
    """Signal only the session created for this exact command, including descendants."""
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        return
    time.sleep(grace)
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    process.wait()


def execute(command, timeout=3300, env=None):
    try:
        process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   start_new_session=True, env=env)
    except OSError:
        raise JobError('CI_PREREQUISITE_MISSING') from None
    try:
        stdout, _ = process.communicate(timeout=timeout)
    except subprocess.TimeoutExpired:
        stop_process_group(process)
        process.communicate()
        raise JobError('CI_COMMAND_TIMEOUT') from None
    except BaseException:
        stop_process_group(process)
        process.communicate()
        raise
    if process.returncode:
        # Fixed command families only: never echo argv, paths or child output.
        stages={('dotnet','restore'):'dotnet-restore',('dotnet','build'):'dotnet-build',
                ('dotnet','test'):'dotnet-test',('npm','ci'):'npm-install',
                ('docker','pull'):'image-pull',('bash','scripts/check-plan3.sh'):'plan3',
                (sys.executable,'-m'):'python-suite',('node','-e'):'browser-launch'}
        stage=stages.get(tuple(command[:2]),'other')
        print(json.dumps({'reasonToken':'CI_COMMAND_FAILED','commandStage':stage}))
        raise JobError('CI_COMMAND_FAILED')
    return stdout.decode().strip()


def native_suite(command, suites, name, format, output, env=None):
    try:
        execute(command, env=env)
    finally:
        if output.is_file():
            payload = output.read_text()
            suites.append({'name':name,'format':format,
                           'report':payload if format == 'trx' else json.loads(payload)})


def cleanup_resources(owner):
    """Use fresh label inspection; never infer ownership from resource names."""
    if len(owner) != 32 or any(char not in '0123456789abcdef' for char in owner):
        raise JobError('CI_RESOURCE_OWNERSHIP_INVALID')
    for kind in ('container','network','volume'):
        listing = ['docker',kind,'ls','-q']
        if kind == 'container':listing.append('-a')
        ids = execute(listing+['--filter','label=io.crm-centric.ci-owner='+owner], timeout=30).splitlines()
        for ident in ids:
            objects = json.loads(execute(['docker',kind,'inspect',ident],timeout=30))
            if len(objects) != 1:
                raise JobError('CI_RESOURCE_OWNERSHIP_INVALID')
            resource = objects[0]
            labels = resource.get('Config',{}).get('Labels',{}) if kind == 'container' else resource.get('Labels',{})
            if not labels or labels.get('io.crm-centric.ci-owner') != owner:
                continue
            command = ['docker',kind,'rm']
            if kind == 'container':command.append('-f')
            execute(command+[ident], timeout=60)


@contextmanager
def job_resources():
    # Each invocation has a fresh owner, even when run/attempt identities repeat.
    previous = os.environ.get('CI_RESOURCE_OWNER')
    owner = uuid.uuid4().hex
    os.environ['CI_RESOURCE_OWNER'] = owner
    try:
        yield
    finally:
        try:
            cleanup_resources(owner)
        finally:
            if previous is None:os.environ.pop('CI_RESOURCE_OWNER',None)
            else:os.environ['CI_RESOURCE_OWNER'] = previous


@contextmanager
def execution_mode(mode, job, evidence):
    """MVP executes real fixtures without invoking deferred scanner hooks."""
    if mode not in ('mvp','hardening'):
        raise JobError('CI_JOB_INPUT_INVALID')
    keys=('CI_IMAGE_JOB','CI_IMAGE_EVIDENCE')
    previous={key:os.environ.get(key) for key in keys}
    try:
        for key in keys:os.environ.pop(key,None)
        if mode=='hardening' and job in ('backend','compose','web-browser'):
            os.environ.update(CI_IMAGE_JOB=job,CI_IMAGE_EVIDENCE=str(evidence))
        yield
    finally:
        for key,value in previous.items():
            if value is None:os.environ.pop(key,None)
            else:os.environ[key]=value


def tool_versions():
    return {'node':execute(['node','--version']).lstrip('v'),
            'npm':execute(['npm','--version']), 'python':platform.python_version(),
            'dotnet':execute(['dotnet','--version']),
            'docker':execute(['docker','version','--format','{{.Server.Version}}']),
            'compose':execute(['docker','compose','version','--short'])}

def prepare_browser():
    execute(['npm','exec','--workspace','apps/crm-web','--','playwright','install','chromium'])
    # A real launch verifies native OS libraries and the locked browser binary.
    execute(['node','-e', "(async () => { const browser = await require('@playwright/test').chromium.launch(); await browser.close(); })().catch(() => process.exit(1))"],timeout=60)


def plan3_commands(identity, mode="hardening"):
    return [
        ['bash','tests/Plan3/aggregate-reporting.sh','--base-ref',identity.base_sha],
        ['bash','tests/Plan3/ci-environment.sh'],
        ['bash','tests/Plan3/clean-export.sh','--sha',identity.candidate_sha,
         '--base-ref',identity.base_sha,'--with-browser']+(['--mvp-reviewed-policy'] if mode=='mvp' else []),
        ['bash','tests/Plan3/negative-cases.sh','--base-ref',identity.base_sha],
        ['bash','tests/Plan3/scratch-cleanup.sh'],
    ]


def atomic_write(output, report):
    """Only sanitized reports may reach the exact artifact path, atomically."""
    output.parent.mkdir(parents=True,exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode='w', dir=output.parent,
                prefix='.'+output.stem+'-', suffix='.tmp', delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(report,stream)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary,output)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def publish_report(report, output, identity, mode="hardening"):
    from scripts.ci.summary import parse_cases
    # The validation input contains native diagnostic payloads. Keep it private
    # and outside the always-uploaded artifact tree even on cancellation.
    with tempfile.TemporaryDirectory(prefix='ci-private-validation-') as directory:
        private = Path(directory)/'native.json'
        private.write_text(json.dumps(report))
        parsed = parse_report('normalized',private,identity,mode=mode)
    for suite in report['suites']:
        if 'format' in suite:
            name = suite['name']
            cases = parse_cases(suite['format'],suite['report'])
            suite.clear(); suite.update(name=name, tests=cases)
    report.update(passed=parsed.passed,failed=parsed.failed,skipped=parsed.skipped,total=parsed.total)
    atomic_write(output,report)
    return parsed


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('job', choices=['backend','compose','contracts-sdk','web-browser'])
    parser.add_argument('--mode', choices=['mvp','hardening'], default='hardening')
    parser.add_argument('--revision', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    def cancelled(signum, frame):
        raise JobError('CI_COMMAND_CANCELLED')
    signal.signal(signal.SIGTERM, cancelled)
    signal.signal(signal.SIGINT, cancelled)
    started = time.monotonic()
    correlation = str(uuid.uuid4())
    report = {'job':args.job,'status':'failure','command_id':args.job,
              'suites':[],'tools':{},'artifacts':[], 'correlation_id':correlation}
    token = 'CI_JOB_FAILED'
    try:
        revision = json.loads(args.revision.read_text())
        identity = RunIdentity(os.environ['GITHUB_RUN_ID'], os.environ['GITHUB_RUN_ATTEMPT'],
                               revision['candidate_sha'], revision['base_sha'], revision['merge_sha'])
        report['identity'] = vars(identity)
        if execute(['git','rev-parse','HEAD']) != identity.candidate_sha:
            raise JobError('CI_CANDIDATE_UNAVAILABLE')
        versions = tool_versions()
        try:
            verify_versions(versions)
        except ValueError:
            raise JobError('CI_TOOLCHAIN_MISMATCH') from None
        report['tools'] = {**versions, 'runner_image':os.environ.get('ImageOS','local'),
                           'runner_version':os.environ.get('ImageVersion','local')}
        if args.mode=='mvp' and args.job in ('backend','compose','web-browser'):
            report['image_hardening']={'status':'not-evaluated'}
        with tempfile.TemporaryDirectory(prefix='ci-job-') as directory, job_resources(), execution_mode(args.mode,args.job,Path(directory)/'image-evidence'):
            scratch = Path(directory)
            def py_suite(name, directory):
                output = scratch / (name+'.json')
                try:
                    execute([sys.executable,'-m','scripts.ci.unittest_report',directory,str(output)])
                finally:
                    if output.is_file():
                        report['suites'].append({'name':name,'tests':json.loads(output.read_text())})
            if args.job in ('backend','compose'):
                execute(['dotnet','restore','CrmPlatform.sln','--locked-mode'])
                execute(['dotnet','build','CrmPlatform.sln','-c','Release','--no-restore'])
                category = 'Category=Compose' if args.job == 'compose' else 'Category!=Compose'
                native_suite(['dotnet','test','tests/Integration/Integration.csproj','-c','Release',
                         '--no-build','--no-restore','--filter',category,
                         '--logger','trx;LogFileName=results.trx','--results-directory',str(scratch)],
                             report['suites'],'compose' if args.job == 'compose' else 'non-compose',
                             'trx',scratch/'results.trx')
                if args.job == 'backend': py_suite('local','tests/Local')
            else:
                execute(['npm','ci','--ignore-scripts','--no-audit','--no-fund'])
                image = json.loads(Path('tools/contracts/toolchain.json').read_text())['dotnetImage']
                execute(['docker','pull',image])
                prepare_browser()
                if args.job == 'contracts-sdk':
                    py_suite('contract','tests/Contract')
                    execute([sys.executable,'scripts/contracts/check.py'])
                    execute(['bash','tests/ClientConsumers/run.sh'])
                    execute([sys.executable,'scripts/contracts/compatibility.py','--base-ref',identity.base_sha]+(['--mvp-reviewed-policy'] if args.mode=='mvp' else []))
                    for command in plan3_commands(identity,args.mode):
                        execute(command)
                else:
                    vitest = scratch/'vitest.json'; browser = scratch/'browser.json'
                    env = {**os.environ,'PLAYWRIGHT_JSON_OUTPUT_NAME':str(browser),
                           'PLAYWRIGHT_HTML_OPEN':'never'}
                    # Plan 3 owns real API stack startup, readiness and teardown.
                    # Reporter args are supplied through the workspace-local npm config below.
                    execute(['bash','scripts/check-plan3.sh','--base-ref',identity.base_sha,'--with-browser']+(['--mvp-reviewed-policy'] if args.mode=='mvp' else []),env=env)
                    execute(['npm','exec','--workspace','apps/crm-web','--','vitest','run',
                             '--reporter=json','--outputFile='+str(vitest)])
                    report['suites'].extend([
                        {'name':'vitest','format':'vitest','report':json.loads(vitest.read_text())},
                        {'name':'browser','format':'playwright','report':json.loads(browser.read_text())}])
            if args.mode=='hardening' and args.job in ('backend','compose','web-browser'):
                from scripts.ci.image_identity import verify_job_evidence
                from scripts.ci.scan import ScanError
                records = [json.loads(p.read_text()) for p in (scratch/'image-evidence').glob('*.json')]
                try:
                    verify_job_evidence(records,args.job,identity.run_id)
                except ScanError:
                    raise JobError('CI_IMAGE_EVIDENCE_INCOMPLETE') from None
                report['image_evidence'] = records
        report['status'] = 'success'
        report['elapsed_seconds'] = time.monotonic()-started
        publish_report(report,args.output,identity,mode=args.mode)
        token = 'CI_JOB_COMPLETE'
    except (JobError, ReportError, ValueError, KeyError, OSError) as failure:
        report['status'] = 'failure'
        token = failure.token if isinstance(failure,JobError) else (failure.reason_token if isinstance(failure,ReportError) else 'CI_JOB_INPUT_INVALID')
    # Retain safe discovered counts on failure; missing suites still fail aggregation.
    for suite in report['suites']:
        if 'format' in suite:
            from scripts.ci.summary import parse_cases
            name = suite['name']
            try:
                cases = parse_cases(suite['format'],suite['report'])
            except ReportError:
                cases = []
            suite.clear(); suite.update(name=name,tests=cases)
    cases = [case for suite in report['suites'] for case in suite['tests']]
    report.update(passed=sum(c['outcome']=='passed' for c in cases),
                  failed=sum(c['outcome']=='failed' for c in cases),
                  skipped=sum(c['outcome']=='skipped' for c in cases), total=len(cases))
    report['reason_token'] = token
    report['elapsed_seconds'] = time.monotonic()-started
    atomic_write(args.output,report)
    print(json.dumps({'job':args.job,'status':report['status'],'reasonToken':token,'correlationId':correlation}))
    return 0 if report['status'] == 'success' else 1

if __name__ == '__main__':
    sys.exit(main())
