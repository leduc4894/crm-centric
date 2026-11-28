import threading
import unittest

from scripts.benchmarks import contact_list as bench

def valid_page(name='Name'):
    return {'items':[{'id':str(i),'displayName':name,'version':1,
                      'createdAt':'2026-01-01T00:00:00Z','updatedAt':'2026-01-01T00:00:00Z'}
                     for i in range(20)], 'nextCursor':'opaque-cursor'}

class ContactListBenchmarkTests(unittest.TestCase):
    def test_p95_nearest_rank(self):
        self.assertEqual(19, bench.nearest_rank(list(range(1, 21)), .95))
        self.assertIsNone(bench.nearest_rank([], .95))

    def test_50_independent_users_start_together(self):
        users = [{'subject': str(i), 'token': 'token-'+str(i)} for i in range(50)]
        seen = set(); guard = threading.Lock(); barrier = threading.Barrier(50)
        def request(user):
            barrier.wait(timeout=5)
            with guard: seen.add(user['subject'])
            return bench.Sample(200, True, .001)
        samples = bench.run_once_per_user(users, request)
        self.assertEqual(50, len(seen)); self.assertEqual(50, len(samples))
        with self.assertRaises(ValueError): bench.run_once_per_user(users[:-1]+[users[0]], request)

    def test_http_errors_timeouts_and_wrong_tenant_fail_run(self):
        for sample in (bench.Sample(403, True, .01), bench.Sample(None, False, .01), bench.Sample(200, False, .01)):
            report = bench.summarize([bench.Sample(200, True, .01), sample], 50, 300, 300)
            self.assertNotEqual('PASS', report['verdict'])
            self.assertEqual(1, report['invalid_or_error'])

    def test_report_contains_no_token_payload_or_connection_string(self):
        report = bench.summarize([bench.Sample(200, True, .01)], 50, 300, 300)
        text = str(report)
        for secret in ('token-123', 'private-payload', 'postgresql://'):
            self.assertNotIn(secret, text)

    def test_threshold_is_strict_and_errors_fail_valid_measurements(self):
        samples=[bench.Sample(200,True,.399)]*50
        self.assertEqual('PASS',bench.summarize(samples,50,300,300)['verdict'])
        self.assertEqual('FAIL',bench.summarize([bench.Sample(200,True,.4)]*50,50,300,300)['verdict'])
        report=bench.summarize(samples+[bench.Sample(500,False,.01)],50,300,300)
        self.assertEqual('FAIL',report['verdict'])
        self.assertEqual('BENCHMARK_MEASUREMENT_FAILED',report['reason_token'])

    def test_timeout_count_is_distinct_from_transport_failures(self):
        timeout=bench.Sample(None,False,.01,'timeout')
        transport=bench.Sample(None,False,.01,'transport')
        report=bench.summarize([timeout,transport],50,300,300)
        self.assertEqual(1,report['timeouts'])
        self.assertEqual(1,report['transport_errors'])
        self.assertNotEqual('PASS',report['verdict'])

    def test_response_requires_known_tenant_ids_and_valid_shape(self):
        page = valid_page()
        self.assertTrue(bench.validate_page(page, {str(i) for i in range(20)}))
        self.assertFalse(bench.validate_page(page, {'B'}))
        self.assertFalse(bench.validate_page({'items': [], 'nextCursor': None, 'private': 'x'}, {'A'}))
        bad=valid_page();bad['items'][0]['version']=True
        self.assertFalse(bench.validate_page(bad, {str(i) for i in range(20)}))

    def test_full_first_page_and_nonempty_cursor_are_required(self):
        item={'id':'A','displayName':'Name','version':1,'createdAt':'2026-01-01T00:00:00Z','updatedAt':'2026-01-01T00:00:00Z'}
        items=[dict(item,id=str(i)) for i in range(20)]
        ids={str(i) for i in range(20)}
        self.assertTrue(bench.validate_page({'items':items,'nextCursor':'opaque-cursor'},ids))
        self.assertFalse(bench.validate_page({'items':items[:-1],'nextCursor':'opaque-cursor'},ids))
        self.assertFalse(bench.validate_page({'items':items,'nextCursor':None},ids))
        self.assertFalse(bench.validate_page({'items':items,'nextCursor':''},ids))

    def test_truncated_http_and_invalid_status_are_classified_without_payload(self):
        import http.client
        from unittest.mock import MagicMock
        opener=MagicMock()
        response=opener.open.return_value.__enter__.return_value
        response.status=200
        response.headers.get_content_type.return_value='application/json'
        response.read.side_effect=http.client.IncompleteRead(b'private-payload')
        sample=bench.http_sample('http://localhost/','secret-token','tenant',set(),opener=opener)
        self.assertEqual((200,False,'invalid_body'),(sample.status,sample.valid,sample.reason))
        opener.open.side_effect=http.client.BadStatusLine('private-payload')
        sample=bench.http_sample('http://localhost/','secret-token','tenant',set(),opener=opener)
        self.assertEqual((None,False,'transport'),(sample.status,sample.valid,sample.reason))
        self.assertNotIn('private-payload',repr(sample))

    def test_http_response_and_timeout_are_classified_without_payload(self):
        import http.server, json
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *_): pass
            def do_GET(self):
                if self.path == '/bad':
                    self.send_response(403); self.end_headers(); return
                body=json.dumps(valid_page()).encode()
                self.send_response(200); self.send_header('Content-Type','application/json'); self.end_headers(); self.wfile.write(body)
        server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler)
        thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
        try:
            url='http://127.0.0.1:'+str(server.server_port)
            good=bench.http_sample(url+'/', 'token-123', 'tenant', {str(i) for i in range(20)})
            self.assertEqual((200, True), (good.status,good.valid))
            bad=bench.http_sample(url+'/bad', 'token-123', 'tenant', {'A'})
            self.assertEqual(403,bad.status)
            wrong=bench.http_sample(url+'/', 'token-123', 'tenant', {'B'})
            self.assertFalse(wrong.valid)
        finally: server.shutdown();server.server_close();thread.join()

    def test_configuration_requires_isolated_tenants_and_large_unique_dataset(self):
        import uuid
        tenant, other = str(uuid.uuid4()), str(uuid.uuid4())
        ids = [str(uuid.UUID(int=i+1)) for i in range(50000)]
        config={'api_origin':'http://localhost:8080','tenant_id':tenant,'other_tenant_id':other,
                'allowed_ids':ids,'other_marker_id':str(uuid.uuid4()),'search_term':'segment-07'}
        bench.validate_configuration(config)
        for mutation in ({'other_tenant_id':tenant}, {'allowed_ids':ids[:-1]}, {'api_origin':'http://user:secret@localhost/'}, {'other_marker_id':ids[0]}):
            with self.subTest(keys=list(mutation)), self.assertRaises(ValueError):
                bench.validate_configuration({**config, **mutation})

    def test_report_does_not_overwrite_prior_evidence(self):
        import tempfile
        from pathlib import Path
        with tempfile.TemporaryDirectory() as d:
            path=Path(d)/'report.json';path.write_text('previous')
            with self.assertRaises(FileExistsError): bench.write_report(path, {'verdict':'INVALID'})
            self.assertEqual('previous',path.read_text())

    def test_cli_failure_has_reason_and_correlation_without_secrets(self):
        import json, subprocess, sys, tempfile
        from pathlib import Path
        with tempfile.TemporaryDirectory() as d:
            source=Path(d)/'input.json';output=Path(d)/'report.json'
            source.write_text(json.dumps({'secret':'private-payload-token'}))
            result=subprocess.run([sys.executable,'scripts/benchmarks/contact_list.py','--input',str(source),'--output',str(output),'--scenario','default'],capture_output=True,text=True)
            self.assertNotEqual(0,result.returncode)
            data=json.loads(result.stdout)
            self.assertEqual('BENCHMARK_CONFIG_INVALID',data['reason_token'])
            self.assertTrue(data['correlation_id'])
            self.assertNotIn('private-payload-token',result.stdout+result.stderr)
            self.assertTrue(output.exists())
            self.assertEqual('BENCHMARK_CONFIG_INVALID',json.loads(output.read_text())['reason_token'])

    def test_invalid_or_duplicate_contact_values_are_rejected(self):
        ids={str(i) for i in range(20)}
        page=valid_page();page['items'][1]=dict(page['items'][0])
        self.assertFalse(bench.validate_page(page,ids))
        page=valid_page();page['items'][0]['email']=7
        self.assertFalse(bench.validate_page(page,ids))
        page=valid_page();page['items'][0]['createdAt']='invalid'
        self.assertFalse(bench.validate_page(page,ids))

    def test_search_response_must_match_requested_filter(self):
        page=valid_page('Bench segment-07 contact')
        ids={str(i) for i in range(20)}
        self.assertTrue(bench.validate_page(page, ids, 'segment-07'))
        self.assertFalse(bench.validate_page(page, ids, 'segment-08'))

    def test_timeout_and_redirect_never_count_as_success(self):
        from unittest.mock import patch
        import socket
        with patch('scripts.benchmarks.contact_list.urllib.request.build_opener') as make:
            make.return_value.open.side_effect = socket.timeout()
            sample = bench.http_sample('http://localhost/', 'token-123', 'tenant', {'A'})
        self.assertIsNone(sample.status)
        self.assertFalse(sample.valid)
        self.assertIsNone(bench.NoRedirect().redirect_request(None, None, 302, '', {}, 'http://other/'))

    def test_each_worker_reuses_its_prepared_http_opener(self):
        from unittest.mock import patch
        with patch('scripts.benchmarks.contact_list.urllib.request.build_opener') as make, \
             patch('scripts.benchmarks.contact_list.http_sample', return_value=bench.Sample(200, True, .001)) as sample:
            request=bench.make_request('http://localhost/', 'tenant', {'A'}, None)
            request({'token':'first'}); request({'token':'second'})
            self.assertEqual(1,make.call_count)
            self.assertIs(sample.call_args.kwargs['opener'],make.return_value)
            worker=threading.Thread(target=request,args=({'token':'third'},))
            worker.start(); worker.join()
            self.assertEqual(2,make.call_count)

    def test_preflight_rejects_wrong_tenant_and_unauthenticated_success(self):
        ok = bench.Sample(200, True, .01)
        denied = bench.Sample(403, False, .01)
        unauthorized = bench.Sample(401, False, .01)
        self.assertTrue(bench.preflight_valid([ok]*50, denied, unauthorized))
        self.assertFalse(bench.preflight_valid([ok]*50, ok, unauthorized))
        self.assertFalse(bench.preflight_valid([ok]*50, denied, ok))
        self.assertFalse(bench.preflight_valid([ok]*49+[denied], denied, unauthorized))

    def test_tokens_must_bind_distinct_subjects_to_issuer(self):
        import base64, json
        def token(sub):
            payload = base64.urlsafe_b64encode(json.dumps({'iss':'issuer','sub':sub}).encode()).decode().rstrip('=')
            return 'e30.'+payload+'.sig'
        users = [{'subject':str(i), 'token':token(str(i))} for i in range(50)]
        bench.validate_token_claims(users, 'issuer')
        users[1]['token'] = token('other')
        with self.assertRaises(ValueError): bench.validate_token_claims(users, 'issuer')

    def test_empty_or_short_run_cannot_pass(self):
        self.assertNotEqual('PASS', bench.summarize([], 50, 300, 300)['verdict'])
        self.assertNotEqual('PASS', bench.summarize([bench.Sample(200, True, .01)], 50, 2, 300)['verdict'])
        self.assertNotEqual('PASS', bench.summarize([bench.Sample(200, True, .01)], 49, 300, 300)['verdict'])

if __name__ == '__main__': unittest.main()
