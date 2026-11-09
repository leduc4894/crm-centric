"""Trusted local realm inspection. Tokens stay in memory; no logging or writes."""
import json
from contextlib import closing
from http.client import HTTPConnection, HTTPException
import io
import math
import os
from pathlib import Path
import stat
import socket
import time
from typing import NamedTuple
from urllib.parse import urlencode, urlsplit
import uuid

class AdminSecretFiles(NamedTuple):
    password_file: Path
    username: str = 'local-admin'

class RealmStatus(NamedTuple):
    status: str
    reason_token: str | None
    correlation_id: str

class _Drift(Exception):
    pass

def _remaining(deadline):
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise TimeoutError()
    return remaining

class _DeadlineReader(io.RawIOBase):
    """Bound every parser read, including partial status/header lines."""
    def __init__(self, sock, deadline):
        self.sock, self.deadline = sock, deadline
        # Preserve socket.makefile reference ownership when HTTPConnection
        # closes a connection before its response body has been consumed.
        self.stream = sock.makefile('rb', buffering=0)

    def readable(self):
        return True

    def readinto(self, buffer):
        self.sock.settimeout(_remaining(self.deadline))
        return self.stream.readinto(buffer)

    def close(self):
        try:
            self.stream.close()
        finally:
            super().close()

class _DeadlineSocket:
    def __init__(self, sock, deadline):
        self.sock, self.deadline = sock, deadline

    def makefile(self, mode):
        return io.BufferedReader(_DeadlineReader(self.sock, self.deadline))

    def sendall(self, data):
        self.sock.settimeout(_remaining(self.deadline))
        self.sock.sendall(data)

    def close(self):
        self.sock.close()

class _DeadlineConnection(HTTPConnection):
    def __init__(self, port, deadline):
        super().__init__('localhost', port, timeout=_remaining(deadline))
        self.deadline = deadline

    def connect(self):
        # The local profile publishes on IPv4 loopback. Use the numeric address
        # directly: no DNS/proxy worker can outlive this synchronous request.
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        try:
            sock.settimeout(_remaining(self.deadline))
            sock.connect(('127.0.0.1', self.port))
            self.sock = _DeadlineSocket(sock, self.deadline)
        except BaseException:
            sock.close()
            raise

def inspect_realm(public_base_url: str, admin_secret_files: AdminSecretFiles, timeout_seconds: float) -> RealmStatus:
    """Inspect canonical public metadata and actual persisted client configuration.

    Caller validates project/container ownership first. This helper accepts only
    HTTP localhost:<port>, never follows redirects/proxies and never mutates IdP.
    """
    correlation = uuid.uuid4().hex
    def result(status, token=None):
        return RealmStatus(status, token, correlation)
    try:
        parsed = urlsplit(public_base_url)
        if (parsed.scheme != 'http' or parsed.hostname != 'localhost' or
                parsed.username is not None or parsed.password is not None or
                not parsed.port or parsed.path or parsed.query or parsed.fragment or
                public_base_url != f'http://localhost:{parsed.port}' or
                not math.isfinite(timeout_seconds) or timeout_seconds <= 0):
            raise ValueError()
        password_path = Path(admin_secret_files.password_file)
        # Reject symlinks throughout the path and use O_NOFOLLOW for the file.
        if any(p.is_symlink() for p in (password_path, *password_path.parents)):
            raise ValueError()
        fd = os.open(password_path, os.O_RDONLY | os.O_NOFOLLOW)
        with os.fdopen(fd, 'rb') as stream:
            info = os.fstat(stream.fileno())
            if not stat.S_ISREG(info.st_mode) or stat.S_IMODE(info.st_mode) != 0o600 or info.st_uid != os.getuid():
                raise ValueError()
            password = stream.read(4097)
            if not password or len(password) > 4096:
                raise ValueError()
        deadline = time.monotonic() + timeout_seconds
        def request(path, data=None, token=None, persisted=False):
            headers = {'Accept': 'application/json', 'Connection': 'close'}
            if token:
                headers['Authorization'] = 'Bearer ' + token
            if data is not None:
                headers['Content-Type'] = 'application/x-www-form-urlencoded'
            with closing(_DeadlineConnection(parsed.port, deadline)) as connection:
                connection.request('POST' if data is not None else 'GET', path,
                                   body=data, headers=headers)
                with connection.getresponse() as response:
                    _remaining(deadline)
                    if persisted and response.status == 404:
                        raise _Drift()
                    if response.status != 200:
                        raise ValueError()
                    body = bytearray()
                    while not response.isclosed():
                        _remaining(deadline)
                        block = response.read1(65536)
                        if not block:
                            break
                        body.extend(block)
                        if len(body) > 1024 * 1024:
                            raise ValueError()
                    _remaining(deadline)
                    return json.loads(body)
        issuer = public_base_url + '/realms/crm-local'
        discovery = request('/realms/crm-local/.well-known/openid-configuration', persisted=True)
        if discovery.get('issuer') != issuer:
            raise _Drift()
        endpoints = {'authorization_endpoint': '/auth', 'token_endpoint': '/token',
                     'end_session_endpoint': '/logout', 'jwks_uri': '/certs'}
        for field, suffix in endpoints.items():
            if discovery.get(field) != issuer + '/protocol/openid-connect' + suffix:
                raise _Drift()
        jwks = request('/realms/crm-local/protocol/openid-connect/certs')
        if not jwks.get('keys') or any(any(field in key for field in ('d', 'p', 'q', 'dp', 'dq', 'qi', 'k')) for key in jwks['keys']):
            raise _Drift()
        token = request('/realms/master/protocol/openid-connect/token',
                        urlencode({'grant_type': 'password', 'client_id': 'admin-cli',
                                   'username': admin_secret_files.username,
                                   'password': password.decode('utf-8')}).encode('ascii'))['access_token']
        realm = request('/admin/realms/crm-local', token=token, persisted=True)
        if realm.get('realm') != 'crm-local' or realm.get('enabled') is not True:
            raise _Drift()
        clients = request('/admin/realms/crm-local/clients', token=token, persisted=True)
        for name, port in (('crm-web-local', 5173), ('demo-module-local', 5180)):
            matches = [client for client in clients if client.get('clientId') == name]
            if len(matches) != 1:
                raise _Drift()
            client = matches[0]
            expected = {'protocol': 'openid-connect', 'enabled': True, 'publicClient': True,
                        'standardFlowEnabled': True, 'implicitFlowEnabled': False,
                        'directAccessGrantsEnabled': False, 'serviceAccountsEnabled': False,
                        'redirectUris': [f'http://localhost:{port}/auth/callback'],
                        'webOrigins': [f'http://localhost:{port}']}
            if (any(client.get(key) != value for key, value in expected.items()) or
                    client.get('attributes', {}).get('pkce.code.challenge.method') != 'S256' or
                    'secret' in client):
                raise _Drift()
        return result('ready')
    except _Drift:
        return result('drift', 'LOCAL_REALM_DRIFT')
    except (OSError, HTTPException, ValueError, TypeError, KeyError, AttributeError):
        return result('unavailable', 'LOCAL_IDP_NOT_READY')
