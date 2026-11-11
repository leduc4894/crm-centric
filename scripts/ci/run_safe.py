"""Private scanner process output: callers may publish only parsed allowlisted data."""
from contextlib import contextmanager
from dataclasses import dataclass
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import uuid

class SafeProcessError(Exception):
    def __init__(self):
        self.reason_token = 'CI_SCAN_INCOMPLETE'
        self.correlation_id = str(uuid.uuid4())
        super().__init__(self.reason_token)

@dataclass(frozen=True)
class PrivateResult:
    stdout: Path
    stderr: Path
    directory: Path
    returncode: int

@contextmanager
def private_command(command, *, timeout=300, cwd=None, accepted=(0,), env=None):
    """No inherited tokens/config; no exception contains commands or diagnostics.

    Temp directory is destroyed even when parsing fails. Never upload these paths.
    Scanner reports should use stdout, or a path inside this directory.
    """
    process = None
    with tempfile.TemporaryDirectory(prefix='ci-scan-') as directory:
        private = Path(directory)
        os.chmod(private, 0o700)
        environment = {'PATH':os.defpath, 'HOME':directory, 'TMPDIR':directory,
                       'LANG':'C.UTF-8', 'LC_ALL':'C.UTF-8',
                       'SEMGREP_SEND_METRICS':'off', 'SEMGREP_ENABLE_VERSION_CHECK':'0'}
        if env:
            # Only caller-owned paths/configuration, never os.environ wholesale.
            if not set(env).issubset({'PATH', 'SEMGREP_RULES', 'TRIVY_CACHE_DIR'}):
                raise SafeProcessError()
            environment.update(env)
        try:
            outpath, errpath = private/'stdout', private/'stderr'
            with outpath.open('xb') as stdout, errpath.open('xb') as stderr:
                os.chmod(outpath,0o600); os.chmod(errpath,0o600)
                process = subprocess.Popen(command, stdout=stdout, stderr=stderr, cwd=cwd,
                                           env=environment, start_new_session=True)
                process.wait(timeout=timeout)
            if process.returncode not in accepted or outpath.stat().st_size > 32*1024*1024 or errpath.stat().st_size > 32*1024*1024:
                raise SafeProcessError()
            yield PrivateResult(outpath,errpath,private,process.returncode)
        except (OSError, subprocess.SubprocessError, ValueError):
            raise SafeProcessError() from None
        finally:
            if process is not None:
                try: os.killpg(process.pid,signal.SIGTERM)
                except ProcessLookupError: pass
                try: process.wait(timeout=0.2)
                except subprocess.TimeoutExpired: pass
                # Descendants may remain after parent exit or ignore TERM.
                try: os.killpg(process.pid,signal.SIGKILL)
                except ProcessLookupError: pass
                process.wait()
