"""Real process-tree cleanup and safe failed-suite retention."""
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

from scripts.ci.jobs import JobError, execute

class ProcessTests(unittest.TestCase):
    def test_timeout_terminates_child_that_ignores_term(self):
        self.assert_timeout_terminates_ready_child(startup_delay=0)

    def test_timeout_terminates_slow_starting_child_that_ignores_term(self):
        self.assert_timeout_terminates_ready_child(startup_delay=0.75)

    def assert_timeout_terminates_ready_child(self, startup_delay):
        with tempfile.TemporaryDirectory() as directory:
            pidfile=Path(directory)/'child'
            child="import os,signal,time; signal.signal(signal.SIGTERM,signal.SIG_IGN); open("+repr(str(pidfile))+",'w').write(str(os.getpid())); time.sleep(60)"
            command=[sys.executable,'-c','import subprocess,time; time.sleep('+repr(startup_delay)+'); subprocess.Popen('+repr([sys.executable,'-c',child])+'); time.sleep(60)']
            # Start the timeout only after the real descendant has installed its
            # SIGTERM handler and published its PID. Slow process startup must
            # not turn a cleanup assertion into a missing-fixture failure.
            popen = subprocess.Popen
            def start_ready(*args, **kwargs):
                from scripts.ci.jobs import stop_process_group
                process = popen(*args, **kwargs)
                deadline = time.monotonic() + 10
                try:
                    while time.monotonic() < deadline:
                        try:
                            if pidfile.read_text().strip().isdigit():
                                return process
                        except FileNotFoundError:
                            pass
                        if process.poll() is not None:
                            self.fail('fixture exited before child readiness')
                        time.sleep(.01)
                    self.fail('fixture did not signal child readiness')
                except BaseException:
                    stop_process_group(process)
                    process.communicate()
                    raise
            try:
                with patch('scripts.ci.jobs.subprocess.Popen', side_effect=start_ready):
                    with self.assertRaises(JobError) as caught: execute(command,timeout=0.5)
                self.assertEqual(caught.exception.token,'CI_COMMAND_TIMEOUT')
                self.assertTrue(pidfile.exists(), 'child was not ready before timeout')
                pid=int(pidfile.read_text())
                for _ in range(100):
                    stat=Path('/proc')/str(pid)/'stat'
                    try:
                        if stat.read_text().split()[2]=='Z': break
                    except FileNotFoundError:
                        break
                    time.sleep(.02)
                else: self.fail('command descendant survived timeout')
            finally:
                if pidfile.exists():
                    try: os.kill(int(pidfile.read_text()),signal.SIGKILL)
                    except ProcessLookupError: pass

    def test_cancellation_cleans_group_before_propagating(self):
        from scripts.ci.jobs import stop_process_group
        process=unittest.mock.Mock(pid=999999)
        with patch('scripts.ci.jobs.os.killpg') as kill, patch('scripts.ci.jobs.time.sleep'):
            stop_process_group(process,grace=0)
        self.assertEqual(kill.call_args_list[0].args,(999999,signal.SIGTERM))
        self.assertEqual(kill.call_args_list[-1].args,(999999,signal.SIGKILL))

    def test_interrupt_calls_group_cleanup_and_propagates(self):
        process=unittest.mock.Mock()
        process.communicate.side_effect=[KeyboardInterrupt, (b'',b'')]
        with patch('scripts.ci.jobs.subprocess.Popen',return_value=process) as spawn, patch('scripts.ci.jobs.stop_process_group') as stop:
            with self.assertRaises(KeyboardInterrupt):execute(['owned-command'])
        self.assertTrue(spawn.call_args.kwargs['start_new_session'])
        stop.assert_called_once_with(process)

    def test_failed_native_command_retains_discovered_report(self):
        from scripts.ci.jobs import native_suite
        with tempfile.TemporaryDirectory() as directory:
            output=Path(directory)/'results.trx'
            output.write_text('<TestRun/>')
            suites=[]
            with patch('scripts.ci.jobs.execute',side_effect=JobError('CI_COMMAND_FAILED')):
                with self.assertRaises(JobError):native_suite(['false'],suites,'compose','trx',output)
            self.assertEqual(suites,[{'name':'compose','format':'trx','report':'<TestRun/>'}])

    def test_failed_command_emits_only_allowlisted_stage(self):
        import contextlib,io,json
        from scripts.ci.jobs import execute
        process=unittest.mock.Mock(returncode=1)
        process.communicate.return_value=(b'private-output',b'private-error')
        for command,stage in [(['dotnet','restore','private-argument'],'dotnet-restore'),
                              (['bash','scripts/check-plan3.sh','private-argument'],'plan3'),
                              (['private-executable','private-argument'],'other')]:
            stream=io.StringIO()
            with contextlib.redirect_stdout(stream),patch('scripts.ci.jobs.subprocess.Popen',return_value=process):
                with self.assertRaises(JobError) as caught:execute(command)
            self.assertEqual('CI_COMMAND_FAILED',caught.exception.token)
            expected={'reasonToken':'CI_COMMAND_FAILED','commandStage':stage}
            if stage=='plan3':expected.update(plan3Step='other',plan3Reason='other')
            if stage=='dotnet-test':expected.update(dotnetTests=['OTHER_TESTS_FAILED'])
            self.assertEqual(expected,json.loads(stream.getvalue()))
            self.assertNotIn('private',stream.getvalue())

    def test_dotnet_failure_projects_only_known_test_family(self):
        import contextlib,io,json
        process=unittest.mock.Mock(returncode=1)
        process.communicate.return_value=(b'Failed Integration.SmokeTests.case',b'')
        stream=io.StringIO()
        with contextlib.redirect_stdout(stream),patch('scripts.ci.jobs.subprocess.Popen',return_value=process):
            with self.assertRaises(JobError):execute(['dotnet','test','private'])
        self.assertEqual(['SMOKE_TESTS_FAILED'],json.loads(stream.getvalue())['dotnetTests'])
        self.assertNotIn('private',stream.getvalue())

    def test_dotnet_failure_keeps_multiple_allowlisted_families(self):
        from scripts.ci.jobs import dotnet_failure_diagnostic
        self.assertEqual(['SMOKE_TESTS_FAILED','CONTAINER_IMAGE_BUILD_FAILED'],
            dotnet_failure_diagnostic(b'Failed Integration.SmokeTests.case Container operation failed: build exit 1',b''))
        self.assertEqual(['OTHER_TESTS_FAILED'],
            dotnet_failure_diagnostic(b'Passed Integration.SmokeTests.case',b''))

    def test_plan3_failure_reports_only_bounded_nested_step_and_reason(self):
        import contextlib,io,json
        process=unittest.mock.Mock(returncode=1)
        cases=[(b'private-payload\nplan3 aggregate: FAIL at sdk-consumers (PLAN3_SDK_CONSUMERS_FAILED)\n',
                b'private-stderr', 'sdk-consumers','PLAN3_SDK_CONSUMERS_FAILED'),
               (b'plan3 aggregate: FAIL at private-step (private-reason)\n',b'', 'other','other'),
               (b'plan3 aggregate: FAIL at compatibility (private-reason)\n',b'', 'compatibility','other'),
               (b'\xffprivate-payload',b'{"reason":"PLAN3_PREREQUISITE_MISSING","detail":"private-payload"}',
                'other','PLAN3_PREREQUISITE_MISSING'),
               (b'plan3 aggregate: FAIL at sdk-consumers (PLAN3_SDK_CONSUMERS_FAILED)\nplan3 aggregate: FAIL at frontend-build (PLAN3_FRONTEND_STEP_FAILED)\n',b'', 'other','other')]
        for stdout,stderr,step,reason in cases:
            process.communicate.return_value=(stdout,stderr)
            stream=io.StringIO()
            with contextlib.redirect_stdout(stream),patch('scripts.ci.jobs.subprocess.Popen',return_value=process):
                with self.assertRaises(JobError) as caught:execute(['bash','scripts/check-plan3.sh'])
            self.assertEqual('CI_COMMAND_FAILED',caught.exception.token)
            self.assertEqual({'reasonToken':'CI_COMMAND_FAILED','commandStage':'plan3',
                              'plan3Step':step,'plan3Reason':reason},json.loads(stream.getvalue()))
            self.assertNotIn('private',stream.getvalue())

    def test_plan3_duplicate_keys_and_oversized_streams_are_unknown(self):
        from scripts.ci.jobs import plan3_failure_diagnostic
        unknown={'plan3Step':'other','plan3Reason':'other'}
        duplicate=b'{"reason":"PLAN3_TOOLCHAIN_MISMATCH","reason":"PLAN3_PREREQUISITE_MISSING"}'
        self.assertEqual(unknown,plan3_failure_diagnostic(b'',duplicate))
        first=b'plan3 aggregate: FAIL at sdk-consumers (PLAN3_SDK_CONSUMERS_FAILED)\n'
        last=b'plan3 aggregate: FAIL at frontend-build (PLAN3_FRONTEND_STEP_FAILED)\n'
        self.assertEqual(unknown,plan3_failure_diagnostic(first+b'x'*(1024*1024)+b'\n'+last,b''))
        self.assertEqual(unknown,plan3_failure_diagnostic(last,b'x'*(1024*1024)+duplicate))
