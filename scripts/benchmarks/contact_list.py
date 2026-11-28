"""Bounded closed-loop contact-list benchmark; reports aggregates only."""
import argparse
import base64
import concurrent.futures
from dataclasses import dataclass
from datetime import datetime
import json
import math
import subprocess
from pathlib import Path
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

WORKERS = 50
WARMUP_SECONDS = 60
MEASURE_SECONDS = 300
TIMEOUT_SECONDS = 10

@dataclass(frozen=True)
class Sample:
    status: int | None
    valid: bool
    seconds: float
    reason: str = ''


def nearest_rank(values, percentile):
    if not 0 < percentile <= 1:
        raise ValueError('invalid percentile')
    if not values:
        return None
    ordered = sorted(values)
    return ordered[math.ceil(percentile * len(ordered)) - 1]


def _validate_users(users):
    if len(users) != WORKERS or len({u['subject'] for u in users}) != WORKERS or len({u['token'] for u in users}) != WORKERS:
        raise ValueError('BENCHMARK_IDENTITIES_INVALID')
    if any(not isinstance(u['subject'], str) or not u['subject'] or not isinstance(u['token'], str) or not u['token'] for u in users):
        raise ValueError('BENCHMARK_IDENTITIES_INVALID')


def run_once_per_user(users, request):
    _validate_users(users)
    barrier = threading.Barrier(WORKERS)
    def worker(user):
        barrier.wait(timeout=10)
        return request(user)
    with concurrent.futures.ThreadPoolExecutor(max_workers=WORKERS) as pool:
        return list(pool.map(worker, users))


def summarize(samples, workers, elapsed, required_duration):
    valid = [s.seconds * 1000 for s in samples if s.status == 200 and s.valid and math.isfinite(s.seconds) and s.seconds >= 0]
    errors = len(samples) - len(valid)
    p95 = nearest_rank(valid, .95)
    complete = workers == WORKERS and elapsed >= required_duration and len(valid) >= WORKERS
    verdict = 'PASS' if complete and errors == 0 and p95 is not None and p95 < 400 else 'INVALID' if not complete else 'FAIL'
    reason = {'PASS':'BENCHMARK_COMPLETE','FAIL':'BENCHMARK_MEASUREMENT_FAILED','INVALID':'BENCHMARK_MEASUREMENT_INVALID'}[verdict]
    return {'verdict': verdict, 'reason_token': reason, 'workers': workers, 'elapsed_seconds': round(elapsed, 3),
            'samples': len(samples), 'successful_valid': len(valid), 'invalid_or_error': errors,
            'http_errors': sum(s.status is not None and s.status != 200 for s in samples),
            'timeouts': sum(s.reason == 'timeout' for s in samples),
            'transport_errors': sum(s.status is None and s.reason != 'timeout' for s in samples),
            'invalid_bodies': sum(s.status == 200 and not s.valid for s in samples),
            'throughput_per_second': len(valid) / elapsed if elapsed > 0 else 0,
            'p50_ms': nearest_rank(valid, .50), 'p95_ms': p95, 'p99_ms': nearest_rank(valid, .99)}


def validate_token_claims(users, issuer):
    """Check distinct claimed principals; the API must verify signatures on use."""
    _validate_users(users)
    if not isinstance(issuer, str) or not issuer:
        raise ValueError('BENCHMARK_IDENTITIES_INVALID')
    for user in users:
        try:
            parts = user['token'].split('.')
            if len(parts) != 3 or len(parts[1]) > 16384:
                raise ValueError()
            claims = json.loads(base64.urlsafe_b64decode(parts[1] + '=' * (-len(parts[1]) % 4)))
            if claims.get('iss') != issuer or claims.get('sub') != user['subject']:
                raise ValueError()
        except (ValueError, TypeError, KeyError, UnicodeError, AttributeError):
            raise ValueError('BENCHMARK_IDENTITIES_INVALID') from None


