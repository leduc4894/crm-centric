"""Unprivileged snapshot seam; no production event authority is provisioned here.

A protected coordinator must supply an independently trusted event authority.
Synthetic in-process authorities prove only tests; this module does not attest CI.
"""
from dataclasses import dataclass, field
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import selectors
import signal
import stat
import subprocess
import tempfile
import time
from urllib.parse import unquote_to_bytes, urlsplit
import uuid

import yaml

from scripts.ci.revision import RevisionContext

_MAX_GIT_STDOUT_BYTES = 32 * 1024 * 1024
_MAX_GIT_STDERR_BYTES = 64 * 1024
_MAX_SNAPSHOT_BYTES = 512 * 1024 * 1024
_MAX_BLOB_BYTES = 16 * 1024 * 1024
_MAX_TREE_ENTRIES = 20000


class ReviewSnapshotError(Exception):
    def __init__(self, reason='CI_REVIEW_SNAPSHOT_INVALID'):
        self.reason_token = reason
        self.correlation_id = str(uuid.uuid4())
        super().__init__(reason)


@dataclass(frozen=True)
class VerifiedEvent:
    revision: RevisionContext
    kind: str
    integration_method: str | None
    source_proof: bytes = field(repr=False)
    authority: object = field(repr=False, compare=False)


@dataclass(frozen=True)
class ReviewSnapshot:
    event: VerifiedEvent
    candidate_tree: str
    base_inputs: tuple
    candidate_inputs: tuple
    base_hashes: tuple
    candidate_hashes: tuple
    base_policy: tuple
    candidate_policy: tuple
    base_adr: tuple
    candidate_adr: tuple
    base_sdk: tuple
    candidate_sdk: tuple
    base_consumers: tuple
    candidate_consumers: tuple
    base_policy_hashes: tuple
    candidate_policy_hashes: tuple
    base_adr_hashes: tuple
    candidate_adr_hashes: tuple
    base_sdk_hashes: tuple
    candidate_sdk_hashes: tuple
    base_consumer_hashes: tuple
    candidate_consumer_hashes: tuple
    base_repository: tuple
    candidate_repository: tuple
    base_repository_hashes: tuple
    candidate_repository_hashes: tuple


def _is_sha(value):
    return isinstance(value, str) and re.fullmatch(r'[0-9a-f]{40}', value) is not None and value != '0' * 40


def wrap_verified_event(revision, kind, integration_method, source_proof, authority):
    """Validate event structure and require caller-supplied independent proof.

    The caller is responsible for securing the coordinator and its authority.
    This method and its synthetic test authority do not themselves prove that.
    """
    try:
        if not isinstance(revision, RevisionContext) or not all(_is_sha(x) for x in
                (revision.candidate_sha, revision.base_sha, revision.head_sha, revision.merge_base_sha)):
            raise ReviewSnapshotError()
        if revision.merge_base_sha != revision.base_sha:
            raise ReviewSnapshotError()
        if kind == 'pull_request':
            if integration_method not in ('merge', 'squash', 'rebase') or revision.merge_sha != revision.candidate_sha:
                raise ReviewSnapshotError()
        elif kind == 'push':
            if integration_method is not None or revision.merge_sha is not None or revision.head_sha != revision.candidate_sha:
                raise ReviewSnapshotError()
        else:
            raise ReviewSnapshotError()
        if not isinstance(source_proof, bytes) or not source_proof or authority is None:
            raise ReviewSnapshotError('CI_REVIEW_AUTHORITY_UNAVAILABLE')
        if authority.verify_event(revision, kind, integration_method, source_proof) is not True:
            raise ReviewSnapshotError('CI_REVIEW_EVENT_UNTRUSTED')
        return VerifiedEvent(revision, kind, integration_method, source_proof, authority)
    except ReviewSnapshotError:
        raise
    except Exception:
        raise ReviewSnapshotError('CI_REVIEW_EVENT_UNTRUSTED') from None


