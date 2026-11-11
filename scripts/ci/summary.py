from dataclasses import dataclass
import uuid

@dataclass(frozen=True)
class RunIdentity:
    run_id: str
    run_attempt: str
    candidate_sha: str
    base_sha: str
    merge_sha: str | None

class ReportError(Exception):
    def __init__(self, reason_token='CI_REPORT_INVALID'):
        self.reason_token=reason_token
        self.correlation_id=str(uuid.uuid4())
        super().__init__(reason_token)

@dataclass(frozen=True)
class CiSummary:
    job: str
    identity: RunIdentity
    status: str
    passed: int
    failed: int
    skipped: int
    total: int


# The policy path is supplied by the trusted workflow checkout, never a report.
from pathlib import Path
import json
import math
import re

POLICY_PATH = Path(__file__).resolve().parents[2] / 'tools/ci/coverage.json'

def _object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ReportError()
        result[key] = value
    return result


def validate_coverage(name, tests, policy):
    rule = policy['suites'][name]
    identities = [test['id'] for test in tests]
    if (len(identities) < rule['minimum'] or len(set(identities)) != len(identities)
            or not set(rule['required']).issubset(identities)):
        raise ReportError('CI_DISCOVERY_INCOMPLETE')
    for test in tests:
        if not isinstance(test['id'], str) or not test['id']:
            raise ReportError()
        if test['outcome'] not in ('passed', 'failed'):
            raise ReportError('CI_UNEXPECTED_SKIP')


def parse_report(kind, path, expected):
    try:
        if kind != 'normalized' or path.stat().st_size > 8 * 1024 * 1024:
            raise ReportError()
        data = json.loads(path.read_text(encoding='utf-8'), object_pairs_hook=_object)
        identity = RunIdentity(**data['identity'])
        if identity != expected:
            raise ReportError('CI_REPORT_IDENTITY_MISMATCH')
        if (not all(isinstance(x, str) and x for x in (identity.run_id, identity.run_attempt))
                or not all(isinstance(x, str) and re.fullmatch('[0-9a-f]{40}', x)
                           for x in (identity.candidate_sha, identity.base_sha))
                or identity.merge_sha not in (None, identity.candidate_sha)):
            raise ReportError()
        policy = json.loads(POLICY_PATH.read_text())
        job = data['job']
        if job not in policy['jobs'] or data['status'] not in ('success', 'failure'):
            raise ReportError()
        if (not re.fullmatch('[a-z0-9-]+', data['command_id'])
                or type(data['elapsed_seconds']) not in (int,float)
                or not math.isfinite(data['elapsed_seconds']) or data['elapsed_seconds'] < 0
                or not isinstance(data['tools'],dict) or not data['tools']
                or not isinstance(data['artifacts'],list)):
            raise ReportError()
        if job in ('backend','compose','web-browser') and data['status']=='success':
            from scripts.ci.image_identity import verify_job_evidence
            from scripts.ci.scan import ScanError
            try:
                verify_job_evidence(data.get('image_evidence',[]),job,identity.run_id)
            except (ScanError,KeyError,TypeError,ValueError):
                raise ReportError('CI_IMAGE_EVIDENCE_INCOMPLETE') from None
        suites = data['suites']
        names = [suite['name'] for suite in suites]
        if len(names) != len(set(names)) or set(names) != set(policy['jobs'][job]):
            raise ReportError('CI_DISCOVERY_INCOMPLETE')
        outcomes = []
        for suite in suites:
            tests = (parse_cases(suite['format'], suite['report'])
                     if 'format' in suite else suite['tests'])
            validate_coverage(suite['name'], tests, policy)
            outcomes.extend(test['outcome'] for test in tests)
        failed = outcomes.count('failed')
        if failed and data['status'] == 'success':
            raise ReportError('CI_REPORT_STATUS_MISMATCH')
        return CiSummary(job, identity, data['status'], outcomes.count('passed'),
                         failed, 0, len(outcomes))
    except ReportError:
        raise
    except (OSError, ValueError, TypeError, KeyError, AttributeError, RecursionError, OverflowError):
        raise ReportError() from None


