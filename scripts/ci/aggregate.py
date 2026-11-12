"""Platform outcomes and complete run-bound reports must both succeed.

The workflow must schedule its required aggregator with always() and explicit
needs. Whole-workflow cancellation or an unscheduled aggregator is non-success;
this module cannot manufacture evidence for a job the scheduler never ran.
"""
from dataclasses import dataclass
import uuid

INVENTORY = ('backend', 'compose', 'contracts-sdk', 'web-browser', 'security')

@dataclass(frozen=True)
class AggregateResult:
    success: bool
    reason_token: str
    correlation_id: str


def aggregate(needs, summaries, expected):
    reason = 'CI_COMPLETE'
    if set(needs) != set(INVENTORY) or any(needs[job] != 'success' for job in INVENTORY):
        reason = 'CI_JOB_NON_SUCCESS'
    elif (len(summaries) != len(INVENTORY)
          or {summary.job for summary in summaries} != set(INVENTORY)):
        reason = 'CI_REPORT_INVENTORY_INVALID'
    elif any(summary.identity != expected for summary in summaries):
        reason = 'CI_REPORT_IDENTITY_MISMATCH'
    elif any(summary.status != 'success' or summary.failed or summary.skipped
             or summary.total <= 0 or summary.passed != summary.total for summary in summaries):
        reason = 'CI_TEST_EVIDENCE_FAILED'
    return AggregateResult(reason == 'CI_COMPLETE', reason, str(uuid.uuid4()))


def main():
    import argparse
    from dataclasses import asdict
    import json
    from pathlib import Path
    from scripts.ci.summary import ReportError, RunIdentity, parse_report, _object

    class SafeParser(argparse.ArgumentParser):
        def error(self, message):
            raise ReportError('CI_AGGREGATE_INPUT_INVALID')

    def read(path):
        if path.stat().st_size > 1024 * 1024:
            raise ReportError('CI_AGGREGATE_INPUT_INVALID')
        return json.loads(path.read_text(), object_pairs_hook=_object)

    parser = SafeParser(description=__doc__)
    parser.add_argument('--mode',choices=['mvp','hardening'],default='hardening')
    parser.add_argument('--identity', type=Path, required=True)
    parser.add_argument('--needs', type=Path, required=True)
    parser.add_argument('--reports', type=Path, nargs='+', required=True)
    try:
        args = parser.parse_args()
        expected = RunIdentity(**read(args.identity))
        needs = read(args.needs)
        # Accept GitHub toJSON(needs), retaining platform result authority.
        if isinstance(needs,dict):
            needs = {key:value.get('result') if isinstance(value,dict) else value
                     for key,value in needs.items()}
        else:
            raise ReportError('CI_AGGREGATE_INPUT_INVALID')
        summaries = [parse_report('normalized', path, expected, mode=args.mode) for path in args.reports]
        result = aggregate(needs, summaries, expected)
    except (ReportError, OSError, ValueError, TypeError, KeyError, RecursionError) as failure:
        reason = failure.reason_token if isinstance(failure,ReportError) else 'CI_AGGREGATE_INPUT_INVALID'
        result = AggregateResult(False, reason, str(uuid.uuid4()))
    print(json.dumps(asdict(result),sort_keys=True))
    return 0 if result.success else 1


if __name__ == '__main__':
    raise SystemExit(main())
