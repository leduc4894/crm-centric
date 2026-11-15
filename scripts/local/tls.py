"""Durable private local IdP TLS identity, published atomically and never rotated implicitly."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import tempfile

class TlsStateMismatch(RuntimeError):
    def __init__(self):
        super().__init__('LOCAL_SECRET_STATE_MISMATCH')

def _private(path, directory=False):
    item = path.lstat()
    if (item.st_uid != os.getuid() or stat.S_IMODE(item.st_mode) != (0o700 if directory else 0o600)
        or not (stat.S_ISDIR(item.st_mode) if directory else stat.S_ISREG(item.st_mode))):
        raise TlsStateMismatch()

def ensure_tls_state(path: Path, project: str, allow_create: bool):
    path = Path(os.path.abspath(path))
    temporary = None
    try:
        if os.getuid() == 0 or not re.fullmatch(r'[a-z0-9][a-z0-9_-]{0,62}', project):
            raise TlsStateMismatch()
        if any(parent.is_symlink() for parent in (path, *path.parents)):
            raise TlsStateMismatch()
        cert, key, manifest = path / 'cert.pem', path / 'key.pem', path / 'manifest.json'
        if path.exists():
            _private(path, True)
            if {p.name for p in path.iterdir()} != {'cert.pem', 'key.pem', 'manifest.json'}:
                raise TlsStateMismatch()
            for item in (cert, key, manifest): _private(item)
            expected = {'version': 1, 'project': project, 'sha256': {
                p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (cert, key)}}
            if json.loads(manifest.read_text()) != expected:
                raise TlsStateMismatch()
            validity = subprocess.run(['openssl', 'x509', '-in', str(cert), '-noout', '-checkend', '86400'],
                                      stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10)
            if validity.returncode: raise TlsStateMismatch()
            return cert, key
        if not allow_create: raise TlsStateMismatch()
        path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        temporary = Path(tempfile.mkdtemp(prefix='.tls-', dir=path.parent))
        result = subprocess.run(['openssl', 'req', '-x509', '-newkey', 'rsa:3072', '-nodes',
            '-keyout', str(temporary / 'key.pem'), '-out', str(temporary / 'cert.pem'),
            '-days', '3650', '-subj', '/CN=keycloak', '-addext', 'subjectAltName=DNS:keycloak',
            '-addext', 'basicConstraints=critical,CA:TRUE',
            '-addext', 'keyUsage=critical,digitalSignature,keyCertSign'], stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
        if result.returncode: raise TlsStateMismatch()
        for name in ('cert.pem', 'key.pem'):
            os.chmod(temporary / name, 0o600)
            with open(temporary / name, 'rb') as f: os.fsync(f.fileno())
        hashes = {name: hashlib.sha256((temporary / name).read_bytes()).hexdigest() for name in ('cert.pem', 'key.pem')}
        fd = os.open(temporary / 'manifest.json', os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        with os.fdopen(fd, 'w') as stream:
            json.dump({'version': 1, 'project': project, 'sha256': hashes}, stream, sort_keys=True)
            stream.flush(); os.fsync(stream.fileno())
        fd = os.open(temporary, os.O_RDONLY | os.O_DIRECTORY)
        try: os.fsync(fd)
        finally: os.close(fd)
        libc = ctypes.CDLL(None, use_errno=True)
        rename = libc.renameat2
        rename.argtypes = (ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint)
        if rename(-100, os.fsencode(temporary), -100, os.fsencode(path), 1): raise OSError(ctypes.get_errno(), 'TLS publication failed')
        temporary = None
        fd = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try: os.fsync(fd)
        finally: os.close(fd)
        return cert, key
    except (OSError, ValueError, TypeError, subprocess.TimeoutExpired):
        raise TlsStateMismatch() from None
    finally:
        if temporary is not None: shutil.rmtree(temporary)