def _git(repo, *args, input_data=None):
    # A caller-controlled Git environment must not redirect object/working trees.
    env = {k: v for k, v in os.environ.items() if not k.startswith('GIT_')}
    env.update(GIT_NO_LAZY_FETCH='1', GIT_TERMINAL_PROMPT='0', GIT_CONFIG_NOSYSTEM='1',
               GIT_CONFIG_GLOBAL=os.devnull, GIT_ATTR_NOSYSTEM='1', GIT_OPTIONAL_LOCKS='0', PATH='/usr/bin:/bin')
    if input_data is not None and (not isinstance(input_data, bytes) or len(input_data) > _MAX_GIT_STDOUT_BYTES):
        raise ReviewSnapshotError('CI_REVIEW_RESOURCE_LIMIT')
    process = None
    try:
        with tempfile.TemporaryFile() as input_file, selectors.DefaultSelector() as selector:
            if input_data is not None:
                input_file.write(input_data)
                input_file.seek(0)
            process = subprocess.Popen(['git', '--no-replace-objects', '-c', 'core.fsmonitor=false',
                                        '-c', 'core.untrackedCache=false', '-c', 'core.hooksPath=/dev/null',
                                        '-c', 'core.attributesfile=/dev/null', '-c', 'core.excludesFile=/dev/null',
                                        *args], cwd=repo, env=env, stdin=input_file, stdout=subprocess.PIPE,
                                       stderr=subprocess.PIPE, start_new_session=True)
            selector.register(process.stdout, selectors.EVENT_READ, ('out', _MAX_GIT_STDOUT_BYTES))
            selector.register(process.stderr, selectors.EVENT_READ, ('err', _MAX_GIT_STDERR_BYTES))
            buffers = {'out': bytearray(), 'err': bytearray()}
            deadline = time.monotonic() + 15
            while selector.get_map():
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise ReviewSnapshotError('CI_REVIEW_GIT_TIMEOUT')
                ready = selector.select(remaining)
                if not ready:
                    raise ReviewSnapshotError('CI_REVIEW_GIT_TIMEOUT')
                for key, _ in ready:
                    chunk = os.read(key.fileobj.fileno(), 65536)
                    if not chunk:
                        selector.unregister(key.fileobj)
                        continue
                    name, limit = key.data
                    if len(buffers[name]) + len(chunk) > limit:
                        raise ReviewSnapshotError('CI_REVIEW_RESOURCE_LIMIT')
                    buffers[name].extend(chunk)
            remaining = deadline - time.monotonic()
            if remaining <= 0 or process.wait(timeout=remaining) != 0:
                raise ReviewSnapshotError()
            return bytes(buffers['out'])
    except ReviewSnapshotError:
        raise
    except (OSError, subprocess.SubprocessError):
        raise ReviewSnapshotError() from None
    finally:
        if process is not None:
            if process.poll() is None:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                process.wait()
            if process.stdout is not None:
                process.stdout.close()
            if process.stderr is not None:
                process.stderr.close()


def _safe_name(name):
    if not isinstance(name, str) or not name or '\\' in name or any(ord(c) < 32 or ord(c) == 127 for c in name):
        raise ReviewSnapshotError()
    path = PurePosixPath(name)
    if path.is_absolute() or str(path) != name or any(part in ('.', '..') for part in path.parts):
        raise ReviewSnapshotError()
    return name


