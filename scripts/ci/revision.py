"""Fail-closed GitHub revision proof; never fetches or mutates the checkout."""
import argparse
from dataclasses import asdict, dataclass
import json
import os
from pathlib import Path
import re
import subprocess
import uuid


class RevisionError(Exception):
    def __init__(self, reason_token):
        self.reason_token = reason_token
        self.correlation_id = str(uuid.uuid4())
        super().__init__(reason_token)


@dataclass(frozen=True)
class RevisionContext:
    candidate_sha: str
    base_sha: str
    head_sha: str
    merge_sha: str | None
    merge_base_sha: str


def _sha(value, token):
    if not isinstance(value, str) or not re.fullmatch(r'[0-9a-f]{40}', value) or value == '0' * 40:
        raise RevisionError(token)
    return value


class GitReader:
    """Read local objects with bounded subprocess time and suppressed diagnostics."""
    def __init__(self, path='.'):
        self.path = path

    def _run(self, args, token):
        try:
            result = subprocess.run(['git', '--no-replace-objects', *args], cwd=self.path,
                                    capture_output=True, text=True, timeout=15,
                                    env={**os.environ, 'GIT_NO_LAZY_FETCH': '1',
                                         'GIT_TERMINAL_PROMPT': '0'})
        except (OSError, subprocess.SubprocessError):
            raise RevisionError(token) from None
        if result.returncode:
            raise RevisionError(token)
        return result.stdout.strip()

    def commit(self, sha, token):
        _sha(sha, token)
        if self._run(['cat-file', '-t', sha], token) != 'commit':
            raise RevisionError(token)

    def parents(self, sha):
        _sha(sha, 'CI_CANDIDATE_UNAVAILABLE')
        # rev-list only emits one row, and does not print commit content.
        return self._run(['rev-list', '--parents', '-n', '1', sha],
                         'CI_CANDIDATE_UNAVAILABLE').split()[1:]

    def merge_base(self, candidate, base):
        _sha(candidate, 'CI_CANDIDATE_UNAVAILABLE')
        _sha(base, 'CI_BASE_UNAVAILABLE')
        return self._run(['merge-base', candidate, base], 'CI_BASE_UNAVAILABLE')

    def checkout(self):
        return _sha(self._run(['rev-parse', '--verify', 'HEAD'],
                             'CI_CANDIDATE_UNAVAILABLE'), 'CI_CANDIDATE_UNAVAILABLE')


def resolve_event(event: dict, checkout_sha: str, git: GitReader) -> RevisionContext:
    checkout = _sha(checkout_sha, 'CI_CANDIDATE_UNAVAILABLE')
    if not isinstance(event, dict):
        raise RevisionError('CI_BASE_UNAVAILABLE')
    kind = event.get('event_name')
    if kind is None:
        kind = 'pull_request' if 'pull_request' in event else 'push' if 'before' in event else None
    if kind == 'pull_request':
        pr = event.get('pull_request')
        if not isinstance(pr, dict):
            raise RevisionError('CI_CANDIDATE_UNAVAILABLE')
        base_data, head_data = pr.get('base'), pr.get('head')
        base = _sha(base_data.get('sha') if isinstance(base_data, dict) else None,
                    'CI_BASE_UNAVAILABLE')
        head = _sha(head_data.get('sha') if isinstance(head_data, dict) else None,
                    'CI_CANDIDATE_UNAVAILABLE')
        candidate = _sha(pr.get('merge_commit_sha'), 'CI_CANDIDATE_UNAVAILABLE')
        git.commit(base, 'CI_BASE_UNAVAILABLE')
        git.commit(head, 'CI_CANDIDATE_UNAVAILABLE')
        git.commit(candidate, 'CI_CANDIDATE_UNAVAILABLE')
        if checkout != candidate or git.parents(candidate) != [base, head]:
            raise RevisionError('CI_CANDIDATE_UNAVAILABLE')
        merge_base = git.merge_base(candidate, base)
        if merge_base != base:
            raise RevisionError('CI_BASE_UNAVAILABLE')
        return RevisionContext(candidate, base, head, candidate, merge_base)
    if kind == 'push':
        base = _sha(event.get('before'), 'CI_BASE_UNAVAILABLE')
        candidate = _sha(event.get('after'), 'CI_CANDIDATE_UNAVAILABLE')
        if event.get('forced') is not False:
            raise RevisionError('CI_BASE_UNAVAILABLE')
        git.commit(base, 'CI_BASE_UNAVAILABLE')
        git.commit(candidate, 'CI_CANDIDATE_UNAVAILABLE')
        if checkout != candidate:
            raise RevisionError('CI_CANDIDATE_UNAVAILABLE')
        merge_base = git.merge_base(candidate, base)
        if merge_base != base:
            raise RevisionError('CI_BASE_UNAVAILABLE')
        return RevisionContext(candidate, base, candidate, None, merge_base)
    # Manual/queue events need separately reviewed event-specific proof.
    raise RevisionError('CI_BASE_UNAVAILABLE')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--event-file', required=True)
    parser.add_argument('--event-name', default=os.environ.get('GITHUB_EVENT_NAME'))
    parser.add_argument('--checkout-sha')
    args = parser.parse_args()
    try:
        path = Path(args.event_file)
        if path.stat().st_size > 4 * 1024 * 1024:
            raise RevisionError('CI_CANDIDATE_UNAVAILABLE')
        event = json.loads(path.read_text(encoding='utf-8'))
        if not isinstance(event, dict):
            raise RevisionError('CI_CANDIDATE_UNAVAILABLE')
        if args.event_name:
            event['event_name'] = args.event_name
        git = GitReader()
        actual_checkout = git.checkout()
        if args.checkout_sha is not None and args.checkout_sha != actual_checkout:
            raise RevisionError('CI_CANDIDATE_UNAVAILABLE')
        context = resolve_event(event, actual_checkout, git)
        print(json.dumps(asdict(context), sort_keys=True))
        return 0
    except (OSError, ValueError, RecursionError):
        error = RevisionError('CI_CANDIDATE_UNAVAILABLE')
    except RevisionError as failure:
        error = failure
    print(json.dumps({'reasonToken': error.reason_token,
                      'correlationId': error.correlation_id}, sort_keys=True))
    return 1


if __name__ == '__main__':
    raise SystemExit(main())
