import importlib.util
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('local_tls', Path(__file__).parents[2] / 'scripts/local/tls.py')

class TlsStateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / 'tls'
        self.module = importlib.util.module_from_spec(SPEC)
        SPEC.loader.exec_module(self.module)

    def test_create_reuse_and_detect_modified_state(self):
        cert, key = self.module.ensure_tls_state(self.path, 'crm-test', allow_create=True)
        before = cert.read_bytes(), key.read_bytes()
        self.assertEqual(0o700, self.path.stat().st_mode & 0o777)
        self.assertEqual(0o600, key.stat().st_mode & 0o777)
        self.assertEqual((cert, key), self.module.ensure_tls_state(self.path, 'crm-test', allow_create=False))
        self.assertEqual(before, (cert.read_bytes(), key.read_bytes()))
        key.write_text('altered')
        with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
            self.module.ensure_tls_state(self.path, 'crm-test', allow_create=False)
        self.assertEqual('altered', key.read_text())

    def test_missing_state_is_not_regenerated_after_migration(self):
        with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
            self.module.ensure_tls_state(self.path, 'crm-test', allow_create=False)
        self.assertFalse(self.path.exists())

    def test_project_and_symlink_mismatch_fail_closed(self):
        self.module.ensure_tls_state(self.path, 'crm-test', allow_create=True)
        with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
            self.module.ensure_tls_state(self.path, 'other-project', allow_create=False)
        cert = self.path / 'cert.pem'
        cert.unlink()
        cert.symlink_to('/etc/hosts')
        with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
            self.module.ensure_tls_state(self.path, 'crm-test', allow_create=False)
