import importlib.util
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('local_realm', Path(__file__).parents[2] / 'scripts/local/realm.py')

class RealmBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.module = importlib.util.module_from_spec(SPEC)
        SPEC.loader.exec_module(self.module)

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