def _inventory(repo, commit, budget=None):
    if not _is_sha(commit) or _git(repo, 'cat-file', '-t', commit).strip() != b'commit':
        raise ReviewSnapshotError()
    if budget is None:
        budget = _MAX_SNAPSHOT_BYTES
    if not isinstance(budget, int) or budget < 0 or budget > _MAX_SNAPSHOT_BYTES:
        raise ReviewSnapshotError('CI_REVIEW_RESOURCE_LIMIT')
    records = _git(repo, 'ls-tree', '-r', '-z', commit).split(b'\0')
    if len(records) > _MAX_TREE_ENTRIES + 1:
        raise ReviewSnapshotError()
    entries = {}
    total = 0
    for record in records:
        if not record:
            continue
        try:
            metadata, raw_name = record.split(b'\t', 1)
            mode, kind, oid = metadata.split(b' ')
            name = _safe_name(raw_name.decode('utf-8'))
            if mode not in (b'100644', b'100755') or kind != b'blob' or name in entries:
                raise ReviewSnapshotError()
            if not re.fullmatch(rb'[0-9a-f]{40}', oid):
                raise ReviewSnapshotError()
            size = int(_git(repo, 'cat-file', '-s', oid.decode('ascii')).strip())
            if size < 0 or size > _MAX_BLOB_BYTES:
                raise ReviewSnapshotError('CI_REVIEW_RESOURCE_LIMIT')
            total += size
            if total > budget:
                raise ReviewSnapshotError('CI_REVIEW_RESOURCE_LIMIT')
            entries[name] = (oid.decode('ascii'), size)
        except (ValueError, UnicodeError, OverflowError):
            raise ReviewSnapshotError() from None
    result = {}
    for name, (oid, size) in entries.items():
        data = _git(repo, 'cat-file', 'blob', oid)
        if len(data) != size:
            raise ReviewSnapshotError()
        result[name] = data
    return result


class _UniqueYamlLoader(yaml.SafeLoader):
    def construct_mapping(self, node, deep=False):
        result = {}
        for key, value in node.value:
            name = self.construct_object(key, deep=deep)
            if not isinstance(name, str) or name in result:
                raise ValueError('invalid mapping')
            result[name] = self.construct_object(value, deep=deep)
        return result


def _pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('duplicate key')
        result[key] = value
    return result