def parse_cases(kind, raw):
    """Extract only stable test identity/outcome, discarding raw diagnostics.

    Native data is embedded in a run-bound envelope; native formats alone do
    not contain enough CI provenance to constitute an accepted job summary.
    """
    try:
        tests=[]
        if kind == 'trx':
            import xml.etree.ElementTree as ET
            if not isinstance(raw,str) or '<!' in raw:
                raise ReportError()
            try:
                tree=ET.fromstring(raw)
            except ET.ParseError:
                raise ReportError() from None
            for element in tree.iter():
                element.tag=element.tag.rsplit('}',1)[-1]
            for result in tree.findall('./Results/UnitTestResult'):
                outcome={'Passed':'passed','Failed':'failed'}.get(result.attrib['outcome'])
                if outcome is None:
                    raise ReportError('CI_UNEXPECTED_SKIP')
                tests.append({'id':result.attrib['testName'],'outcome':outcome})
            summary=tree.find('./ResultSummary')
            counters=summary.find('./Counters').attrib
            if (summary.attrib.get('outcome') in ('Failed','Error','Aborted','Timeout')
                    or any(int(counters.get(key, '0')) for key in
                           ('error','timeout','aborted','inconclusive','notExecuted'))):
                raise ReportError('CI_TEST_EVIDENCE_FAILED')
            if (int(counters['total']) != len(tests)
                    or int(counters['passed']) != sum(t['outcome']=='passed' for t in tests)
                    or int(counters['failed']) != sum(t['outcome']=='failed' for t in tests)):
                raise ReportError('CI_DISCOVERY_INCOMPLETE')
        elif kind == 'vitest':
            if (raw.get('success') is False or raw.get('numFailedTestSuites',0)
                    or raw.get('numRuntimeErrorTestSuites',0)):
                raise ReportError('CI_TEST_EVIDENCE_FAILED')
            for file in raw['testResults']:
                name=file['name'].replace('\\','/')
                if '/apps/crm-web/' in name:
                    name='apps/crm-web/'+name.split('/apps/crm-web/',1)[1]
                if name.startswith('/') or '..' in name.split('/'):
                    raise ReportError()
                for case in file['assertionResults']:
                    tests.append({'id':name+'::'+' > '.join(case['ancestorTitles']+[case['title']]),
                                  'outcome':case['status']})
            if type(raw['numTotalTests']) is not int or raw['numTotalTests'] != len(tests):
                raise ReportError('CI_DISCOVERY_INCOMPLETE')
        elif kind == 'playwright':
            if raw.get('errors'):
                raise ReportError('CI_TEST_EVIDENCE_FAILED')
            stats = raw['stats']
            counters = [stats[key] for key in ('expected','unexpected','skipped','flaky')]
            if any(type(count) is not int or count < 0 for count in counters):
                raise ReportError()
            if stats['skipped'] or stats['flaky']:
                raise ReportError('CI_TEST_EVIDENCE_FAILED')
            def visit(suites,parents):
                for suite in suites:
                    for spec in suite.get('specs',[]):
                        for case in spec['tests']:
                            results=case['results']
                            if case['expectedStatus'] != 'passed' or not results:
                                raise ReportError('CI_UNEXPECTED_SKIP')
                            statuses=[result['status'] for result in results]
                            if any(status not in ('passed','failed','timedOut','interrupted') for status in statuses):
                                raise ReportError('CI_UNEXPECTED_SKIP')
                            outcome = 'passed' if statuses == ['passed'] else 'failed'
                            if case['status'] != ('expected' if outcome == 'passed' else 'unexpected'):
                                raise ReportError('CI_REPORT_STATUS_MISMATCH')
                            tests.append({'id':suite['file']+'::'+' > '.join(parents+[spec['title']])+'::'+case['projectName'],
                                          'outcome':outcome})
                    visit(suite.get('suites',[]),parents+[suite['title']] if suite.get('title') else parents)
            visit(raw['suites'],[])
            if (sum(counters) != len(tests)
                    or stats['expected'] != sum(test['outcome'] == 'passed' for test in tests)
                    or stats['unexpected'] != sum(test['outcome'] == 'failed' for test in tests)):
                raise ReportError('CI_DISCOVERY_INCOMPLETE')
        else:
            raise ReportError('CI_REPORT_FORMAT_UNSUPPORTED')
        if not tests or any(test['outcome'] not in ('passed','failed') for test in tests):
            raise ReportError('CI_UNEXPECTED_SKIP')
        return tests
    except ReportError:
        raise
    except (ValueError, TypeError, KeyError, AttributeError, RecursionError):
        raise ReportError() from None


def validate_simulation(output, policy):
    """Only the explicitly named missing-prerequisite cases may skip here.

    This exception belongs to a simulation, never to a real suite report.
    """
    rows = re.findall(r'^\S+ \(([^\n()]+)\) \.\.\. (ok|skipped \'([^\n]*)\')$',
                      output, re.MULTILINE)
    identities = [row[0] for row in rows]
    rule = policy['suites']['contract']
    if (len(identities) < rule['minimum'] or len(set(identities)) != len(identities)
            or not set(rule['required']).issubset(identities)):
        raise ReportError('CI_DISCOVERY_INCOMPLETE')
    skipped = {identity: reason for identity, status, reason in rows if status != 'ok'}
    if set(skipped) != set(policy['simulation_skips']):
        raise ReportError('CI_UNEXPECTED_SKIP')
    for identity, reason in skipped.items():
        if not reason or not re.search(policy['simulation_skips'][identity], reason):
            raise ReportError('CI_UNEXPECTED_SKIP')


def simulation_main():
    import sys
    try:
        if len(sys.argv) != 3 or sys.argv[1] != '--validate-simulation':
            raise ReportError()
        validate_simulation(Path(sys.argv[2]).read_text(), json.loads(POLICY_PATH.read_text()))
        print('ci simulation: reviewed required identities and named prerequisite skips verified')
        return 0
    except (ReportError, OSError, ValueError, KeyError, TypeError) as failure:
        error = failure if isinstance(failure, ReportError) else ReportError()
        print(json.dumps({'reasonToken':error.reason_token, 'correlationId':error.correlation_id}))
        return 1


if __name__ == '__main__':
    raise SystemExit(simulation_main())
