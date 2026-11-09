import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('local_secrets', Path(__file__).parents[2] / 'scripts/local/secrets.py')

class SecretStateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / 'state'
        self.module = importlib.util.module_from_spec(SPEC)
        SPEC.loader.exec_module(self.module)

    def test_generation_reuse_permissions_and_no_values_in_manifest(self):
        state = self.module.ensure_secret_state(self.path, 'crm-test', False)
        before = {k: p.read_bytes() for k, p in state.files.items()}
        self.assertEqual(8, len(before))
        self.assertEqual(0o700, self.path.stat().st_mode & 0o777)
        for p in [*state.files.values(), self.path / 'manifest.json']:
            self.assertEqual(0o600, p.stat().st_mode & 0o777)
        self.module.ensure_secret_state(self.path, 'crm-test', True)
        self.assertTrue(all(before[k] == p.read_bytes() for k, p in state.files.items()), 'Secret changed during warm start.')
        manifest = (self.path / 'manifest.json').read_text()
        self.assertEqual(1, json.loads(manifest)['version'])
        for key in ('crm_admin_password', 'crm_migrator_password', 'crm_app_password', 'idp_db_password', 'idp_admin_password'):
            self.assertGreaterEqual(len(before[key]), 40)
            self.assertNotIn(before[key].decode(), manifest)
        self.assertEqual(5, len({before[k] for k in before if k.endswith('_password')}))

    def test_existing_volume_missing_or_changed_state_fails_without_rewrite(self):
        with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
            self.module.ensure_secret_state(self.path, 'crm-test', True)
        state = self.module.ensure_secret_state(self.path, 'crm-test', False)
        file = state.files['crm_app_password']
        file.write_text('changed')
        with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
            self.module.ensure_secret_state(self.path, 'crm-test', True)
        self.assertEqual('changed', file.read_text())
        file.unlink()
        with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
            self.module.ensure_secret_state(self.path, 'crm-test', False)

    def test_foreign_directory_project_permissions_and_symlinks_rejected(self):
        self.path.mkdir(mode=0o700)
        with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
            self.module.ensure_secret_state(self.path, 'crm-test', False)
        self.path.rmdir()
        state = self.module.ensure_secret_state(self.path, 'crm-test', False)
        with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
            self.module.ensure_secret_state(self.path, 'other-project', False)
        file = state.files['crm_app_password']
        file.chmod(0o644)
        with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
            self.module.ensure_secret_state(self.path, 'crm-test', False)
        file.chmod(0o600)
        target = Path(self.temp.name) / 'target'
        file.rename(target)
        file.symlink_to(target)
        with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
            self.module.ensure_secret_state(self.path, 'crm-test', False)
        alias = Path(self.temp.name) / 'alias'
        alias.symlink_to(self.path, target_is_directory=True)
        with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
            self.module.ensure_secret_state(alias / 'nested', 'crm-test', False)

    def test_interrupted_generation_never_publishes_partial_state(self):
        original_write = self.module._write
        count = 0
        def fail_after_first(path, data):
            nonlocal count
            count += 1
            if count == 2:
                raise OSError('injected write failure')
            original_write(path, data)
        with patch.object(self.module, '_write', side_effect=fail_after_first):
            with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
                self.module.ensure_secret_state(self.path, 'crm-test', False)
        self.assertFalse(self.path.exists())
        self.assertEqual([], list(Path(self.temp.name).iterdir()))
        self.module.ensure_secret_state(self.path, 'crm-test', False)

    def test_foreign_owner_is_rejected(self):
        self.module.ensure_secret_state(self.path, 'crm-test', False)
        import subprocess
        result = subprocess.run(['docker', 'run', '--rm', '--mount',
            f'type=bind,src={self.path},dst=/state',
            'postgres:18.6-bookworm@sha256:f1ea2a1a4fb7b68d4836be1fdcb8a6466a89b072158bf9d97663600538c294c5',
            'chown', '999:999', '/state/crm_app_password'], capture_output=True)
        self.assertEqual(0, result.returncode, 'Filesystem owner fixture failed.')
        try:
            with self.assertRaisesRegex(Exception, 'LOCAL_SECRET_STATE_MISMATCH'):
                self.module.ensure_secret_state(self.path, 'crm-test', False)
        finally:
            subprocess.run(['docker', 'run', '--rm', '--mount',
                f'type=bind,src={self.path},dst=/state',
                'postgres:18.6-bookworm@sha256:f1ea2a1a4fb7b68d4836be1fdcb8a6466a89b072158bf9d97663600538c294c5',
                'chown', f'{os.getuid()}:{os.getgid()}', '/state/crm_app_password'], capture_output=True, check=True)

    def test_atomic_publish_cannot_replace_even_an_empty_foreign_directory(self):
        source = Path(self.temp.name) / 'temporary'
        source.mkdir(mode=0o700)
        (source / 'private').write_text('fixture')
        self.path.mkdir(mode=0o700)
        with self.assertRaises(OSError):
            self.module._publish(source, self.path)
        self.assertEqual([], list(self.path.iterdir()))
        self.assertTrue(source.exists())

    def test_helper_can_be_imported_from_lifecycle_script_directory(self):
        import subprocess
        import sys
        result = subprocess.run([sys.executable, '-c',
            'from pathlib import Path; from secrets import ensure_secret_state; import sys; ensure_secret_state(Path(sys.argv[1]), "crm-test", False)', str(self.path)],
            cwd=SPEC.origin.rsplit('/', 1)[0], capture_output=True)
        self.assertEqual(0, result.returncode, 'Local helper import collided with standard-library module.')
