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
import stat
import subprocess
from urllib.parse import urlsplit
import uuid

import yaml

from scripts.ci.revision import RevisionContext


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
    try:
        result = subprocess.run(['git', '--no-replace-objects', '-c', 'core.fsmonitor=false',
                                 '-c', 'core.untrackedCache=false', '-c', 'core.hooksPath=/dev/null',
                                 '-c', 'core.attributesfile=/dev/null', '-c', 'core.excludesFile=/dev/null',
                                 *args], cwd=repo, env=env, input=input_data,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=15)
        if result.returncode:
            raise ReviewSnapshotError()
        return result.stdout
    except (OSError, subprocess.SubprocessError):
        raise ReviewSnapshotError() from None


def _safe_name(name):
    if not isinstance(name, str) or not name or '\\' in name or any(ord(c) < 32 or ord(c) == 127 for c in name):
        raise ReviewSnapshotError()
    path = PurePosixPath(name)
    if path.is_absolute() or str(path) != name or any(part in ('.', '..') for part in path.parts):
        raise ReviewSnapshotError()
    return name


def _inventory(repo, commit):
    if not _is_sha(commit) or _git(repo, 'cat-file', '-t', commit).strip() != b'commit':
        raise ReviewSnapshotError()
    records = _git(repo, 'ls-tree', '-r', '-z', commit).split(b'\0')
    if len(records) > 20001:
        raise ReviewSnapshotError()
    result = {}
    for record in records:
        if not record:
            continue
        try:
            metadata, raw_name = record.split(b'\t', 1)
            mode, kind, oid = metadata.split(b' ')
            name = _safe_name(raw_name.decode('utf-8'))
            relevant = (name.startswith(('docs/contracts/', 'docs/architecture/', 'scripts/ci/', 'scripts/contracts/',
                                         'tools/contracts/', '.github/workflows/', 'sdks/', 'tests/Contract/',
                                         'tests/ClientConsumers/', 'tests/CI/')) or name in ('package.json', 'package-lock.json',
                                                                                 '.npmrc', 'requirements-contracts.lock'))
            if not relevant:
                continue
            if mode not in (b'100644', b'100755') or kind != b'blob' or name in result:
                raise ReviewSnapshotError()
            if not re.fullmatch(rb'[0-9a-f]{40}', oid):
                raise ReviewSnapshotError()
            size = int(_git(repo, 'cat-file', '-s', oid.decode('ascii')).strip())
            if size > 16 * 1024 * 1024:
                raise ReviewSnapshotError()
            result[name] = _git(repo, 'cat-file', 'blob', oid.decode('ascii'))
        except (ValueError, UnicodeError, OverflowError):
            raise ReviewSnapshotError() from None
    if sum(map(len, result.values())) > 512 * 1024 * 1024:
        raise ReviewSnapshotError()
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
    initial = {p for p in inventory if p.startswith('docs/contracts/') and p.endswith(('.openapi.yaml', '.schema.json'))}
    if not any(p.endswith('.openapi.yaml') for p in initial) or not any(p.endswith('.schema.json') for p in initial):
        raise ReviewSnapshotError()
    pending = list(initial)
    seen = set()
    while pending:
        name = pending.pop()
        if name in seen:
            continue
        if name not in inventory or not name.startswith('docs/contracts/'):
            raise ReviewSnapshotError()
        seen.add(name)
        try:
            text = inventory[name].decode('utf-8')
            document = (json.loads(text, object_pairs_hook=_pairs,
                                   parse_constant=lambda _: (_ for _ in ()).throw(ValueError('non-finite')))
                        if name.endswith('.json') else yaml.load(text, Loader=_UniqueYamlLoader))
            todo = [document]
            visited = set()
            count = 0
            while todo:
                node = todo.pop()
                count += 1
                if count > 100000:
                    raise ReviewSnapshotError()
                if isinstance(node, (dict, list)):
                    if id(node) in visited:
                        continue
                    visited.add(id(node))
                if isinstance(node, dict):
                    for key, value in node.items():
                        if key in ('$ref', '$dynamicRef', '$recursiveRef'):
                            if not isinstance(value, str):
                                raise ReviewSnapshotError()
                            ref = urlsplit(value)
                            if ref.scheme or ref.netloc or ref.query or '%' in ref.path or '\\' in ref.path or ref.path.startswith('/'):
                                raise ReviewSnapshotError()
                            if ref.path:
                                relative = PurePosixPath(ref.path)
                                if '..' in relative.parts or str(relative) != ref.path:
                                    raise ReviewSnapshotError()
                                target = str(PurePosixPath(name).parent / relative)
                                _safe_name(target)
                                pending.append(target)
                        else:
                            todo.append(value)
                elif isinstance(node, list):
                    todo.extend(node)
        except ReviewSnapshotError:
            raise
        except (UnicodeError, ValueError, TypeError, yaml.YAMLError, RecursionError):
            raise ReviewSnapshotError() from None
    return tuple(sorted((name, inventory[name]) for name in seen))


