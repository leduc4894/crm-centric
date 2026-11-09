"""Finite Docker calls and serialized, project-scoped local lifecycle."""
from contextlib import contextmanager
from dataclasses import dataclass
import fcntl
from http.client import HTTPException
import hashlib
import json
import math
import os
from pathlib import Path
import re
import socket
import stat
import subprocess
import tempfile
import time
import uuid
from local.secrets import ensure_secret_state, NAMES
from local.realm import inspect_realm, AdminSecretFiles, _DeadlineConnection
from contextlib import closing

class LocalFailure(Exception):
    def __init__(self, token):
        self.token = token
        super().__init__(token)

@dataclass(frozen=True)
class LocalStackConfig:
    root: Path
    project: str
    state_dir: Path
    api_port: int = 5080
    idp_port: int = 8080
    start_timeout: float = 180
    build_timeout: float = 900

    @classmethod
    def from_environment(cls):
        root = Path(__file__).resolve().parents[2]
        project = os.environ.get('LOCAL_STACK_PROJECT', 'crm-local-' + hashlib.sha256(str(root).encode()).hexdigest()[:12])
        state = Path(os.environ.get('LOCAL_STACK_STATE_DIR', str(root / '.local/stack' / project)))
        try:
            config = cls(root, project, state, int(os.environ.get('LOCAL_API_PORT', '5080')), int(os.environ.get('LOCAL_IDP_PORT', '8080')),
                         float(os.environ.get('LOCAL_START_TIMEOUT_SECONDS', '180')), float(os.environ.get('LOCAL_BUILD_TIMEOUT_SECONDS', '900')))
            config.validate()
            return config
        except (ValueError, OSError):
            raise LocalFailure('LOCAL_CONFIGURATION_INVALID') from None

    def validate(self):
        if (not re.fullmatch(r'[a-z0-9][a-z0-9_-]{0,62}', self.project) or
                not self.state_dir.is_absolute() or str(self.state_dir) != os.path.abspath(self.state_dir) or
                any(p.is_symlink() for p in (self.state_dir, *self.state_dir.parents)) or
                self.state_dir.resolve() != self.state_dir or
                (self.state_dir.is_relative_to(self.root) and
                 (not self.state_dir.is_relative_to(self.root / '.local') or self.state_dir == self.root / '.local')) or
                self.api_port == self.idp_port or any(p < 1 or p > 65535 for p in (self.api_port, self.idp_port)) or
                any(not math.isfinite(t) or t <= 0 or t > 3600 for t in (self.start_timeout, self.build_timeout)) or
                os.getuid() == 0 or os.getgid() == 0):
            raise LocalFailure('LOCAL_CONFIGURATION_INVALID')

class Docker:
    def __init__(self, config):
        self.config = config
        self.env = os.environ.copy()
        for line in (config.root / 'infra/compose/images.lock.env').read_text().splitlines():
            if line and not line.startswith('#'):
                key, value = line.split('=', 1)
                self.env[key] = value
        self.env.update(LOCAL_STACK_PROJECT=config.project, LOCAL_STACK_STATE_DIR=str(config.state_dir),
                        LOCAL_API_PORT=str(config.api_port), LOCAL_IDP_PORT=str(config.idp_port),
                        LOCAL_UID=str(os.getuid()), LOCAL_GID=str(os.getgid()))

    def call(self, args, timeout=30, token='LOCAL_SMOKE_FAILED', check=True):
        try:
            result = subprocess.run(['docker', *args], cwd=self.config.root, env=self.env,
                                    capture_output=True, timeout=max(0.01, timeout), text=True)
        except FileNotFoundError:
            raise LocalFailure('LOCAL_PREREQUISITE_MISSING') from None
        except subprocess.TimeoutExpired:
            raise LocalFailure(token) from None
        if check and result.returncode:
            raise LocalFailure(token)
        return result

    def compose(self, args, **kwargs):
        return self.call(['compose', '--project-name', self.config.project, '--file', str(self.config.root / 'compose.yaml'), *args], **kwargs)

    def containers(self, deadline=None):
        def budget():
            return min(30, _remaining(deadline)) if deadline is not None else 30
        ids = self.call(['ps', '-aq', '--filter', 'label=com.docker.compose.project=' + self.config.project], timeout=budget()).stdout.split()
        if not ids:
            return []
        try:
            return json.loads(self.call(['inspect', *ids], timeout=budget()).stdout)
        except (ValueError, KeyError):
            raise LocalFailure('LOCAL_SMOKE_FAILED') from None

    def volumes_exist(self):
        return bool(self.call(['volume', 'ls', '-q', '--filter', 'label=com.docker.compose.project=' + self.config.project]).stdout.strip())

