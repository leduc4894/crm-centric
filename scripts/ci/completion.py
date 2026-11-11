"""Safe fallback for failed setup/actions; scheduler outcomes remain authoritative.

Job-level cancellation may prevent even this step from running. No cancelled or
missing workflow is converted into success by this diagnostic helper.
"""
import json
import os
from pathlib import Path
import uuid
from scripts.ci.jobs import atomic_write
from scripts.ci.summary import RunIdentity, parse_report, ReportError


def complete(job, output, steps, expected=None):
    token='CI_STEP_NON_SUCCESS'
    try:
        if not isinstance(steps,dict): raise ValueError()
        outcomes={key:value['outcome'] for key,value in steps.items()}
        if 'cancelled' in outcomes.values(): token='CI_COMMAND_CANCELLED'
        elif outcomes.get('setup')=='failure': token='CI_SETUP_FAILED'
        elif outcomes.get('revision')=='failure': token='CI_REVISION_FAILED'
        elif any(value=='failure' for value in outcomes.values()): token='CI_ACTION_FAILED'
        if expected is not None and outcomes.get('gate') in ('success','failure'):
            parsed=parse_report('normalized',output,expected)
            if parsed.job!=job: raise ValueError()
            if all(value=='success' for value in outcomes.values()) and parsed.status=='success':
                return 'CI_JOB_COMPLETE'
            # Preserve already normalized failed evidence, including discovered
            # counts, only after validating its identity and complete inventory.
            if parsed.status=='failure': return 'CI_JOB_FAILED'
    except (OSError,ValueError,TypeError,KeyError,ReportError):
        pass
    atomic_write(output,{'job':job,'status':'failure','reason_token':token,
                         'correlation_id':str(uuid.uuid4()),'suites':[],
                         'passed':0,'failed':0,'skipped':0,'total':0})
    return token


def main():
    import argparse
    class SafeParser(argparse.ArgumentParser):
        def error(self,message): raise ValueError()
    parser=SafeParser()
    parser.add_argument('job',choices=['backend','compose','contracts-sdk','web-browser','security'])
    parser.add_argument('--output',type=Path,required=True)
    try: args=parser.parse_args()
    except ValueError:
        print(json.dumps({'reasonToken':'CI_JOB_INPUT_INVALID','correlationId':str(uuid.uuid4())}))
        return 1
    expected=None
    try:
        revision=json.loads(Path('revision.json').read_text())
        expected=RunIdentity(os.environ['GITHUB_RUN_ID'],os.environ['GITHUB_RUN_ATTEMPT'],
            revision['candidate_sha'],revision['base_sha'],revision['merge_sha'])
        steps=json.loads(os.environ['CI_STEPS'])
    except (OSError,ValueError,KeyError): steps={}
    token=complete(args.job,args.output,steps,expected)
    print(json.dumps({'reasonToken':token,'correlationId':str(uuid.uuid4())}))
    return 0 if token=='CI_JOB_COMPLETE' else 1

if __name__=='__main__': raise SystemExit(main())