def _contract_inputs(inventory):
    """Validate a conservative local JSON-Pointer ref graph and return exact blobs.

    Nested resource IDs, anchors and dynamic/recursive refs are deliberately
    unsupported; rejecting them is safer than silently dropping their scope.
    """
    initial = {p for p in inventory if p.startswith('docs/contracts/') and p.endswith(('.openapi.yaml', '.schema.json'))}
    if not any(p.endswith('.openapi.yaml') for p in initial) or not any(p.endswith('.schema.json') for p in initial):
        raise ReviewSnapshotError()
    documents = {}
    states = {}
    node_count = 0

    def parse(name):
        if name in documents:
            return documents[name]
        if name not in inventory or not name.startswith('docs/contracts/'):
            raise ReviewSnapshotError()
        text = inventory[name].decode('utf-8')
        def reject_constant(_):
            raise ValueError('non-finite JSON value')
        value = (json.loads(text, object_pairs_hook=_pairs, parse_constant=reject_constant)
                 if name.endswith('.json') else yaml.load(text, Loader=_UniqueYamlLoader))
        if not isinstance(value, (dict, list)):
            raise ReviewSnapshotError()
        documents[name] = value
        return value

    def resolve(source, value):
        if not isinstance(value, str) or any(ord(c) < 32 or ord(c) == 127 for c in value):
            raise ReviewSnapshotError()
        if re.search(r'%(?![0-9a-fA-F]{2})', value):
            raise ReviewSnapshotError()
        ref = urlsplit(value)
        before_fragment = value.split('#', 1)[0]
        if (ref.scheme or ref.netloc or '?' in before_fragment or '%' in ref.path or
                '\\' in ref.path or ref.path.startswith('/')):
            raise ReviewSnapshotError()
        target = source
        if ref.path:
            relative = PurePosixPath(ref.path)
            if '..' in relative.parts or str(relative) != ref.path:
                raise ReviewSnapshotError()
            target = _safe_name(str(PurePosixPath(source).parent / relative))
        fragment = unquote_to_bytes(ref.fragment).decode('utf-8')
        if fragment and not fragment.startswith('/'):
            raise ReviewSnapshotError()
        path = []
        for token in fragment.split('/')[1:] if fragment else ():
            if re.search(r'~(?![01])', token):
                raise ReviewSnapshotError()
            path.append(token.replace('~1', '/').replace('~0', '~'))
        node = parse(target)
        canonical = []
        for token in path:
            if isinstance(node, dict) and token in node:
                node = node[token]
                canonical.append(token)
            elif isinstance(node, list) and re.fullmatch(r'0|[1-9][0-9]*', token) and int(token) < len(node):
                index = int(token)
                node = node[index]
                canonical.append(index)
            else:
                raise ReviewSnapshotError()
        return target, tuple(canonical), node

    def visit(name, pointer, node, depth):
        nonlocal node_count
        if depth > 128:
            raise ReviewSnapshotError('CI_REVIEW_RESOURCE_LIMIT')
        key = (name, pointer)
        if states.get(key) == 1:
            raise ReviewSnapshotError()
        if states.get(key) == 2:
            return
        node_count += 1
        if node_count > 100000:
            raise ReviewSnapshotError('CI_REVIEW_RESOURCE_LIMIT')
        states[key] = 1
        if isinstance(node, dict):
            if any(key in node for key in ('$id', '$anchor', '$dynamicAnchor', '$dynamicRef', '$recursiveRef')):
                raise ReviewSnapshotError()
            if '$ref' in node:
                target, path, target_node = resolve(name, node['$ref'])
                visit(target, path, target_node, depth + 1)
            for name_part, value in node.items():
                if name_part != '$ref':
                    visit(name, pointer + (name_part,), value, depth + 1)
        elif isinstance(node, list):
            for index, value in enumerate(node):
                visit(name, pointer + (index,), value, depth + 1)
        states[key] = 2

    try:
        for name in sorted(initial):
            visit(name, (), parse(name), 0)
        return tuple(sorted((name, inventory[name]) for name in documents))
    except ReviewSnapshotError:
        raise
    except (UnicodeError, ValueError, TypeError, yaml.YAMLError, RecursionError):
        raise ReviewSnapshotError() from None


def _category(inventory, prefixes, exact=()):
    values = tuple(sorted((p, data) for p, data in inventory.items() if p.startswith(prefixes) or p in exact))
    if not values:
        raise ReviewSnapshotError()
    return values


def _hashes(snapshot):
    return tuple((path, hashlib.sha256(data).hexdigest()) for path, data in snapshot)


def _read_worktree_file(root_fd, name, remaining):
    """Read each path without following symlinks; returned bytes never become review inputs."""
    components = _safe_name(name).split('/')
    current = os.dup(root_fd)
    try:
        for component in components[:-1]:
            next_fd = os.open(component, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=current)
            os.close(current)
            current = next_fd
        file_fd = os.open(components[-1], os.O_RDONLY | os.O_NOFOLLOW, dir_fd=current)
        try:
            before = os.fstat(file_fd)
            if not stat.S_ISREG(before.st_mode) or before.st_size > _MAX_BLOB_BYTES or before.st_size > remaining:
                raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
            result = bytearray()
            while True:
                chunk = os.read(file_fd, 131072)
                if not chunk:
                    break
                result.extend(chunk)
                if len(result) > _MAX_BLOB_BYTES or len(result) > remaining:
                    raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
            after = os.fstat(file_fd)
            identity = lambda item: (item.st_dev, item.st_ino, item.st_size, item.st_mtime_ns, item.st_ctime_ns, item.st_mode)
            if identity(before) != identity(after):
                raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
            return bytes(result), after.st_mode
        finally:
            os.close(file_fd)
    finally:
        os.close(current)


