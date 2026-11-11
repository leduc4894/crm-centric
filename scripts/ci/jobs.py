"""Isolated locked job runner. Raw subprocess diagnostics never leave this process."""
import argparse
import json
import os
from pathlib import Path
import platform
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

def execute(command, timeout=3300, env=None):
    try:
        result = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                timeout=timeout, env=env)
    except subprocess.TimeoutExpired:
        raise JobError('CI_COMMAND_TIMEOUT') from None
    except OSError:
        raise JobError('CI_PREREQUISITE_MISSING') from None
    if result.returncode:
        raise JobError('CI_COMMAND_FAILED')
    return result.stdout.decode().strip()

def tool_versions():
    return {'node':execute(['node','--version']).lstrip('v'),
            'npm':execute(['npm','--version']), 'python':platform.python_version(),
            'dotnet':execute(['dotnet','--version']),
            'docker':execute(['docker','version','--format','{{.Server.Version}}']),
            'compose':execute(['docker','compose','version','--short'])}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('job', choices=['backend','compose','contracts-sdk','web-browser'])
    parser.add_argument('--revision', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
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
        with tempfile.TemporaryDirectory(prefix='ci-job-') as directory:
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
                execute(['dotnet','test','tests/Integration/Integration.csproj','-c','Release',
                         '--no-build','--no-restore','--filter',category,
                         '--logger','trx;LogFileName=results.trx','--results-directory',str(scratch)])
                report['suites'].append({'name':'compose' if args.job == 'compose' else 'non-compose',
                                        'format':'trx','report':(scratch/'results.trx').read_text()})
                if args.job == 'backend': py_suite('local','tests/Local')
            else:
                execute(['npm','ci','--ignore-scripts','--no-audit','--no-fund'])
                image = json.loads(Path('tools/contracts/toolchain.json').read_text())['dotnetImage']
                execute(['docker','pull',image])
                if args.job == 'contracts-sdk':
                    py_suite('contract','tests/Contract')
                    execute([sys.executable,'scripts/contracts/check.py'])
                    execute(['bash','tests/ClientConsumers/run.sh'])
                    execute([sys.executable,'scripts/contracts/compatibility.py','--base-ref',identity.base_sha])
                    for harness in sorted(Path('tests/Plan3').glob('*.sh')):
                        execute(['bash',str(harness)])
                else:
                    execute(['npm','exec','--workspace','apps/crm-web','--','playwright','install','--with-deps','chromium'])
                    vitest = scratch/'vitest.json'; browser = scratch/'browser.json'
                    env = {**os.environ,'PLAYWRIGHT_JSON_OUTPUT_NAME':str(browser),
                           'PLAYWRIGHT_HTML_OPEN':'never'}
                    # Plan 3 owns real API stack startup, readiness and teardown.
                    # Reporter args are supplied through the workspace-local npm config below.
                    execute(['bash','scripts/check-plan3.sh','--base-ref',identity.base_sha,'--with-browser'],env=env)
                    execute(['npm','exec','--workspace','apps/crm-web','--','vitest','run',
                             '--reporter=json','--outputFile='+str(vitest)])
                    report['suites'].extend([
                        {'name':'vitest','format':'vitest','report':json.loads(vitest.read_text())},
                        {'name':'browser','format':'playwright','report':json.loads(browser.read_text())}])
        report['status'] = 'success'
        report['elapsed_seconds'] = time.monotonic()-started
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report))
        parsed = parse_report('normalized', args.output, identity)
        # Publish only normalized identities and outcomes, never native payloads.
        from scripts.ci.summary import parse_cases
        for suite in report['suites']:
            if 'format' in suite:
                cases = parse_cases(suite['format'],suite['report'])
                name = suite['name']
                suite.clear(); suite.update(name=name, tests=cases)
        report.update(passed=parsed.passed,failed=parsed.failed,skipped=parsed.skipped,total=parsed.total)
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
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report))
    print(json.dumps({'job':args.job,'status':report['status'],'reasonToken':token,'correlationId':correlation}))
    return 0 if report['status'] == 'success' else 1

if __name__ == '__main__':
    sys.exit(main())