@contextmanager
def project_lock(docker):
    context = docker.call(['context', 'show'], token='LOCAL_PREREQUISITE_MISSING').stdout.strip()
    # Bind the lock to the daemon endpoint, so alias contexts cannot bypass it.
    try:
        endpoint = json.loads(docker.call(['context', 'inspect', context]).stdout)[0]['Endpoints']['docker']['Host']
    except (ValueError, KeyError, IndexError):
        raise LocalFailure('LOCAL_CONFIGURATION_INVALID') from None
    if not endpoint.startswith('unix://') or os.environ.get('DOCKER_HOST', endpoint) != endpoint:
        raise LocalFailure('LOCAL_CONFIGURATION_INVALID')
    base = Path('/tmp') / f'crm-centric-{os.getuid()}'
    for directory in (base, base / 'locks'):
        try:
            directory.mkdir(mode=0o700, exist_ok=True)
            info = directory.lstat()
            if not stat.S_ISDIR(info.st_mode) or stat.S_IMODE(info.st_mode) != 0o700 or info.st_uid != os.getuid():
                raise LocalFailure('LOCAL_CONFIGURATION_INVALID')
        except OSError:
            raise LocalFailure('LOCAL_CONFIGURATION_INVALID') from None
    name = hashlib.sha256((endpoint + '\0' + docker.config.project).encode()).hexdigest()
    try:
        fd = os.open(base / 'locks' / name, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    except OSError:
        raise LocalFailure('LOCAL_CONFIGURATION_INVALID') from None
    try:
        info = os.fstat(fd)
        if not stat.S_ISREG(info.st_mode) or stat.S_IMODE(info.st_mode) != 0o600 or info.st_uid != os.getuid():
            raise LocalFailure('LOCAL_CONFIGURATION_INVALID')
        try:
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise LocalFailure('LOCAL_LIFECYCLE_BUSY') from None
        yield
    finally:
        os.close(fd)

_SECRET_GRANTS = {
    'crm-db': {'crm_admin_password': 'crm_admin_password'},
    'crm-bootstrap': {'crm_admin_connection': 'crm_admin_connection',
                      'crm_migrator_password': 'crm_migrator_password', 'crm_app_password': 'crm_app_password'},
    'crm-migrate': {'crm_migrator_connection': 'platform_connection'},
    'crm-api': {'crm_app_connection': 'platform_connection'},
    'idp-db': {'idp_db_admin_password': 'idp_db_admin_password', 'idp_db_password': 'idp_db_password'},
    'keycloak': {'idp_db_password': 'idp_db_password', 'idp_admin_password': 'idp_admin_password'}
}

def _owned(config, containers):
    result = {}
    for container in containers:
        labels = container['Config']['Labels']
        if (labels.get('com.docker.compose.project') != config.project or
                labels.get('com.docker.compose.project.working_dir') != str(config.root) or
                labels.get('com.docker.compose.project.config_files') != str(config.root / 'compose.yaml') or
                labels.get('com.docker.compose.oneoff') != 'False'):
            raise LocalFailure('LOCAL_CONFIGURATION_INVALID')
        service = labels.get('com.docker.compose.service')
        if service not in ('crm-db', 'crm-bootstrap', 'crm-migrate', 'crm-api', 'idp-db', 'keycloak') or service in result:
            raise LocalFailure('LOCAL_CONFIGURATION_INVALID')
        expected = {(str(config.state_dir / source), '/run/secrets/' + target)
                    for source, target in _SECRET_GRANTS[service].items()}
        actual = []
        for mount in container['Mounts']:
            source, destination = mount.get('Source', ''), mount.get('Destination', '')
            # The stack has no non-secret bind mounts. Also reject secret mounts
            # hidden under other destinations or supplied as volumes/directories.
            if (mount.get('Type') == 'bind' or destination == '/run/secrets' or
                    destination.startswith('/run/secrets/') or Path(source) == config.state_dir or
                    Path(source).parent == config.state_dir):
                if mount.get('Type') != 'bind' or mount.get('RW') is not False:
                    raise LocalFailure('LOCAL_SECRET_STATE_MISMATCH')
                actual.append((source, destination))
        if len(actual) != len(expected) or set(actual) != expected:
            raise LocalFailure('LOCAL_SECRET_STATE_MISMATCH')
        result[service] = container
    return result

def _remaining(deadline):
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise LocalFailure('LOCAL_START_TIMEOUT')
    return remaining

def _ports(config, owned):
    for service, port in (('crm-api', config.api_port), ('keycloak', config.idp_port)):
        container = owned.get(service)
        if container and container['State']['Running']:
            bindings = container['NetworkSettings']['Ports'].get('8080/tcp')
            if bindings != [{'HostIp': '127.0.0.1', 'HostPort': str(port)}]:
                raise LocalFailure('LOCAL_PORT_CONFLICT')
        else:
            try:
                with socket.socket() as probe:
                    probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                    probe.bind(('127.0.0.1', port))
            except OSError:
                raise LocalFailure('LOCAL_PORT_CONFLICT') from None

def inspect_stack(docker, deadline):
    config = docker.config
    owned = _owned(config, docker.containers(deadline))
    for service in ('crm-db', 'crm-bootstrap', 'crm-migrate', 'crm-api', 'idp-db', 'keycloak'):
        if service not in owned:
            raise LocalFailure('LOCAL_SMOKE_FAILED')
        state = owned[service]['State']
        if service in ('crm-bootstrap', 'crm-migrate'):
            if state['Status'] != 'exited' or state['ExitCode'] != 0:
                raise LocalFailure('LOCAL_BOOTSTRAP_FAILED' if service == 'crm-bootstrap' else 'LOCAL_MIGRATION_FAILED')
        elif not state['Running'] or state.get('Health', {}).get('Status') != 'healthy':
            raise LocalFailure('LOCAL_IDP_NOT_READY' if service in ('idp-db', 'keycloak') else 'LOCAL_SMOKE_FAILED')
    _ports(config, owned)
    for service in ('crm-api', 'keycloak'):
        probe = '/usr/local/bin/healthcheck.sh' if service == 'crm-api' else '/usr/local/bin/keycloak-healthcheck.sh'
        docker.call(['exec', owned[service]['Id'], probe], timeout=min(6, _remaining(deadline)), token='LOCAL_IDP_NOT_READY' if service == 'keycloak' else 'LOCAL_SMOKE_FAILED')
    for path in ('/health/live', '/health/ready'):
        try:
            with closing(_DeadlineConnection(config.api_port, time.monotonic() + min(4, _remaining(deadline)))) as connection:
                connection.request('GET', path, headers={'Connection': 'close'})
                with connection.getresponse() as response:
                    if response.status != 200:
                        raise LocalFailure('LOCAL_SMOKE_FAILED')
        except (OSError, HTTPException):
            raise LocalFailure('LOCAL_SMOKE_FAILED') from None
    realm = inspect_realm(f'http://localhost:{config.idp_port}', AdminSecretFiles(config.state_dir / 'idp_admin_password'), min(15, _remaining(deadline)))
    if realm.reason_token:
        raise LocalFailure(realm.reason_token)

def _event(event, **fields):
    print(json.dumps(dict(service='local-stack', eventName=event, correlationId=uuid.uuid4().hex, **fields)), flush=True)

def run_local_stack(action: str, config: LocalStackConfig) -> int:
    config.validate()
    if action not in ('up', 'down', 'smoke'):
        raise LocalFailure('LOCAL_CONFIGURATION_INVALID')
    docker = Docker(config)
    docker.call(['info', '--format', '{{.ServerVersion}}'], token='LOCAL_PREREQUISITE_MISSING')
    docker.compose(['version'], token='LOCAL_PREREQUISITE_MISSING')
    with project_lock(docker):
        owned = _owned(config, docker.containers())
        existing_volumes = docker.volumes_exist()
        read_stack_metadata(config, required=existing_volumes)
        if action == 'down':
            docker.compose(['down', '--timeout', '20'], timeout=45)
            _event('stack_stopped', status='stopped', project=config.project)
            return 0
        if action == 'smoke':
            if not owned:
                raise LocalFailure('LOCAL_SMOKE_FAILED')
            if not config.state_dir.exists():
                raise LocalFailure('LOCAL_SECRET_STATE_MISMATCH')
            ensure_secret_state(config.state_dir, config.project, existing_volumes)
            inspect_stack(docker, time.monotonic() + config.start_timeout)
        else:
            ensure_secret_state(config.state_dir, config.project, existing_volumes)
            write_stack_metadata(config, 'pending')
            _ports(config, owned)
            started = time.monotonic()
            docker.compose(['build'], timeout=config.build_timeout, token='LOCAL_START_TIMEOUT')
            _event('build_completed', status='built', seconds=round(time.monotonic() - started, 3), project=config.project)
            deadline = time.monotonic() + config.start_timeout
            # Never let an old ready API hide a failed bootstrap/migration.
            docker.compose(['rm', '--stop', '--force', 'crm-api', 'crm-migrate', 'crm-bootstrap'], timeout=min(30, _remaining(deadline)))
            docker.compose(['up', '-d', '--wait', '--wait-timeout', str(max(1, int(_remaining(deadline)))), 'crm-db', 'idp-db'], timeout=_remaining(deadline), token='LOCAL_START_TIMEOUT')
            for service, token in (('crm-bootstrap', 'LOCAL_BOOTSTRAP_FAILED'), ('crm-migrate', 'LOCAL_MIGRATION_FAILED')):
                docker.compose(['up', '--no-deps', '--force-recreate', '--abort-on-container-exit', '--exit-code-from', service, service], timeout=_remaining(deadline), token=token)
            docker.compose(['up', '-d', '--no-deps', 'crm-api', 'keycloak'], timeout=_remaining(deadline), token='LOCAL_PORT_CONFLICT')
            while True:
                try:
                    inspect_stack(docker, deadline)
                    break
                except LocalFailure as error:
                    if error.token in ('LOCAL_REALM_DRIFT', 'LOCAL_CONFIGURATION_INVALID', 'LOCAL_SECRET_STATE_MISMATCH', 'LOCAL_PORT_CONFLICT'):
                        raise
                    remaining = _remaining(deadline)
                    time.sleep(min(1, remaining))
            write_stack_metadata(config, 'ready')
            _event('startup_completed', status='ready', seconds=round(config.start_timeout - _remaining(deadline), 3), project=config.project)
        _event('stack_ready', status='ready', project=config.project, api=f'http://localhost:{config.api_port}', issuer=f'http://localhost:{config.idp_port}/realms/crm-local')
        return 0


def _metadata_identity(config):
    return dict(version=1, project=config.project, checkout=str(config.root),
                stateDirectory=str(config.state_dir), apiPort=config.api_port,
                idpPort=config.idp_port, secretFiles=list(NAMES))

def read_stack_metadata(config, required=False):
    path = Path(str(config.state_dir) + '.stack.json')
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    except FileNotFoundError:
        if required:
            raise LocalFailure('LOCAL_SECRET_STATE_MISMATCH') from None
        return None
    except OSError:
        raise LocalFailure('LOCAL_CONFIGURATION_INVALID') from None
    try:
        with os.fdopen(fd, 'rb') as stream:
            info = os.fstat(stream.fileno())
            if not stat.S_ISREG(info.st_mode) or stat.S_IMODE(info.st_mode) != 0o600 or info.st_uid != os.getuid():
                raise LocalFailure('LOCAL_CONFIGURATION_INVALID')
            data = stream.read(16385)
            if len(data) > 16384:
                raise LocalFailure('LOCAL_CONFIGURATION_INVALID')
            value = json.loads(data)
        if (not isinstance(value, dict) or value.get('provisioning') not in ('pending', 'ready') or
                {k: v for k, v in value.items() if k != 'provisioning'} != _metadata_identity(config)):
            raise LocalFailure('LOCAL_CONFIGURATION_INVALID')
        return value
    except (OSError, ValueError, TypeError):
        raise LocalFailure('LOCAL_CONFIGURATION_INVALID') from None

def write_stack_metadata(config, provisioning):
    if provisioning not in ('pending', 'ready'):
        raise LocalFailure('LOCAL_CONFIGURATION_INVALID')
    existing = read_stack_metadata(config)
    path = Path(str(config.state_dir) + '.stack.json')
    temporary = None
    try:
        path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        fd, temporary = tempfile.mkstemp(prefix='.stack-', dir=path.parent)
        with os.fdopen(fd, 'w') as stream:
            json.dump(dict(_metadata_identity(config), provisioning=provisioning), stream, sort_keys=True)
            stream.flush(); os.fsync(stream.fileno())
        if existing is None:
            os.link(temporary, path, follow_symlinks=False)
            os.unlink(temporary)
        else:
            os.replace(temporary, path)
        temporary = None
        parent = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try: os.fsync(parent)
        finally: os.close(parent)
    except OSError:
        raise LocalFailure('LOCAL_CONFIGURATION_INVALID') from None
    finally:
        if temporary is not None:
            os.unlink(temporary)
