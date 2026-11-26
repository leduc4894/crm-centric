import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('local_realm', Path(__file__).parents[2] / 'scripts/local/realm.py')

class RealmBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.module = importlib.util.module_from_spec(SPEC)
        SPEC.loader.exec_module(self.module)

    def test_import_configures_exact_crm_audience_and_logout_without_changing_demo(self):
        realm = json.loads((Path(__file__).parents[2] / 'infra/keycloak/crm-local-realm.json').read_text())
        crm, demo = realm['clients']
        self.assertEqual('crm-web-local', crm['clientId'])
        self.assertEqual('http://localhost:5173/', crm['attributes']['post.logout.redirect.uris'])
        mapper, = crm['protocolMappers']
        self.assertEqual('oidc-audience-mapper', mapper['protocolMapper'])
        self.assertEqual('crm-api', mapper['config']['included.custom.audience'])
        self.assertEqual('true', mapper['config']['access.token.claim'])
        self.assertEqual('false', mapper['config']['id.token.claim'])
        self.assertEqual('demo-module-local', demo['clientId'])
        self.assertNotIn('protocolMappers', demo)
        self.assertEqual({'pkce.code.challenge.method': 'S256'}, demo['attributes'])
        for client in (crm, demo):
            self.assertFalse(client['implicitFlowEnabled'])
            self.assertFalse(client['directAccessGrantsEnabled'])
            self.assertEqual('S256', client['attributes']['pkce.code.challenge.method'])
            self.assertTrue(all('*' not in uri for uri in client['redirectUris']))

    def test_inspection_detects_stale_client_and_mapper_configuration(self):
        import copy
        from http.server import BaseHTTPRequestHandler, HTTPServer
        import threading

        config = json.loads((Path(__file__).parents[2] / 'infra/keycloak/crm-local-realm.json').read_text())
        clients = copy.deepcopy(config['clients'])
        crm, _ = clients
        crm['id'] = '11111111-1111-4111-8111-111111111111'
        mapper = crm.pop('protocolMappers')[0]
        cases = [
            ('ready', lambda client, mappers: None),
            ('drift', lambda client, mappers: client['attributes'].pop('post.logout.redirect.uris')),
            ('drift', lambda client, mappers: client['attributes'].__setitem__('post.logout.redirect.uris', '*')),
            ('drift', lambda client, mappers: mappers.clear()),
            ('drift', lambda client, mappers: mappers.append(copy.deepcopy(mappers[0]))),
            ('drift', lambda client, mappers: mappers[0]['config'].__setitem__('included.custom.audience', 'wrong')),
            ('drift', lambda client, mappers: mappers[0]['config'].__setitem__('access.token.claim', 'false')),
            ('drift', lambda client, mappers: mappers[0]['config'].__setitem__('id.token.claim', 'true')),
        ]
        for expected, mutate in cases:
            with self.subTest(expected=expected, mutate=mutate), tempfile.TemporaryDirectory() as directory:
                current = copy.deepcopy(clients)
                mappers = [copy.deepcopy(mapper)]
                mutate(current[0], mappers)
                class Peer(BaseHTTPRequestHandler):
                    def log_message(self, *_args):
                        pass
                    def do_POST(self):
                        self.rfile.read(int(self.headers.get('Content-Length', '0')))
                        self.reply({'access_token': 'test-only-sentinel'})
                    def do_GET(self):
                        base = f'http://localhost:{self.server.server_port}/realms/crm-local'
                        paths = {
                            '/realms/crm-local/.well-known/openid-configuration': {
                                'issuer': base, **{field: base + '/protocol/openid-connect' + suffix
                                    for field, suffix in {'authorization_endpoint': '/auth', 'token_endpoint': '/token',
                                                          'end_session_endpoint': '/logout', 'jwks_uri': '/certs'}.items()}},
                            '/realms/crm-local/protocol/openid-connect/certs': {'keys': [{'kty': 'RSA'}]},
                            '/admin/realms/crm-local': {'realm': 'crm-local', 'enabled': True},
                            '/admin/realms/crm-local/clients': current,
                            '/admin/realms/crm-local/clients/11111111-1111-4111-8111-111111111111/protocol-mappers/models': mappers,
                        }
                        self.reply(paths[self.path])
                    def reply(self, payload):
                        body = json.dumps(payload).encode()
                        self.send_response(200)
                        self.send_header('Content-Length', str(len(body)))
                        self.end_headers()
                        self.wfile.write(body)
                password = Path(directory) / 'password'
                password.write_text('test-only-sentinel')
                password.chmod(0o600)
                with HTTPServer(('127.0.0.1', 0), Peer) as server:
                    thread = threading.Thread(target=server.serve_forever)
                    thread.start()
                    try:
                        result = self.module.inspect_realm(f'http://localhost:{server.server_port}', self.module.AdminSecretFiles(password), 2)
                        self.assertEqual(expected, result.status)
                        self.assertEqual(None if expected == 'ready' else 'LOCAL_REALM_DRIFT', result.reason_token)
                        self.assertEqual(32, len(result.correlation_id))
                    finally:
                        server.shutdown()
                        thread.join()

    def test_untrusted_url_or_secret_is_unavailable_without_network_or_secret_output(self):
        with tempfile.TemporaryDirectory() as directory:
            password = Path(directory) / 'password'
            password.write_text('private-sentinel')
            password.chmod(0o600)
            for url in ('http://example.com:8080', 'http://localhost:8080/evil', 'http://user:password@localhost:8080', 'https://localhost:8080', 'http://localhost:0', 'http://localhost:8080?x=1'):
                result = self.module.inspect_realm(url, self.module.AdminSecretFiles(password), 1)
                self.assertEqual('unavailable', result.status)
                self.assertEqual('LOCAL_IDP_NOT_READY', result.reason_token)
                self.assertEqual(32, len(result.correlation_id))
                self.assertNotIn('private-sentinel', str(result))
            password.chmod(0o644)
            self.assertEqual('unavailable', self.module.inspect_realm('http://localhost:1', self.module.AdminSecretFiles(password), 1).status)

    def test_invalid_http_response_returns_safe_unavailable(self):
        import socketserver
        import threading
        class BrokenPeer(socketserver.BaseRequestHandler):
            def handle(self):
                self.request.recv(4096)
                self.request.sendall(b'not-http\r\n\r\n')
        with tempfile.TemporaryDirectory() as directory:
            password = Path(directory) / 'password'
            password.write_text('private-sentinel')
            password.chmod(0o600)
            with socketserver.TCPServer(('127.0.0.1', 0), BrokenPeer) as peer:
                thread = threading.Thread(target=peer.handle_request)
                thread.start()
                try:
                    try:
                        result = self.module.inspect_realm(f'http://localhost:{peer.server_address[1]}', self.module.AdminSecretFiles(password), 1)
                    except Exception:
                        self.fail('Malformed HTTP escaped the safe status boundary.')
                    self.assertEqual('unavailable', result.status)
                    self.assertEqual('LOCAL_IDP_NOT_READY', result.reason_token)
                finally:
                    thread.join(timeout=2)

    def test_complete_http_body_reaches_metadata_drift_check(self):
        import socketserver
        import threading
        class Peer(socketserver.BaseRequestHandler):
            def handle(self):
                self.request.recv(4096)
                self.request.sendall(b'HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}')
        with tempfile.TemporaryDirectory() as directory:
            password = Path(directory) / 'password'
            password.write_text('private-sentinel')
            password.chmod(0o600)
            with socketserver.TCPServer(('127.0.0.1', 0), Peer) as peer:
                thread = threading.Thread(target=peer.handle_request)
                thread.start()
                try:
                    result = self.module.inspect_realm(f'http://localhost:{peer.server_address[1]}', self.module.AdminSecretFiles(password), 1)
                    self.assertEqual('drift', result.status)
                    self.assertEqual('LOCAL_REALM_DRIFT', result.reason_token)
                finally:
                    thread.join(timeout=2)

    def test_absolute_deadline_closes_trickled_status_headers_and_body(self):
        import socket
        import socketserver
        import threading
        import time
        for phase in ('status', 'headers', 'body'):
            with self.subTest(phase=phase), tempfile.TemporaryDirectory() as directory:
                finished = threading.Event()
                class Peer(socketserver.BaseRequestHandler):
                    def handle(self):
                        try:
                            request = b''
                            while b'\r\n\r\n' not in request:
                                block = self.request.recv(4096)
                                if not block:
                                    return
                                request += block
                            if phase == 'status':
                                prefix, chunks, suffix = b'', [bytes([byte]) for byte in b'HTTP/1.1 200 OK\r\n'], b'Content-Length: 2\r\n\r\n{}'
                            elif phase == 'headers':
                                prefix, chunks, suffix = b'HTTP/1.1 200 OK\r\n', [b'X-Trickle: a\r\n'] * 16, b'Content-Length: 2\r\n\r\n{}'
                            else:
                                prefix, chunks, suffix = b'HTTP/1.1 200 OK\r\nContent-Length: 16\r\n\r\n', [b' '] * 16, b''
                            self.request.sendall(prefix)
                            self.request.settimeout(.04)
                            for chunk in chunks:
                                # An EOF must stop this peer shortly after helper return.
                                try:
                                    if self.request.recv(1, socket.MSG_PEEK) == b'':
                                        return
                                except TimeoutError:
                                    pass
                                self.request.sendall(chunk)
                            self.request.sendall(suffix)
                        except (BrokenPipeError, ConnectionResetError):
                            pass
                        finally:
                            finished.set()
                password = Path(directory) / 'password'
                password.write_text('private-sentinel')
                password.chmod(0o600)
                with socketserver.TCPServer(('127.0.0.1', 0), Peer) as peer:
                    peer.timeout = 1
                    thread = threading.Thread(target=peer.handle_request)
                    thread.start()
                    try:
                        started = time.monotonic()
                        result = self.module.inspect_realm(f'http://localhost:{peer.server_address[1]}', self.module.AdminSecretFiles(password), .2)
                        elapsed = time.monotonic() - started
                        self.assertEqual('unavailable', result.status)
                        self.assertEqual('LOCAL_IDP_NOT_READY', result.reason_token)
                        self.assertLess(elapsed, .4, 'Trickled response exceeded the absolute deadline budget.')
                        self.assertTrue(finished.wait(.15), 'The request socket remained active after helper return.')
                    finally:
                        thread.join(timeout=2)
                        self.assertFalse(thread.is_alive(), 'Test peer did not finish.')