def _reject_unsafe_git_metadata(root):
    """Reject object/attribute indirection and local transformation helpers."""
    config = _git(root, 'config', '--local', '--includes', '--name-only', '--list').decode('utf-8').splitlines()
    unsafe = {'core.autocrlf', 'core.eol', 'core.attributesfile', 'core.alternaterefscommand',
              'core.alternaterefprefixes', 'extensions.partialclone'}
    if any(k.lower().startswith('filter.') or k.lower() in unsafe or
           (k.lower().startswith('remote.') and k.lower().endswith(('.promisor', '.partialclonefilter')))
           for k in config):
        raise ReviewSnapshotError('CI_REVIEW_WORKTREE_UNSAFE')
    for name in ('info/attributes', 'objects/info/alternates', 'objects/info/http-alternates'):
        path = _git(root, 'rev-parse', '--git-path', name).decode('utf-8').strip()
        metadata = Path(path) if Path(path).is_absolute() else root / path
        if metadata.exists() or metadata.is_symlink():
            raise ReviewSnapshotError('CI_REVIEW_WORKTREE_UNSAFE')


def _check_worktree(root, candidate):
    """Conservative dirty check without `git status`, clean filters or Git hooks.

    Raw worktree files are compared to index blobs. Text filters, non-LF eol,
    external attributes and transformations fail closed; data never enter the
    returned immutable snapshot. This is not an atomic worktree fence.
    """
    _reject_unsafe_git_metadata(root)
    raw_index = _git(root, 'ls-files', '--stage', '-z')
    if raw_index.count(b'\0') > _MAX_TREE_ENTRIES:
        raise ReviewSnapshotError('CI_REVIEW_RESOURCE_LIMIT')
    index = {}
    for record in raw_index.split(b'\0'):
        if not record:
            continue
        metadata, raw_name = record.split(b'\t', 1)
        mode, oid, stage = metadata.split(b' ')
        name = _safe_name(raw_name.decode('utf-8'))
        if name in index or stage != b'0' or mode not in (b'100644', b'100755') or not re.fullmatch(rb'[0-9a-f]{40}', oid):
            raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
        index[name] = (mode, oid)
    tree = {}
    raw_tree = _git(root, 'ls-tree', '-r', '-z', candidate)
    if raw_tree.count(b'\0') > _MAX_TREE_ENTRIES:
        raise ReviewSnapshotError('CI_REVIEW_RESOURCE_LIMIT')
    for record in raw_tree.split(b'\0'):
        if not record:
            continue
        metadata, raw_name = record.split(b'\t', 1)
        mode, kind, oid = metadata.split(b' ')
        name = _safe_name(raw_name.decode('utf-8'))
        if name in tree or kind != b'blob' or mode not in (b'100644', b'100755'):
            raise ReviewSnapshotError('CI_REVIEW_WORKTREE_UNSAFE')
        tree[name] = (mode, oid)
    if index != tree or not index:
        raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
    if _git(root, 'ls-files', '--others', '--exclude-standard', '-z'):
        raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
    noninputs = (':(exclude)**/__pycache__/**', ':(exclude)sdks/**/bin/**', ':(exclude)sdks/**/obj/**',
                 ':(exclude).superpowers/**', ':(exclude).local/**', ':(exclude).venv/**',
                 ':(exclude)node_modules/**', ':(exclude)**/node_modules/**')
    if _git(root, 'ls-files', '--others', '--ignored', '--exclude-standard', '-z', '--', '.', *noninputs):
        raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
    names = b''.join(name.encode('utf-8') + b'\0' for name in index)
    raw_attrs = _git(root, 'check-attr', '--cached', '-z', '--stdin', 'text', 'eol', 'ident', 'filter',
                     'working-tree-encoding', input_data=names).split(b'\0')
    if raw_attrs[-1:] != [b''] or (len(raw_attrs) - 1) % 3:
        raise ReviewSnapshotError('CI_REVIEW_WORKTREE_UNSAFE')
    attrs = {}
    for i in range(0, len(raw_attrs) - 1, 3):
        name, key, value = (x.decode('utf-8') for x in raw_attrs[i:i + 3])
        if name not in index:
            raise ReviewSnapshotError('CI_REVIEW_WORKTREE_UNSAFE')
        attrs.setdefault(name, {})[key] = value
    if set(attrs) != set(index):
        raise ReviewSnapshotError('CI_REVIEW_WORKTREE_UNSAFE')
    root_fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    total = 0
    try:
        for name, (mode, oid) in index.items():
            attr = attrs[name]
            if (attr.get('filter') not in ('unspecified', 'unset') or
                    attr.get('ident') not in ('unspecified', 'unset') or
                    attr.get('working-tree-encoding') not in ('unspecified', 'unset') or
                    attr.get('eol') not in ('unspecified', 'lf') or
                    attr.get('text') not in ('unspecified', 'unset', 'set', 'auto')):
                raise ReviewSnapshotError('CI_REVIEW_WORKTREE_UNSAFE')
            data, mode_bits = _read_worktree_file(root_fd, name, _MAX_SNAPSHOT_BYTES - total)
            total += len(data)
            if total > _MAX_SNAPSHOT_BYTES or bool(mode_bits & 0o111) != (mode == b'100755'):
                raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
            if attr.get('text') in ('set', 'auto') and b'\x00' not in data[:8000] and b'\r\n' in data:
                raise ReviewSnapshotError('CI_REVIEW_WORKTREE_UNSAFE')
            object_size = int(_git(root, 'cat-file', '-s', oid.decode('ascii')).strip())
            if object_size > _MAX_BLOB_BYTES or object_size != len(data):
                raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
            if hashlib.sha256(data).digest() != hashlib.sha256(_git(root, 'cat-file', 'blob', oid.decode('ascii'))).digest():
                raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
    finally:
        os.close(root_fd)