def _category(inventory, prefixes, exact=()):
    values = tuple(sorted((p, data) for p, data in inventory.items() if p.startswith(prefixes) or p in exact))
    if not values:
        raise ReviewSnapshotError()
    return values


def _hashes(snapshot):
    return tuple((path, hashlib.sha256(data).hexdigest()) for path, data in snapshot)


def _read_worktree_file(root_fd, name):
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
            if not stat.S_ISREG(before.st_mode) or before.st_size > 16 * 1024 * 1024:
                raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
            result = bytearray()
            while True:
                chunk = os.read(file_fd, 131072)
                if not chunk:
                    break
                result.extend(chunk)
                if len(result) > 16 * 1024 * 1024:
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


def _check_worktree(root, candidate):
    """Conservative dirty check without `git status`, clean filters or Git hooks.

    Raw worktree files are compared to index blobs. Text filters, non-LF eol,
    external attributes and transformations fail closed; data never enter the
    returned immutable snapshot. This is not an atomic worktree fence.
    """
    config = _git(root, 'config', '--local', '--includes', '--name-only', '--list').decode('utf-8').splitlines()
    if any(k.lower().startswith('filter.') or k.lower() in ('core.autocrlf', 'core.eol', 'core.attributesfile') for k in config):
        raise ReviewSnapshotError('CI_REVIEW_WORKTREE_UNSAFE')
    info = _git(root, 'rev-parse', '--git-path', 'info/attributes').decode('utf-8').strip()
    info_path = Path(info) if Path(info).is_absolute() else root / info
    if info_path.exists() or info_path.is_symlink():
        raise ReviewSnapshotError('CI_REVIEW_WORKTREE_UNSAFE')
    raw_index = _git(root, 'ls-files', '--stage', '-z')
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
    for record in _git(root, 'ls-tree', '-r', '-z', candidate).split(b'\0'):
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
    relevant = ('docs/contracts', 'docs/architecture', 'scripts/ci', 'scripts/contracts',
                'tools/contracts', '.github/workflows', 'sdks', 'tests/Contract', 'tests/ClientConsumers', 'tests/CI')
    noninputs = (':(exclude)**/__pycache__/**', ':(exclude)sdks/**/bin/**', ':(exclude)sdks/**/obj/**')
    if _git(root, 'ls-files', '--others', '--ignored', '--exclude-standard', '-z', '--', *relevant, *noninputs):
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
            data, mode_bits = _read_worktree_file(root_fd, name)
            total += len(data)
            if total > 512 * 1024 * 1024 or bool(mode_bits & 0o111) != (mode == b'100755'):
                raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
            if attr.get('text') in ('set', 'auto') and b'\x00' not in data[:8000] and b'\r\n' in data:
                raise ReviewSnapshotError('CI_REVIEW_WORKTREE_UNSAFE')
            object_size = int(_git(root, 'cat-file', '-s', oid.decode('ascii')).strip())
            if object_size > 16 * 1024 * 1024 or object_size != len(data):
                raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
            if hashlib.sha256(data).digest() != hashlib.sha256(_git(root, 'cat-file', 'blob', oid.decode('ascii'))).digest():
                raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
    finally:
        os.close(root_fd)


def capture_review_snapshot(repo: Path, event: VerifiedEvent, candidate_tree: str) -> ReviewSnapshot:
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
        if _git(root, 'rev-parse', '--show-toplevel').decode('utf-8').strip() != str(root):
            raise ReviewSnapshotError()
        if _git(root, 'for-each-ref', '--format=%(refname)', 'refs/replace/').strip():
            raise ReviewSnapshotError()
        _check_worktree(root, rev.candidate_sha)
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
        old, new = _inventory(root, rev.base_sha), _inventory(root, rev.candidate_sha)
        inputs = (_contract_inputs(old), _contract_inputs(new))
        policy = tuple(_category(x, ('scripts/ci/', 'scripts/contracts/', 'tools/contracts/', '.github/workflows/'),
                                 ('package.json', 'package-lock.json', '.npmrc', 'requirements-contracts.lock')) for x in (old, new))
        adrs = tuple(_category(x, ('docs/architecture/',)) for x in (old, new))
        sdks = tuple(_category(x, ('sdks/',)) for x in (old, new))
        consumers = tuple(_category(x, ('tests/Contract/', 'tests/ClientConsumers/', 'tests/CI/')) for x in (old, new))
        _check_worktree(root, rev.candidate_sha)
        if _git(root, 'rev-parse', '--verify', 'HEAD').decode('ascii').strip() != rev.candidate_sha:
            raise ReviewSnapshotError('CI_REVIEW_WORKTREE_DIRTY')
        return ReviewSnapshot(event, candidate_tree, *inputs, *map(_hashes, inputs), *policy, *adrs, *sdks, *consumers,
                              *map(_hashes, policy), *map(_hashes, adrs), *map(_hashes, sdks), *map(_hashes, consumers))
    except ReviewSnapshotError:
        raise
    except Exception:
        raise ReviewSnapshotError() from None
