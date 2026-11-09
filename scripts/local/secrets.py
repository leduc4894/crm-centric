"""Private, persistent local credentials. Callers serialize lifecycle operations.

The state directory is published only after all files and the v1 manifest are
fsynced. Existing directories are validated and never repaired or overwritten.
"""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import tempfile
from typing import NamedTuple

PASSWORDS = ('crm_admin_password', 'crm_migrator_password', 'crm_app_password',
             'idp_db_password', 'idp_admin_password')
CONNECTIONS = {'crm_admin_connection': ('postgres', 'postgres', 'crm_admin_password'),
               'crm_migrator_connection': ('crm', 'crm_migrator', 'crm_migrator_password'),
               'crm_app_connection': ('crm', 'crm_app', 'crm_app_password')}
NAMES = (*PASSWORDS, *CONNECTIONS)

class SecretState(NamedTuple):
    directory: Path
    project: str
    files: dict[str, Path]

class SecretStateMismatch(RuntimeError):
    def __init__(self):
        super().__init__('LOCAL_SECRET_STATE_MISMATCH')

def _private(path: Path, directory: bool = False):
    info = path.lstat()
    if info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) != (0o700 if directory else 0o600):
        raise SecretStateMismatch()
    if not (stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode)):
        raise SecretStateMismatch()

def _write(path: Path, data: bytes):
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, 'wb') as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())

def _publish(source: Path, destination: Path):
    # Linux renameat2(RENAME_NOREPLACE) publishes a whole directory atomically
    # and refuses every existing target, including an empty foreign directory.
    libc = ctypes.CDLL(None, use_errno=True)
    rename = libc.renameat2
    rename.argtypes = (ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint)
    rename.restype = ctypes.c_int
    if rename(-100, os.fsencode(source), -100, os.fsencode(destination), 1):
        raise OSError(ctypes.get_errno(), 'Atomic secret publication failed.')

def ensure_secret_state(state_dir: Path, project: str, existing_volumes: bool) -> SecretState:
    """Return fixed secret-file paths; never print or rotate credential values."""
    state_dir = Path(os.path.abspath(state_dir))
    temporary = None
    try:
        if os.getuid() == 0:
            raise SecretStateMismatch()
        if not re.fullmatch(r'[a-z0-9][a-z0-9_-]{0,62}', project):
            raise SecretStateMismatch()
        for parent in (state_dir, *state_dir.parents):
            if parent.is_symlink():
                raise SecretStateMismatch()
        files = {name: state_dir / name for name in NAMES}
        if state_dir.exists():
            _private(state_dir, True)
            if set(p.name for p in state_dir.iterdir()) != {*NAMES, 'manifest.json'}:
                raise SecretStateMismatch()
            for file in [*files.values(), state_dir / 'manifest.json']:
                _private(file)
            manifest = json.loads((state_dir / 'manifest.json').read_text())
            hashes = {name: hashlib.sha256(file.read_bytes()).hexdigest() for name, file in files.items()}
            if manifest != {'version': 1, 'project': project, 'sha256': hashes} or any(not p.stat().st_size for p in files.values()):
                raise SecretStateMismatch()
            return SecretState(state_dir, project, files)
        if existing_volumes:
            raise SecretStateMismatch()
        # Parent is a lifecycle-owned directory; never chmod an existing parent.
        state_dir.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        temporary = Path(tempfile.mkdtemp(prefix='.secrets-', dir=state_dir.parent))
        values = {name: os.urandom(32).hex().encode('ascii') for name in PASSWORDS}
        for name, (database, role, password) in CONNECTIONS.items():
            values[name] = (f'Host=crm-db;Port=5432;Database={database};Username={role};Password=' + values[password].decode('ascii')).encode('ascii')
        for name, value in values.items():
            _write(temporary / name, value)
        manifest = {'version': 1, 'project': project, 'sha256': {name: hashlib.sha256(value).hexdigest() for name, value in values.items()}}
        _write(temporary / 'manifest.json', json.dumps(manifest, sort_keys=True).encode('ascii'))
        fd = os.open(temporary, os.O_RDONLY | os.O_DIRECTORY)
        try: os.fsync(fd)
        finally: os.close(fd)
        # A concurrent publisher must never be replaced, even if empty.
        # Lifecycle commands hold the project lock; rename publishes complete state.
        if state_dir.exists():
            raise SecretStateMismatch()
        _publish(temporary, state_dir)
        temporary = None
        fd = os.open(state_dir.parent, os.O_RDONLY | os.O_DIRECTORY)
        try: os.fsync(fd)
        finally: os.close(fd)
        return SecretState(state_dir, project, files)
    except (OSError, ValueError, TypeError, KeyError, AttributeError):
        raise SecretStateMismatch() from None
    finally:
        if temporary is not None:
            shutil.rmtree(temporary)
