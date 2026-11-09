"""Real CLI validation/OS-lock tests; no mocked Docker commands."""
import json
import copy
import os
from pathlib import Path
import subprocess
import sys
import socket
import tempfile
import unittest
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from local.compose import Docker, LocalStackConfig, project_lock, _ports, LocalFailure, read_stack_metadata, write_stack_metadata, _owned

class LifecycleConfigurationTests(unittest.TestCase):
    def cli(self, action, **environment):
        env = dict(os.environ, **environment)
        return subprocess.run(['bash', str(ROOT / f'scripts/dev-{action}.sh')], env=env,
                              capture_output=True, text=True, timeout=10)

    def test_owned_container_requires_exact_readonly_secret_grants_for_every_service(self):
        config = LocalStackConfig(ROOT, 'crm-mount-validation', Path('/tmp/crm-owned-state'))
        # Hand-checked against the six consumers' documented credential boundaries.
        grants = {
            'crm-db': [('crm_admin_password', 'crm_admin_password')],
            'crm-bootstrap': [('crm_admin_connection', 'crm_admin_connection'),
                              ('crm_migrator_password', 'crm_migrator_password'), ('crm_app_password', 'crm_app_password')],
            'crm-migrate': [('crm_migrator_connection', 'platform_connection')],
            'crm-api': [('crm_app_connection', 'platform_connection')],
            'idp-db': [('idp_db_admin_password', 'idp_db_admin_password'), ('idp_db_password', 'idp_db_password')],
            'keycloak': [('idp_db_password', 'idp_db_password'), ('idp_admin_password', 'idp_admin_password')]
        }
        containers = []
        for service, pairs in grants.items():
            mounts = [dict(Type='bind', Source=str(config.state_dir / source),
                           Destination='/run/secrets/' + target, RW=False) for source, target in pairs]
            if service in ('crm-db', 'idp-db'):
                mounts.append(dict(Type='volume', Source='/var/lib/docker/volumes/test/_data',
                                   Destination='/var/lib/postgresql', RW=True))
            containers.append({'Config': {'Labels': {
                'com.docker.compose.project': config.project, 'com.docker.compose.service': service,
                'com.docker.compose.project.working_dir': str(ROOT),
                'com.docker.compose.project.config_files': str(ROOT / 'compose.yaml'),
                'com.docker.compose.oneoff': 'False'}}, 'Mounts': mounts})
        self.assertEqual(set(grants), set(_owned(config, containers)))
        api = next(c for c in containers if c['Config']['Labels']['com.docker.compose.service'] == 'crm-api')
        original = api['Mounts'][0]
        cases = {
            'wrong file in correct directory': [dict(original, Source=str(config.state_dir / 'crm_admin_connection'))],
            'missing mount': [],
            'extra credential': [original, dict(original, Source=str(config.state_dir / 'idp_admin_password'), Destination='/run/secrets/idp_admin_password')],
            'writable mount': [dict(original, RW=True)],
            'wrong destination': [dict(original, Destination='/run/secrets/other')],
            'wrong mount type': [dict(original, Type='volume')],
            'duplicate mount': [original, original],
            'credential outside secret directory': [original, dict(original, Source=str(config.state_dir / 'crm_admin_connection'), Destination='/tmp/admin')]
        }
        for name, mounts in cases.items():
            with self.subTest(case=name):
                changed = copy.deepcopy(containers)
                next(c for c in changed if c['Config']['Labels']['com.docker.compose.service'] == 'crm-api')['Mounts'] = mounts
                with self.assertRaises(LocalFailure) as error:
                    _owned(config, changed)
                self.assertEqual('LOCAL_SECRET_STATE_MISMATCH', error.exception.token)
        for service in grants:
            with self.subTest(service=service):
                changed = copy.deepcopy(containers)
                target = next(c for c in changed if c['Config']['Labels']['com.docker.compose.service'] == service)
                target['Mounts'][0]['Source'] = str(config.state_dir / 'ungranted_credential')
                with self.assertRaises(LocalFailure):
                    _owned(config, changed)

    def test_invalid_configuration_closes_without_creating_state(self):
        with tempfile.TemporaryDirectory() as parent:
            state = Path(parent) / 'absent'
            for overrides in ({'LOCAL_STACK_PROJECT': '../foreign'}, {'LOCAL_API_PORT': '0'},
                              {'LOCAL_START_TIMEOUT_SECONDS': 'nan'}, {'LOCAL_BUILD_TIMEOUT_SECONDS': 'inf'},
                              {'LOCAL_STACK_STATE_DIR': 'relative/path'}, {'LOCAL_IDP_PORT': '5080'}):
                env = {'LOCAL_STACK_STATE_DIR': str(state), 'LOCAL_STACK_PROJECT': 'test-invalid',
                       'LOCAL_API_PORT': '5080', 'LOCAL_IDP_PORT': '8080', **overrides}
                result = self.cli('up', **env)
                self.assertEqual(1, result.returncode)
                diagnostic = json.loads(result.stderr)
                self.assertEqual('LOCAL_CONFIGURATION_INVALID', diagnostic['reasonToken'])
                self.assertEqual(32, len(diagnostic['correlationId']))
                self.assertFalse(state.exists())

    def test_state_cannot_enter_build_context_or_escape_local_ignore_for_metadata(self):
        for state in (ROOT / 'src/Platform.Infrastructure/local-secrets', ROOT / '.local'):
            with self.assertRaises(LocalFailure):
                LocalStackConfig(ROOT, 'crm-unsafe-context', state).validate()
        LocalStackConfig(ROOT, 'crm-safe-context', ROOT / '.local/stack/safe').validate()

    def test_symlink_loop_configuration_fails_with_safe_diagnostic(self):
        with tempfile.TemporaryDirectory() as parent:
            loop = Path(parent) / 'loop'
            loop.symlink_to(loop)
            result = self.cli('up', LOCAL_STACK_PROJECT='crm-invalid-path', LOCAL_STACK_STATE_DIR=str(loop))
            self.assertEqual(1, result.returncode)
            try:
                diagnostic = json.loads(result.stderr)
            except json.JSONDecodeError:
                self.fail('Configuration error escaped the safe JSON boundary.')
            self.assertEqual('LOCAL_CONFIGURATION_INVALID', diagnostic['reasonToken'])

    def test_stack_metadata_preserves_ports_and_provisioning_without_rewriting_secrets(self):
        with tempfile.TemporaryDirectory() as parent:
            state = Path(parent) / 'secrets'
            config = LocalStackConfig(ROOT, 'crm-metadata', state, 5201, 8201)
            write_stack_metadata(config, 'pending')
            self.assertIsNotNone(read_stack_metadata(config), 'Lifecycle metadata was not persisted.')
            self.assertEqual('pending', read_stack_metadata(config)['provisioning'])
            self.assertFalse(state.exists())
            write_stack_metadata(config, 'ready')
            metadata = Path(str(state) + '.stack.json')
            self.assertEqual(0o600, metadata.stat().st_mode & 0o777)
            self.assertEqual('ready', read_stack_metadata(config)['provisioning'])
            result = self.cli('down', LOCAL_STACK_PROJECT='crm-metadata', LOCAL_STACK_STATE_DIR=str(state),
                              LOCAL_API_PORT='5202', LOCAL_IDP_PORT='8201')
            self.assertEqual(1, result.returncode)
            self.assertEqual('LOCAL_CONFIGURATION_INVALID', json.loads(result.stderr)['reasonToken'])
            self.assertEqual(5201, json.loads(metadata.read_text())['apiPort'])

    def test_stack_metadata_rejects_foreign_files_and_symlinks(self):
        with tempfile.TemporaryDirectory() as parent:
            state = Path(parent) / 'secrets'
            config = LocalStackConfig(ROOT, 'crm-metadata', state)
            metadata = Path(str(state) + '.stack.json')
            foreign = Path(parent) / 'foreign'
            foreign.write_text('foreign')
            metadata.symlink_to(foreign)
            with self.assertRaises(LocalFailure):
                write_stack_metadata(config, 'pending')
            self.assertEqual('foreign', foreign.read_text())
            metadata.unlink(); metadata.write_text('{}'); metadata.chmod(0o600)
            with self.assertRaises(LocalFailure):
                write_stack_metadata(config, 'pending')
            self.assertEqual('{}', metadata.read_text())
            metadata.unlink(); os.mkfifo(metadata, 0o600)
            try:
                result = self.cli('down', LOCAL_STACK_PROJECT='crm-metadata', LOCAL_STACK_STATE_DIR=str(state))
            except subprocess.TimeoutExpired:
                self.fail('Nonregular metadata blocked lifecycle indefinitely.')
            self.assertEqual(1, result.returncode)
            self.assertEqual('LOCAL_CONFIGURATION_INVALID', json.loads(result.stderr)['reasonToken'])

    def test_smoke_of_absent_stack_does_not_generate_credentials(self):
        with tempfile.TemporaryDirectory() as parent:
            state = Path(parent) / 'absent'
            result = self.cli('smoke', LOCAL_STACK_PROJECT='crm-smoke-' + uuid.uuid4().hex,
                              LOCAL_STACK_STATE_DIR=str(state))
            self.assertEqual(1, result.returncode)
            self.assertEqual('LOCAL_SMOKE_FAILED', json.loads(result.stderr)['reasonToken'])
            self.assertFalse(state.exists(), 'Read-only smoke generated credentials.')

    def test_recently_closed_owned_listener_does_not_block_restart(self):
        with socket.socket() as server, socket.socket() as other:
            server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            server.bind(('127.0.0.1', 0)); server.listen()
            port = server.getsockname()[1]
            other.bind(('127.0.0.1', 0)); other_port = other.getsockname()[1]
            with self.assertRaises(LocalFailure) as conflict:
                _ports(LocalStackConfig(ROOT, 'test-live', Path('/tmp/not-created'), port, other_port), {})
            self.assertEqual('LOCAL_PORT_CONFLICT', conflict.exception.token)
            with socket.create_connection(('127.0.0.1', port)) as client:
                accepted, _ = server.accept()
                accepted.close()  # Server-side TIME_WAIT remains after active close.
                self.assertEqual(b'', client.recv(1))
            server.close(); other.close()
            config = LocalStackConfig(ROOT, 'test-restart', Path('/tmp/not-created'), port, other_port)
            try:
                _ports(config, {})
            except LocalFailure:
                self.fail("A closed listener was incorrectly classified as a live port conflict.")

    def test_project_lock_covers_all_commands_across_state_directories(self):
        with tempfile.TemporaryDirectory() as parent:
            project = 'crm-lock-' + uuid.uuid4().hex
            config = LocalStackConfig(ROOT, project, Path(parent) / 'first')
            with project_lock(Docker(config)):
                for action in ('up', 'down', 'smoke'):
                    result = self.cli(action, LOCAL_STACK_PROJECT=project, LOCAL_STACK_STATE_DIR=str(Path(parent) / 'second'))
                    self.assertEqual(1, result.returncode)
                    self.assertEqual('LOCAL_LIFECYCLE_BUSY', json.loads(result.stderr)['reasonToken'])
            # A released OS lock is immediately reusable, independent of state.
            with project_lock(Docker(LocalStackConfig(ROOT, project, Path(parent) / 'second'))):
                self.assertFalse((Path(parent) / 'first').exists())
                self.assertFalse((Path(parent) / 'second').exists())

if __name__ == '__main__': unittest.main()