def _capture_with_guard(repo: Path, event: VerifiedEvent, candidate_tree: str) -> ReviewSnapshot:
    """Require protected coordinator's continuous confinement capability.

    This module provides no such production capability. A synthetic guard can
    test this seam but cannot establish OS/CI isolation or authorize a verdict.
    """
    try:
        if not isinstance(event, VerifiedEvent) or not _is_sha(candidate_tree):
            raise ReviewSnapshotError()
        wrap_verified_event(event.revision, event.kind, event.integration_method, event.source_proof, event.authority)
        if not callable(getattr(event.authority, 'confine', None)):
            raise ReviewSnapshotError('CI_REVIEW_CONFINEMENT_UNAVAILABLE')
        with event.authority.confine(Path(repo), event.revision, candidate_tree) as guard:
            if guard is None or guard.assert_confined() is not True:
                raise ReviewSnapshotError('CI_REVIEW_CONFINEMENT_UNAVAILABLE')
            snapshot = _capture_confined_snapshot(repo, event, candidate_tree)
            if guard.assert_confined() is not True:
                raise ReviewSnapshotError('CI_REVIEW_CONFINEMENT_UNAVAILABLE')
            return snapshot
        raise ReviewSnapshotError('CI_REVIEW_CONFINEMENT_UNAVAILABLE')
    except ReviewSnapshotError:
        raise
    except Exception:
        raise ReviewSnapshotError('CI_REVIEW_CONFINEMENT_UNAVAILABLE') from None


def capture_review_snapshot(repo: Path, event: VerifiedEvent, candidate_tree: str) -> ReviewSnapshot:
    """Production capture is unavailable until an independently protected owner exists.

    An in-process object or synthetic subprocess cannot establish owner identity,
    lifetime containment, safe result transport, or guard cleanup on forced kill.
    Do not invoke the private snapshot fixture from a coordinator.
    """
    raise ReviewSnapshotError('CI_REVIEW_SUPERVISOR_UNAVAILABLE')