def validate_configuration(config):
    try:
        url = urllib.parse.urlsplit(config['api_origin'])
        if url.scheme not in ('http', 'https') or not url.hostname or url.username or url.password or url.path not in ('', '/') or url.query or url.fragment:
            raise ValueError()
        _ = url.port
        tenant, other, marker = (str(uuid.UUID(config[name])) for name in ('tenant_id','other_tenant_id','other_marker_id'))
        ids = config['allowed_ids']
        if tenant == other or not isinstance(ids, list) or len(ids) < 50000 or len(set(ids)) != len(ids):
            raise ValueError()
        if any(str(uuid.UUID(value)) != value for value in ids) or marker in ids:
            raise ValueError()
        if not isinstance(config['search_term'], str) or not 1 <= len(config['search_term']) <= 100:
            raise ValueError()
    except (KeyError, ValueError, TypeError, AttributeError):
        raise ValueError('BENCHMARK_CONFIG_INVALID') from None


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def validate_page(body, expected_ids, search_term=None):
    if not isinstance(body, dict) or set(body) != {'items', 'nextCursor'} or not isinstance(body['items'], list) or not 1 <= len(body['items']) <= 20:
        return False
    if body['nextCursor'] is not None and not isinstance(body['nextCursor'], str):
        return False
    seen = set()
    for item in body['items']:
        if (not isinstance(item, dict) or item.get('id') not in expected_ids
                or item['id'] in seen or not isinstance(item.get('displayName'), str)
                or not 1 <= len(item['displayName']) <= 200 or type(item.get('version')) is not int
                or item['version'] < 1 or set(item) - {'id','displayName','email','phone','version','createdAt','updatedAt'}):
            return False
        seen.add(item['id'])
        if search_term is not None and search_term.casefold() not in item['displayName'].casefold() and search_term.casefold() not in str(item.get('email') or '').casefold():
            return False
        for name, limit in (('email', 320), ('phone', 40)):
            value = item.get(name)
            if value is not None and (not isinstance(value, str) or len(value) > limit):
                return False
        for name in ('createdAt','updatedAt'):
            try:
                value = item[name]
                if not isinstance(value, str) or datetime.fromisoformat(value.replace('Z','+00:00')).utcoffset() is None:
                    return False
            except (KeyError, ValueError):
                return False
    return True


def http_sample(url, token, tenant, expected_ids, search_term=None):
    started = time.monotonic()
    status = None
    valid = False
    try:
        request = urllib.request.Request(url, headers={'Authorization':'Bearer '+token, 'X-Tenant-Id':tenant, 'Accept':'application/json'})
        with urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect()).open(request, timeout=TIMEOUT_SECONDS) as response:
            status = response.status
            raw = response.read(1024 * 1024 + 1)
            if status == 200 and response.headers.get_content_type() == 'application/json' and len(raw) <= 1024 * 1024:
                def unique(pairs):
                    result = {}
                    for key, value in pairs:
                        if key in result: raise ValueError('duplicate key')
                        result[key] = value
                    return result
                valid = validate_page(json.loads(raw, object_pairs_hook=unique), expected_ids, search_term)
    except urllib.error.HTTPError as error:
        status = error.code
        error.close()
    except urllib.error.URLError as error:
        reason = 'timeout' if isinstance(error.reason, TimeoutError) else 'transport'
        return Sample(None, False, time.monotonic() - started, reason)
    except TimeoutError:
        return Sample(None, False, time.monotonic() - started, 'timeout')
    except (OSError, ValueError, TypeError, UnicodeError):
        return Sample(status, False, time.monotonic() - started, 'transport' if status is None else 'invalid_body')
    return Sample(status, valid, time.monotonic() - started)


def preflight_valid(users, wrong_tenant, unauthorized):
    return (len(users) == WORKERS and all(s.status == 200 and s.valid for s in users)
            and wrong_tenant.status in (403, 404) and unauthorized.status in (401, 403))


def run_benchmark(users, request, warmup=WARMUP_SECONDS, duration=MEASURE_SECONDS):
    _validate_users(users)
    if warmup < WARMUP_SECONDS or duration < MEASURE_SECONDS:
        raise ValueError('BENCHMARK_DURATION_INVALID')
    barrier = threading.Barrier(WORKERS)
    start = [None]
    results = [[] for _ in users]
    def worker(index):
        slot = barrier.wait(timeout=30)
        if slot == 0:
            start[0] = time.monotonic()
        barrier.wait(timeout=30)
        begin = start[0] + warmup
        end = begin + duration
        while time.monotonic() < end:
            dispatched = time.monotonic()
            sample = request(users[index])
            if dispatched >= begin:
                results[index].append(sample)
    with concurrent.futures.ThreadPoolExecutor(max_workers=WORKERS) as pool:
        futures = [pool.submit(worker, i) for i in range(WORKERS)]
        for future in futures:
            future.result()
    elapsed = max(0, time.monotonic() - (start[0] + warmup))
    report = summarize([sample for row in results for sample in row], sum(bool(row) for row in results), elapsed, duration)
    return report


