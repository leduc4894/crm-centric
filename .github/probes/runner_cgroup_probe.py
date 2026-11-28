"""Informational runner cgroup probe; never executes candidate code."""
import json
import os
from pathlib import Path, PurePosixPath
import re
import selectors
import subprocess
import time
import uuid


class UnsupportedMount(Exception):
    pass


def resolve_cgroup_parent(mountinfo, cgroups):
    """Only the unambiguous root-mounted cgroup v2 layout is supported."""
    matches = []
    for line in mountinfo.splitlines():
        parts = line.split(' ')
        try:
            sep = parts.index('-')
            if parts[sep + 1] == 'cgroup2':
                matches.append((parts[3], parts[4]))
        except (ValueError, IndexError):
            return None
    paths = [line[3:] for line in cgroups.splitlines() if line.startswith('0::/')]
    if matches != [('/', '/sys/fs/cgroup')] or len(paths) != 1:
        return None
    raw = paths[0]
    if '\\' in raw or any(part in ('.', '..') for part in raw.split('/')):
        return None
    return str(PurePosixPath('/sys/fs/cgroup') / raw.lstrip('/'))


def run_probe(backend):
    result = {'cgroup_version': 'unavailable', 'delegated_group': 'unavailable',
              'kill_tree': 'not_tested', 'cleanup': 'not_needed', 'mount_layout': 'unknown'}
    created = False
    try:
        result['cgroup_version'] = backend.perform('locate')
        result['mount_layout'] = 'supported'
        backend.perform('create')
        created = True
        result['delegated_group'] = 'created'
        backend.perform('spawn')
        backend.perform('attach')
        backend.perform('read_pid')
    except UnsupportedMount:
        result['cgroup_version'] = 'v2'
        result['mount_layout'] = 'unsupported'
    except (OSError, ValueError, TimeoutError, subprocess.SubprocessError):
        if created:
            result['kill_tree'] = 'failed'
    finally:
        if created:
            cleanup_ok = True
            try:
                backend.perform('kill')
                if backend.perform('populated'):
                    raise TimeoutError('cgroup remained populated')
            except (OSError, ValueError, TimeoutError, subprocess.SubprocessError):
                cleanup_ok = False
            try:
                backend.perform('reap')
            except (OSError, ValueError, TimeoutError, subprocess.SubprocessError):
                cleanup_ok = False
            if cleanup_ok:
                try:
                    backend.perform('remove')
                    result['cleanup'] = 'removed'
                    if result['kill_tree'] == 'not_tested':
                        result['kill_tree'] = 'terminated'
                except (OSError, ValueError, TimeoutError, subprocess.SubprocessError):
                    cleanup_ok = False
            if not cleanup_ok:
                result['cleanup'] = 'failed'
                result['kill_tree'] = 'failed'
    return result


class SystemBackend:
    def __init__(self):
        self.group = None
        self.process = None

    def perform(self, operation):
        if operation == 'locate':
            root = Path('/sys/fs/cgroup')
            if not (root / 'cgroup.controllers').is_file():
                raise OSError('not cgroup v2')
            parent = resolve_cgroup_parent(Path('/proc/self/mountinfo').read_text(),
                                           Path('/proc/self/cgroup').read_text())
            if parent is None:
                raise UnsupportedMount()
            resolved = Path(parent).resolve(strict=True)
            if not resolved.is_relative_to(root.resolve(strict=True)):
                raise OSError('outside cgroup mount')
            self.group = resolved / ('ci-probe-' + uuid.uuid4().hex)
            return 'v2'
        if operation == 'create':
            self.group.mkdir()
        elif operation == 'spawn':
            if not (self.group / 'cgroup.kill').is_file():
                raise OSError('cgroup kill unavailable')
            group = self.group
            def attach_before_exec():
                (group / 'cgroup.procs').write_text(str(os.getpid()), encoding='ascii')
            program = ("import subprocess,time; "
                       "p=subprocess.Popen(['/bin/sleep','30'], start_new_session=True); "
                       "print(p.pid, flush=True); time.sleep(30)")
            self.process = subprocess.Popen(['python3', '-c', program], stdout=subprocess.PIPE,
                                            stderr=subprocess.DEVNULL, text=True,
                                            preexec_fn=attach_before_exec)
        elif operation == 'attach':
            # Popen only returns after successful pre-exec attachment.
            if self.process is None:
                raise OSError('no attached process')
        elif operation == 'read_pid':
            with selectors.DefaultSelector() as selector:
                selector.register(self.process.stdout, selectors.EVENT_READ)
                if not selector.select(5):
                    raise TimeoutError('child did not start')
                line = self.process.stdout.readline().strip()
            if not re.fullmatch(r'[1-9][0-9]*', line):
                raise ValueError('invalid descendant identifier')
        elif operation == 'kill':
            (self.group / 'cgroup.kill').write_text('1', encoding='ascii')
        elif operation == 'populated':
            deadline = time.monotonic() + 5
            while time.monotonic() < deadline:
                events = (self.group / 'cgroup.events').read_text(encoding='ascii').splitlines()
                values = dict(line.split(' ', 1) for line in events)
                if values.get('populated') == '0':
                    return False
                if values.get('populated') != '1':
                    raise ValueError('missing populated state')
                time.sleep(0.05)
            return True
        elif operation == 'reap':
            if self.process is not None:
                self.process.wait(timeout=5)
        elif operation == 'remove':
            self.group.rmdir()
        else:
            raise ValueError('unknown operation')


def main():
    sha = os.environ.get('GITHUB_SHA', '')
    if not re.fullmatch(r'[0-9a-f]{40}', sha):
        raise SystemExit('invalid run commit identity')
    result = run_probe(SystemBackend())
    result['commit_sha'] = sha
    print(json.dumps(result, sort_keys=True))


if __name__ == '__main__':
    main()