def _capture_confined_snapshot(repo: Path, event: VerifiedEvent, candidate_tree: str) -> ReviewSnapshot:
    """Snapshot validated Git objects; worktree bytes are only checked for dirtiness.

    No worktree bytes are accepted as review inputs; clean checks are not an
    atomic fence. No candidate-configured filter or hook may run.
    """
    try:
        if not isinstance(event, VerifiedEvent):
            raise ReviewSnapshotError()
        wrap_verified_event(event.revision, event.kind, event.integration_method, event.source_proof, event.authority)
        rev = event.revision
        if not _is_sha(candidate_tree):
            raise ReviewSnapshotError()
        root = Path(repo).resolve(strict=True)
        _reject_unsafe_git_metadata(root)
        if _git(root, 'rev-parse', '--show-toplevel').decode('utf-8').strip() != str(root):
            raise ReviewSnapshotError()
        if _git(root, 'for-each-ref', '--format=%(refname)', 'refs/replace/').strip():
            raise ReviewSnapshotError()
        _check_worktree(root, rev.candidate_sha)
        _reject_unsafe_git_metadata(root)
        if (_git(root, 'for-each-ref', '--format=%(refname)', 'refs/replace/').strip() or
                _git(root, 'rev-parse', '--verify', 'HEAD').decode('ascii').strip() != rev.candidate_sha):
            raise ReviewSnapshotError()
        if _git(root, 'rev-parse', '--verify', rev.candidate_sha + '^{tree}').decode('ascii').strip() != candidate_tree:
            raise ReviewSnapshotError()
        if _git(root, 'merge-base', rev.candidate_sha, rev.base_sha).decode('ascii').strip() != rev.base_sha:
            raise ReviewSnapshotError()
        parents = _git(root, 'rev-list', '--parents', '-n', '1', rev.candidate_sha).decode('ascii').strip().split()
        if event.kind == 'pull_request' and parents != [rev.candidate_sha, rev.base_sha, rev.head_sha]:
            raise ReviewSnapshotError()
        for sha in (rev.base_sha, rev.head_sha, rev.candidate_sha):
            if _git(root, 'cat-file', '-t', sha).strip() != b'commit':
                raise ReviewSnapshotError()
        old = _inventory(root, rev.base_sha)
        remaining = _MAX_SNAPSHOT_BYTES - sum(map(len, old.values()))
        new = _inventory(root, rev.candidate_sha, remaining)
        inputs = (_contract_inputs(old), _contract_inputs(new))
        policy = tuple(_category(x, ('scripts/', 'tools/contracts/', '.github/'),
                                 ('package.json', 'package-lock.json', '.npmrc', 'requirements-contracts.lock',
                                  '.node-version', 'global.json', 'Directory.Build.props', 'Directory.Packages.props')) for x in (old, new))
        adrs = tuple(_category(x, ('docs/architecture/',)) for x in (old, new))
        sdks = tuple(_category(x, ('sdks/',)) for x in (old, new))
        consumers = tuple(_category(x, ('tests/Contract/', 'tests/ClientConsumers/', 'tests/CI/')) for x in (old, new))
        _check_worktree(root, rev.candidate_sha)
        if (_git(root, 'for-each-ref', '--format=%(refname)', 'refs/replace/').strip() or
                _git(root, 'rev-parse', '--verify', 'HEAD').decode('ascii').strip() != rev.candidate_sha):
            raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
        repository = (tuple(sorted(old.items())), tuple(sorted(new.items())))
        return ReviewSnapshot(event, candidate_tree, *inputs, *map(_hashes, inputs), *policy, *adrs, *sdks, *consumers,
                              *map(_hashes, policy), *map(_hashes, adrs), *map(_hashes, sdks), *map(_hashes, consumers),
                              *repository, *map(_hashes, repository))
    except ReviewSnapshotError:
        raise
    except Exception:
        raise ReviewSnapshotError() from None