def write_report(path, report):
    with path.open('x', encoding='utf-8') as output:
        json.dump(report, output, sort_keys=True, indent=2)
        output.write('\n')


def main():
    parser = argparse.ArgumentParser(description='Run an isolated contact-list benchmark')
    parser.add_argument('--input', required=True, type=Path, help='private configuration file')
    parser.add_argument('--output', required=True, type=Path, help='aggregate JSON output')
    parser.add_argument('--scenario', required=True, choices=('default','search'))
    args = parser.parse_args()
    correlation = str(uuid.uuid4())
    try:
        if args.input.resolve() == args.output.resolve() or args.input.stat().st_size > 32 * 1024 * 1024:
            raise ValueError('BENCHMARK_CONFIG_INVALID')
        config = json.loads(args.input.read_text())
        validate_configuration(config)
        users = config['users']
        validate_token_claims(users, config['issuer'])
        if any('\r' in u['token'] or '\n' in u['token'] for u in users):
            raise ValueError('BENCHMARK_IDENTITIES_INVALID')
        candidate = subprocess.run(['git','rev-parse','HEAD'], check=True, capture_output=True, text=True).stdout.strip()
        if config['candidate_sha'] != candidate or len(candidate) != 40:
            raise ValueError('BENCHMARK_CANDIDATE_MISMATCH')
        status = subprocess.run(['git','status','--porcelain'], check=True, capture_output=True, text=True).stdout
        if status:
            raise ValueError('BENCHMARK_WORKTREE_DIRTY')
        origin = config['api_origin'].rstrip('/')
        query = {'limit':'20'}
        if args.scenario == 'search': query['q'] = config['search_term']
        url = origin + '/api/v1/contacts?' + urllib.parse.urlencode(query)
        allowed = frozenset(config['allowed_ids'])
        tenant = config['tenant_id']
        search_term = config['search_term'] if args.scenario == 'search' else None
        def request(user): return http_sample(url, user['token'], tenant, allowed, search_term)
        checks = run_once_per_user(users, request)
        wrong = http_sample(url, users[0]['token'], config['other_tenant_id'], allowed)
        unauthorized = http_sample(url, 'invalid', tenant, allowed)
        if not preflight_valid(checks, wrong, unauthorized):
            raise ValueError('BENCHMARK_PREFLIGHT_FAILED')
        report = run_benchmark(users, request)
        report.update({'scenario':args.scenario, 'candidate_sha':candidate, 'dataset_size':len(allowed),
                       'warmup_seconds':WARMUP_SECONDS, 'measurement_seconds':MEASURE_SECONDS,
                       'timeout_seconds':TIMEOUT_SECONDS, 'correlation_id':correlation,
                       'request_shape':'GET /api/v1/contacts?limit=20' + ('&q=<synthetic>' if args.scenario == 'search' else '')})
        write_report(args.output, report)
        print(json.dumps({'reason_token':report['reason_token'],'correlation_id':correlation,'verdict':report['verdict']}))
        return 0 if report['verdict'] == 'PASS' else 1
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        reasons = {'BENCHMARK_CONFIG_INVALID','BENCHMARK_IDENTITIES_INVALID',
                   'BENCHMARK_CANDIDATE_MISMATCH','BENCHMARK_WORKTREE_DIRTY',
                   'BENCHMARK_PREFLIGHT_FAILED','BENCHMARK_DURATION_INVALID'}
        token = str(error) if type(error) is ValueError and str(error) in reasons else 'BENCHMARK_INVALID'
        failure = {'reason_token':token, 'correlation_id':correlation, 'verdict':'INVALID', 'scenario':args.scenario}
        try:
            if args.input.resolve() == args.output.resolve():
                raise OSError('same file')
            write_report(args.output, failure)
            failure['evidence_saved'] = True
        except OSError:
            failure['evidence_saved'] = False
            failure['evidence_reason_token'] = 'BENCHMARK_EVIDENCE_WRITE_FAILED'
        print(json.dumps(failure))
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
