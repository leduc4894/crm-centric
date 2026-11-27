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
                    except (FileNotFoundError, ProcessLookupError):
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

    def test_contract_commands_identify_failed_stage_without_exposing_output(self):
        import contextlib, io, json
        commands = [
            ([sys.executable, 'scripts/contracts/check.py'], 'contract-generated-check'),
            (['bash', 'tests/ClientConsumers/run.sh'], 'contract-consumers'),
            ([sys.executable, 'scripts/contracts/compatibility.py', '--base-ref', 'private'], 'contract-compatibility'),
            (['bash', 'tests/Plan3/aggregate-reporting.sh'], 'plan3-aggregate-reporting'),
            (['bash', 'tests/Plan3/ci-environment.sh'], 'plan3-ci-environment'),
            (['bash', 'tests/Plan3/clean-export.sh'], 'plan3-clean-export'),
            (['bash', 'tests/Plan3/negative-cases.sh'], 'plan3-negative-cases'),
            (['bash', 'tests/Plan3/scratch-cleanup.sh'], 'plan3-scratch-cleanup'),
        ]
        process = unittest.mock.Mock(returncode=1)
        process.communicate.return_value = (b'private-output', b'private-error')
        for command, stage in commands:
            with self.subTest(stage=stage):
                stream = io.StringIO()
                with contextlib.redirect_stdout(stream), patch('scripts.ci.jobs.subprocess.Popen', return_value=process):
                    with self.assertRaises(JobError): execute(command)
                self.assertEqual({'reasonToken': 'CI_COMMAND_FAILED', 'commandStage': stage}, json.loads(stream.getvalue()))
                self.assertNotIn('private', stream.getvalue())

    def test_reviewed_contract_break_emits_safe_run_bound_diagnostic(self):
        import contextlib, io, json
        process = unittest.mock.Mock(returncode=0)
        process.communicate.return_value = (json.dumps({
            'reason': 'CONTRACT_BREAKING_REVIEWED',
            'securityReview': {'basis': 'maintainer-reviewed-adr-019', 'independentVerification': False},
            'private': 'do-not-emit'}).encode(), b'private-error')
        output = io.StringIO()
        with contextlib.redirect_stdout(output), patch('scripts.ci.jobs.subprocess.Popen', return_value=process):
            execute([sys.executable, 'scripts/contracts/compatibility.py'])
        self.assertTrue(output.getvalue(), 'reviewed break diagnostic was not emitted')
        self.assertEqual({'reasonToken': 'CONTRACT_BREAKING_REVIEWED',
                          'basis': 'maintainer-reviewed-adr-019', 'independentVerification': False},
                         json.loads(output.getvalue()))
        self.assertNotIn('private', output.getvalue())

    def test_contact_review_emits_only_scoped_safe_diagnostic(self):
        import contextlib, io, json
        process = unittest.mock.Mock(returncode=0)
        process.communicate.return_value = (json.dumps({
            'reason': 'CONTRACT_BREAKING_REVIEWED',
            'contactReview': {'basis': 'maintainer-reviewed-adr-020-021', 'independentVerification': False},
            'private': 'do-not-emit'}).encode(), b'private-error')
        output = io.StringIO()
        with contextlib.redirect_stdout(output), patch('scripts.ci.jobs.subprocess.Popen', return_value=process):
            execute([sys.executable, 'scripts/contracts/compatibility.py'])
        self.assertTrue(output.getvalue(), 'contact reviewed-break marker missing')
        self.assertEqual({'reasonToken': 'CONTRACT_BREAKING_REVIEWED',
                          'basis': 'maintainer-reviewed-adr-020-021', 'independentVerification': False},
                         json.loads(output.getvalue()))
        self.assertNotIn('private', output.getvalue())

    def test_plan3_review_evidence_survives_later_browser_failure(self):
        import contextlib, io, json
        process = unittest.mock.Mock(returncode=1)
        process.communicate.return_value = (
            b'private log\n===== compatibility: python3 scripts/contracts/compatibility.py (exit 0, 2s) =====\n' + json.dumps({'reason': 'CONTRACT_BREAKING_REVIEWED',
                'securityReview': {'basis': 'maintainer-reviewed-adr-019',
                                   'independentVerification': False}, 'private': 'sentinel'}).encode() + b'\n', b'')
        output = io.StringIO()
        with contextlib.redirect_stdout(output), patch('scripts.ci.jobs.subprocess.Popen', return_value=process):
            with self.assertRaises(JobError): execute(['bash', 'scripts/check-plan3.sh'])
        rows = [json.loads(line) for line in output.getvalue().splitlines()]
        self.assertIn({'reasonToken': 'CONTRACT_BREAKING_REVIEWED',
                       'basis': 'maintainer-reviewed-adr-019', 'independentVerification': False}, rows)
        self.assertNotIn('sentinel', output.getvalue())

    def test_plan3_other_step_cannot_forge_review_evidence(self):
        import contextlib, io, json
        process = unittest.mock.Mock(returncode=1)
        process.communicate.return_value = (
            b'===== generated-check: command (exit 0, 2s) =====\n' + json.dumps({
                'reason': 'CONTRACT_BREAKING_REVIEWED', 'securityReview': {
                    'basis': 'maintainer-reviewed-adr-019', 'independentVerification': False}}).encode() + b'\n', b'')
        output = io.StringIO()
        with contextlib.redirect_stdout(output), patch('scripts.ci.jobs.subprocess.Popen', return_value=process):
            with self.assertRaises(JobError): execute(['bash', 'scripts/check-plan3.sh'])
        self.assertNotIn('CONTRACT_BREAKING_REVIEWED', output.getvalue())

    def test_dotnet_failure_projects_only_known_test_family(self):
        import contextlib,io,json
        process=unittest.mock.Mock(returncode=1)
        process.communicate.return_value=(b'Failed Integration.SmokeTests.case',b'')
        stream=io.StringIO()
        with contextlib.redirect_stdout(stream),patch('scripts.ci.jobs.subprocess.Popen',return_value=process):
            with self.assertRaises(JobError):execute(['dotnet','test','private'])
        diagnostic=json.loads(stream.getvalue())
        self.assertEqual(['SMOKE_TESTS_FAILED'],diagnostic['dotnetTests'])
        self.assertEqual(['OTHER'],diagnostic['dotnetMethods'])
        self.assertNotIn('private',stream.getvalue())

    def test_dotnet_failure_identifies_wave2_test_families_without_echoing_payload(self):
        from scripts.ci.jobs import dotnet_failure_diagnostic
        output = (b'[xUnit.net 00:01] Integration.ContactSchemaTests.Case [FAIL]\n'
                  b'Failed Integration.TimelineEndpointsTests.Case private-sentinel')
        self.assertEqual(['CONTACT_SCHEMA_TESTS_FAILED', 'TIMELINE_ENDPOINTS_TESTS_FAILED'],
                         dotnet_failure_diagnostic(output, b''))

    def test_dotnet_failure_keeps_multiple_allowlisted_families(self):
        from scripts.ci.jobs import dotnet_failure_diagnostic
        self.assertEqual(['SMOKE_TESTS_FAILED','CONTAINER_IMAGE_BUILD_FAILED'],
            dotnet_failure_diagnostic(b'Failed Integration.SmokeTests.case Container operation failed: build exit 1',b''))
        self.assertEqual(['OTHER_TESTS_FAILED'],
            dotnet_failure_diagnostic(b'Passed Integration.SmokeTests.case',b''))
        self.assertEqual(['TEST_RUN_ABORTED','PERMISSION_FAILED'],
            dotnet_failure_diagnostic(b'Test Run Aborted: Permission denied',b''))
        self.assertEqual(['LOCAL_BOOTSTRAP_TESTS_FAILED'],dotnet_failure_diagnostic(
            b'[xUnit.net 00:01] Integration.LocalBootstrapTests.Adapter_case [FAIL]',b''))
        self.assertEqual(['READINESS_TESTS_FAILED'],dotnet_failure_diagnostic(
            b'\x1b[31mFailed Integration.ReadinessTests.Database_case\x1b[0m',b''))
        from scripts.ci.jobs import dotnet_failure_methods
        self.assertEqual(['Cold_import_has_two_pkce_clients_and_canonical_discovery'],
            dotnet_failure_methods(b'Integration.KeycloakContainerTests.Cold_import_has_two_pkce_clients_and_canonical_discovery [FAIL]',b''))

    def test_compose_failure_reports_only_allowlisted_test_identity(self):
        from scripts.ci.jobs import dotnet_failure_diagnostic, dotnet_failure_methods
        output = (b"[xUnit.net 00:01] Integration.Compose.LocalStackTests."
                  b"Api_uses_trusted_tls_backchannel_with_unchanged_public_issuer [FAIL]\n"
                  b"  Failed Integration.Compose.LocalStackFailureTests."
                  b"Parallel_projects_do_not_share_resources [1 s]\n"
                  b"  Failed Integration.Compose.LocalStackTests.private_payload [1 s]\n")
        self.assertEqual(['COMPOSE_LOCAL_STACK_TESTS_FAILED', 'COMPOSE_LOCAL_STACK_FAILURE_TESTS_FAILED'],
                         dotnet_failure_diagnostic(output, b''))
        self.assertEqual(['Api_uses_trusted_tls_backchannel_with_unchanged_public_issuer',
                          'Parallel_projects_do_not_share_resources'], dotnet_failure_methods(output, b''))
        self.assertEqual(['OTHER'], dotnet_failure_methods(b'Failed Integration.Compose.LocalStackTests.private_payload', b''))

    def test_compose_diagnostics_reject_payload_and_mismatched_family(self):
        from scripts.ci.jobs import dotnet_failure_diagnostic, dotnet_failure_methods
        examples = [
            b'Error message: Integration.Compose.LocalStackTests.Cold_up_needs_no_host_sdk [FAIL]',
            b'Failed Integration.Other.Some_test [1 s] payload Integration.Compose.LocalStackTests.Cold_up_needs_no_host_sdk',
        ]
        for line in examples:
            with self.subTest(line=line):
                self.assertEqual(['OTHER_TESTS_FAILED'], dotnet_failure_diagnostic(line, b''))
                self.assertEqual(['OTHER'], dotnet_failure_methods(line, b''))

    def test_compose_new_method_reports_fixed_family_but_not_method(self):
        from scripts.ci.jobs import dotnet_failure_diagnostic, dotnet_failure_methods
        output = b'Integration.Compose.LocalStackSecretTests.New_unreviewed_method [FAIL]'
        self.assertEqual(['COMPOSE_LOCAL_STACK_SECRET_TESTS_FAILED'], dotnet_failure_diagnostic(output, b''))
        self.assertEqual(['OTHER'], dotnet_failure_methods(output, b''))
        mismatch = b'Integration.Compose.LocalStackSecretTests.Cold_up_needs_no_host_sdk [FAIL]'
        self.assertEqual(['OTHER'], dotnet_failure_methods(mismatch, b''))

    def test_compose_safe_startup_reason_is_bound_to_failure_details(self):
        from scripts.ci.jobs import dotnet_failure_diagnostic
        name = b'Integration.Compose.LocalStackTests.Api_uses_trusted_tls_backchannel_with_unchanged_public_issuer'
        output = (b'  Failed ' + name + b' [1 m]\n  Error Message:\n'
                  b'   initial startup: status, status, LOCAL_START_TIMEOUT\n  Stack Trace:\nprivate-sentinel')
        self.assertEqual(['COMPOSE_LOCAL_STACK_TESTS_FAILED', 'LOCAL_START_TIMEOUT'],
                         dotnet_failure_diagnostic(output, b''))
        unrelated = (b'  Failed ' + name + b' [1 m]\n  Error Message:\n   TLS failure\n'
                     b'  Stack Trace:\n'
                     b'  Failed Integration.Other.Other_test [1 s]\n  Error Message:\n'
                     b'   operation: status, LOCAL_START_TIMEOUT\n')
        self.assertEqual(['COMPOSE_LOCAL_STACK_TESTS_FAILED'], dotnet_failure_diagnostic(unrelated, b''))
        self.assertEqual(['OTHER_TESTS_FAILED'], dotnet_failure_diagnostic(b'private-sentinel LOCAL_START_TIMEOUT', b''))

    def test_tls_stage_diagnostic_is_bounded_to_failing_test(self):
        from scripts.ci.jobs import dotnet_failure_diagnostic
        test = b'Integration.Compose.LocalStackTests.Api_uses_trusted_tls_backchannel_with_unchanged_public_issuer'
        for stage in ('STARTUP', 'DISCOVERY', 'MANAGEMENT'):
            with self.subTest(stage=stage):
                token = 'COMPOSE_TLS_' + stage + '_FAILED'
                output = (b'  Failed ' + test + b' [1 m]\n  Error Message:\n   reasonToken='
                          + token.encode() + b' correlationId=0123456789abcdef0123456789abcdef\n  Stack Trace:\n')
                self.assertEqual(['COMPOSE_LOCAL_STACK_TESTS_FAILED', token], dotnet_failure_diagnostic(output, b''))
        startup_timeout = (b'  Failed ' + test + b' [1 m]\n  Error Message:\n'
                           b'   reasonToken=COMPOSE_TLS_STARTUP_FAILED correlationId=0123456789abcdef0123456789abcdef cause=LOCAL_START_TIMEOUT\n')
        self.assertEqual(['COMPOSE_LOCAL_STACK_TESTS_FAILED', 'COMPOSE_TLS_STARTUP_FAILED', 'LOCAL_START_TIMEOUT'],
                         dotnet_failure_diagnostic(startup_timeout, b''))
        invalid = (b'  Failed ' + test + b' [1 m]\n  Error Message:\n'
                   b'   reasonToken=COMPOSE_TLS_DISCOVERY_FAILED correlationId=not-a-guid private-sentinel\n')
        self.assertEqual(['COMPOSE_LOCAL_STACK_TESTS_FAILED'], dotnet_failure_diagnostic(invalid, b''))
        unrelated = (b'  Failed ' + test + b' [1 m]\n  Error Message:\n   unrelated\n  Stack Trace:\n'
                     b'  Failed Integration.Other.Other [1 s]\n  Error Message:\n'
                     b'   reasonToken=COMPOSE_TLS_DISCOVERY_FAILED correlationId=0123456789abcdef0123456789abcdef\n')
        self.assertEqual(['COMPOSE_LOCAL_STACK_TESTS_FAILED'], dotnet_failure_diagnostic(unrelated, b''))

    def test_failed_compose_command_does_not_echo_private_output(self):
        import contextlib, io, json
        process = unittest.mock.Mock(returncode=1)
        process.communicate.return_value = (
            b'Failed Integration.Compose.LocalStackFailureTests.Parallel_projects_do_not_share_resources [1 s]\nprivate-sentinel', b'')
        stream = io.StringIO()
        with contextlib.redirect_stdout(stream), patch('scripts.ci.jobs.subprocess.Popen', return_value=process):
            with self.assertRaises(JobError):
                execute(['dotnet', 'test', 'private-sentinel'])
        diagnostic = json.loads(stream.getvalue())
        self.assertEqual('CI_COMMAND_FAILED', diagnostic['reasonToken'])
        self.assertEqual(['COMPOSE_LOCAL_STACK_FAILURE_TESTS_FAILED'], diagnostic['dotnetTests'])
        self.assertEqual(['Parallel_projects_do_not_share_resources'], diagnostic['dotnetMethods'])
        self.assertNotIn('private-sentinel', stream.getvalue())

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

    def test_browser_followup_commands_report_fixed_stage_without_payload(self):
        import contextlib,io,json
        process=unittest.mock.Mock(returncode=1)
        process.communicate.return_value=(b'private-sentinel',b'')
        commands=[(['npm','exec','--workspace','apps/crm-web','--','playwright','test','tests/e2e/browser-oidc.spec.ts'], 'browser-provider'),
                  (['npm','exec','--workspace','apps/crm-web','--','playwright','test','tests/e2e/contacts.spec.ts'], 'browser-contacts'),
                  (['npm','exec','--workspace','apps/crm-web','--','vitest','run'], 'browser-vitest')]
        for command,stage in commands:
            with self.subTest(stage=stage):
                stream=io.StringIO()
                with contextlib.redirect_stdout(stream),patch('scripts.ci.jobs.subprocess.Popen',return_value=process):
                    with self.assertRaises(JobError):execute(command)
                result=json.loads(stream.getvalue())
                self.assertEqual(stage,result['commandStage'])
                self.assertNotIn('private-sentinel',stream.getvalue())

    def test_browser_vitest_failure_exposes_only_approved_file(self):
        import contextlib,io,json,tempfile
        from pathlib import Path
        process=unittest.mock.Mock(returncode=1)
        process.communicate.return_value=(b'private-sentinel',b'')
        with tempfile.TemporaryDirectory(prefix='ci-job-') as directory:
            report=Path(directory)/'vitest.json'
            report.write_text(json.dumps({'testResults':[{'name':'/private/apps/crm-web/src/contacts/contactQueries.test.tsx','status':'failed','message':'private-sentinel'},{'name':'/private/apps/crm-web/src/secret.test.ts','status':'failed'}]}))
            command=['npm','exec','--workspace','apps/crm-web','--','vitest','run','--outputFile='+str(report)]
            stream=io.StringIO()
            with contextlib.redirect_stdout(stream),patch('scripts.ci.jobs.subprocess.Popen',return_value=process):
                with self.assertRaises(JobError):execute(command)
            result=json.loads(stream.getvalue())
            self.assertEqual(['src/contacts/contactQueries.test.tsx'],result['vitestFailedFiles'])
            self.assertNotIn('private-sentinel',stream.getvalue())
            self.assertNotIn('secret.test.ts',stream.getvalue())
            from scripts.ci.jobs import vitest_failure_files
            report.unlink();report.symlink_to('/etc/hosts')
            self.assertEqual(['OTHER'],vitest_failure_files(command))
            self.assertEqual(['OTHER'],vitest_failure_files(['--outputFile=/etc/hosts']))

    def test_clean_export_failure_projects_nested_frontend_failure(self):
        import contextlib,io,json
        process=unittest.mock.Mock(returncode=1)
        process.communicate.return_value=(b'===== frontend-test: npm run test (exit 1, 9s) =====\n FAIL src/contacts/contactQueries.test.tsx > private-sentinel\nplan3 aggregate: FAIL at frontend-test (PLAN3_FRONTEND_STEP_FAILED)\n',b'')
        stream=io.StringIO()
        with contextlib.redirect_stdout(stream),patch('scripts.ci.jobs.subprocess.Popen',return_value=process):
            with self.assertRaises(JobError): execute(['bash','tests/Plan3/clean-export.sh'])
        result=json.loads(stream.getvalue())
        self.assertEqual('frontend-test',result['plan3Step'])
        self.assertEqual(['src/contacts/contactQueries.test.tsx'],result['plan3FailedFiles'])
        self.assertNotIn('private-sentinel',stream.getvalue())

    def test_frontend_failure_reports_only_known_test_file(self):
        from scripts.ci.jobs import plan3_failure_diagnostic
        data = ("===== frontend-test: npm run test (exit 1, 9s) =====\n"
                " ❯ src/contacts/ContactAttributes.test.tsx (36 tests | 1 failed)\n"
                " FAIL src/contacts/ContactAttributes.test.tsx > private-sentinel\n"
                " FAIL src/contacts/Untrusted.test.tsx > private-sentinel\n"
                "plan3 aggregate: FAIL at frontend-test (PLAN3_FRONTEND_STEP_FAILED)\n").encode()
        result = plan3_failure_diagnostic(data,b'')
        self.assertEqual(['src/contacts/ContactAttributes.test.tsx'],result['plan3FailedFiles'])
        self.assertNotIn('private-sentinel',str(result))
        self.assertNotIn('Untrusted',str(result))

    def test_plan3_duplicate_keys_and_oversized_streams_are_unknown(self):
        from scripts.ci.jobs import plan3_failure_diagnostic
        unknown={'plan3Step':'other','plan3Reason':'other'}
        duplicate=b'{"reason":"PLAN3_TOOLCHAIN_MISMATCH","reason":"PLAN3_PREREQUISITE_MISSING"}'
        self.assertEqual(unknown,plan3_failure_diagnostic(b'',duplicate))
        first=b'plan3 aggregate: FAIL at sdk-consumers (PLAN3_SDK_CONSUMERS_FAILED)\n'
        last=b'plan3 aggregate: FAIL at frontend-build (PLAN3_FRONTEND_STEP_FAILED)\n'
        self.assertEqual(unknown,plan3_failure_diagnostic(first+b'x'*(1024*1024)+b'\n'+last,b''))
        self.assertEqual(unknown,plan3_failure_diagnostic(last,b'x'*(1024*1024)+duplicate))
