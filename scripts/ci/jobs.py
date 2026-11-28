"""Isolated locked job runner. Raw subprocess diagnostics never leave this process."""
import argparse
from contextlib import contextmanager
import json
import os
from pathlib import Path
import platform
import re
import signal
import subprocess
import sys
import tempfile
import time
import uuid

from scripts.ci.summary import ReportError, RunIdentity, parse_report

VERSIONS = {'node':'24.22.2','npm':'11.19.1','python':'3.15.2','dotnet':'10.0.403','docker':'29.9.3','compose':'5.7.0'}

def verify_versions(actual):
    if actual != VERSIONS:
        raise ValueError('CI_TOOLCHAIN_MISMATCH')

class JobError(Exception):
    def __init__(self, token): self.token = token

def merge_browser_evidence(regular, provider, contacts):
    from scripts.ci.summary import parse_cases
    regular_cases = parse_cases('playwright', regular)
    provider_cases = parse_cases('playwright', provider)
    expected = 'browser-oidc.spec.ts::real provider signs in isolated tenants and terminates provider session::chromium'
    if len(provider_cases) != 1 or provider_cases[0] != {'id': expected, 'outcome': 'passed'}:
        raise ReportError('CI_DISCOVERY_INCOMPLETE')
    contacts_cases = parse_cases('playwright', contacts)
    expected_contacts = 'contacts.spec.ts::contact persists across sessions, rejects stale notes and isolates delayed tenant reads::chromium'
    if len(contacts_cases) != 1 or contacts_cases[0] != {'id': expected_contacts, 'outcome': 'passed'}:
        raise ReportError('CI_DISCOVERY_INCOMPLETE')
    cases = regular_cases + provider_cases + contacts_cases
    if len({case['id'] for case in cases}) != len(cases):
        raise ReportError('CI_DISCOVERY_INCOMPLETE')
    counters = ('expected', 'unexpected', 'skipped', 'flaky')
    return {'errors': [], 'stats': {key: regular['stats'][key] + provider['stats'][key] + contacts['stats'][key] for key in counters},
            'suites': regular['suites'] + provider['suites'] + contacts['suites']}

def stop_process_group(process, grace=1):
    """Signal only the session created for this exact command, including descendants."""
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        return
    time.sleep(grace)
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    process.wait()


_FRONTEND_TEST_FILES = frozenset((
    'config/contactsProxy.test.ts','config/devServer.test.ts','config/tenantProxy.test.ts',
    'config/healthProxy.test.ts','src/settings/CustomFieldsPage.test.tsx',
    'src/callbackRouter.test.tsx','src/contacts/queryScope.test.tsx',
    'src/contacts/ContactList.test.tsx','src/contacts/ContactTable.test.tsx',
    'src/contacts/ContactShell.test.tsx','src/contacts/ContactForm.test.tsx',
    'src/contacts/ContactTimeline.test.tsx','src/contacts/ContactAttributes.test.tsx',
    'src/contacts/contactSession.test.tsx','src/contacts/crmClient.test.ts',
    'src/contacts/contactQueries.test.tsx','src/contacts/CustomFieldSettings.test.tsx',
    'src/auth/oidcConfig.test.ts','src/auth/authClient.test.ts','src/auth/AuthProvider.test.tsx',
    'src/health/HealthStatus.test.tsx','src/health/healthClient.test.ts',
    'src/tenant/tenantClient.test.ts','src/tenant/TenantPicker.test.tsx',
    'src/router.test.tsx','src/tenant/TenantProvider.test.tsx',
    'tests/picomatch-compatibility.test.ts','tests/e2e/realOidcFixture.test.ts',
    'tests/e2e/stackProvenance.test.ts',
))

def plan3_failure_diagnostic(stdout, stderr):
    """Project private child output to fixed enums; never trust it as evidence.

    Diagnostic fields do not affect the job verdict. Ambiguous, malformed and
    unknown values produce `other`; no detail/command/payload string is copied.
    """
    steps=frozenset(('locked-install','generated-check','sdk-consumers','compatibility',
        'frontend-typecheck','frontend-lint','frontend-test','frontend-build',
        'frontend-leak-scan','browser-e2e'))
    reasons=frozenset(('PLAN3_ARGUMENT_INVALID','PLAN3_BROWSER_STEP_FAILED',
        'PLAN3_COMPATIBILITY_FAILED','PLAN3_FRONTEND_STEP_FAILED','PLAN3_GENERATED_CHECK_FAILED',
        'PLAN3_INTERRUPTED','PLAN3_LOCKED_INSTALL_FAILED','PLAN3_PREREQUISITE_MISSING',
        'PLAN3_SDK_CONSUMERS_FAILED','PLAN3_SOURCE_MUTATED','PLAN3_STACK_START_FAILED',
        'PLAN3_STACK_TEARDOWN_FAILED','PLAN3_STEP_FAILED','PLAN3_TEMP_DIRECTORY_UNAVAILABLE',
        'PLAN3_TERMINATED','PLAN3_TOOLCHAIN_MISMATCH','PLAN3_WORKSPACE_UNAVAILABLE',
        'CONTRACT_ARGUMENT_INVALID','CONTRACT_BASE_UNAVAILABLE','CONTRACT_BREAKING',
        'CONTRACT_DIFF_ERROR','CONTRACT_INPUT_INVALID','CONTRACT_INPUT_REVIEW_REQUIRED',
        'CONTRACT_POLICY_REVIEW_REQUIRED','CONTRACT_TOOL_ERROR','CONTRACT_TOOL_UNAVAILABLE',
        'SDK_SURFACE_INCOMPATIBLE','SDK_TOOLCHAIN_REVIEW_REQUIRED',
        'SECURITY_REVIEW_REQUIRED','SCHEMA_COMPATIBILITY_REVIEW_REQUIRED'))
    step=reason='other'
    unknown={'plan3Step':step,'plan3Reason':reason}
    # Never select a tail: it could hide an earlier conflicting envelope.
    if len(stdout)>1024*1024 or len(stderr)>1024*1024:
        return unknown
    class DuplicateKey(ValueError): pass
    def unique(pairs):
        result={}
        for key,value in pairs:
            if key in result: raise DuplicateKey()
            result[key]=value
        return result
    # Bound parsing work and decode failures; child diagnostics remain private.
    output=stdout.decode('utf-8',errors='replace')
    matches=re.findall(r'^plan3 aggregate: FAIL at ([a-z-]+) \(([^\r\n()]*)\)$',output,re.MULTILINE)
    if len(matches)==1:
        observed_step,observed_reason=matches[0]
        if observed_step in steps:
            step=observed_step
            if observed_reason in reasons: reason=observed_reason
    elif not matches:
        observed=[]
        for line in stderr.decode('utf-8',errors='replace').splitlines():
            try: row=json.loads(line,object_pairs_hook=unique)
            except DuplicateKey: return unknown
            except (ValueError,RecursionError): continue
            value=row.get('reason') if isinstance(row,dict) else None
            if isinstance(value,str) and value in reasons: observed.append(value)
        if len(observed)==1: reason=observed[0]
    result = {'plan3Step':step, 'plan3Reason':reason}
    if step == 'frontend-test':
        # Only source-reviewed filenames are eligible; never expose arbitrary
        # test titles, assertion details, paths or payloads from child output.

        clean = re.sub(r'\x1b\[[0-9;]*[A-Za-z]', '', output)
        sections = re.findall(r'^===== frontend-test: [^\r\n]* \(exit [1-9][0-9]*, [0-9]+s\) =====\r?\n(.*?)(?=^===== |\Z)', clean, re.MULTILINE | re.DOTALL)
        found = set()
        if len(sections) == 1:
            for line in sections[0].splitlines():
                match = re.match(r'^\s*FAIL\s+([^\s]+\.test\.tsx?)(?:\s|$)', line)
                if match and match.group(1) in _FRONTEND_TEST_FILES:
                    found.add(match.group(1))
        result['plan3FailedFiles'] = sorted(found) if found else ['OTHER']
        if 'src/contacts/ContactTimeline.test.tsx' in found:
            allowed_cases = frozenset((
                'requires explicit successful review when a saved note has no ETag',
                'does not attach the note ETag to an old contact representation',
                'renders ordered provenance and untrusted summaries as text and pages forward',
                'validates Unicode note length and uses returned or refreshed ETag (true)',
                'validates Unicode note length and uses returned or refreshed ETag (false)',
                'keeps draft after conflict or uncertain delivery and requires explicit reconciliation (412)',
                'keeps draft after conflict or uncertain delivery and requires explicit reconciliation (0)',
                'retries a failed timeline read and rejects a different contact in the page',
                'removes the stale record when reconciliation confirms deletion',
                'holds a timed-out note for explicit review without replay',
                'returns keyboard focus to the note input after saving',
                'gates cross-panel writes on refresh of the conflicting field and preserves its draft',
                'allows timeline pagination after the server denies a note write',
                'keeps writes locked when a sibling field read fails during reconciliation',
                'retains a successful note draft when its post-write timeline refresh fails',
                'prevents simultaneous note submits while the first request is pending',
                'locks for review and retains a saved note if sibling refresh returns 403',
                'keeps the note locked for review when the post-write contact read returns 403',
                'keeps the note locked for review when the post-write contact read returns 409',
            ))
            cases = set()
            for line in sections[0].splitlines():
                match = re.fullmatch(r'\s*FAIL\s+src/contacts/ContactTimeline\.test\.tsx\s+>\s+(.+?)\s*', line)
                if match and match.group(1) in allowed_cases:
                    cases.add(match.group(1))
            result['plan3TimelineCases'] = sorted(cases) if cases else ['OTHER']
    return result

# These public identifiers are reviewed source names, not arbitrary test output.
_COMPOSE_FAILURE_METHODS = {
    'LocalStackTests': frozenset((
        'Existing_v1_stack_migrates_tls_without_losing_data_and_rejects_missing_tls_afterwards',
        'Api_uses_trusted_tls_backchannel_with_unchanged_public_issuer',
        'Cold_up_needs_no_host_sdk', 'Warm_up_revalidates_oneshots_and_preserves_data',
        'Down_up_keeps_secrets_volumes_and_realm', 'Sql_and_secret_mount_boundaries_hold',
    )),
    'LocalStackFailureTests': frozenset((
        'Crm_db_outage_recovers_same_api_instance', 'Idp_outage_does_not_change_crm_readiness',
        'Bootstrap_and_migration_failure_block_new_api', 'Partial_init_retry_and_mismatched_secrets_are_safe',
        'Realm_drift_is_not_overwritten', 'Foreign_listener_cannot_supply_smoke_success',
        'Concurrent_lifecycle_is_rejected_including_distinct_state_paths',
        'Parallel_projects_do_not_share_resources', 'Shutdown_is_bounded',
    )),
    'LocalStackSecretTests': frozenset(('Secrets_and_request_sentinels_are_absent_across_success_and_failure',)),
}


def _compose_failure_records(text):
    # Bind the class and method to one complete xUnit failure record. Free-form
    # diagnostic text must never be interpreted as another failing test.
    name = r'Integration\.Compose\.(LocalStackTests|LocalStackFailureTests|LocalStackSecretTests)\.([A-Za-z0-9_]+)'
    xunit = re.compile(r'^\s*(?:\[xUnit\.net [^\]\r\n]{1,40}\]\s+)?' + name + r'\s+\[FAIL\]\s*$')
    summary = re.compile(r'^\s*Failed\s+' + name + r'(?:\s+\[[^\]\r\n]{1,32}\])?\s*$')
    for line in text.splitlines():
        found = xunit.fullmatch(line) or summary.fullmatch(line)
        if found:
            yield found.group(1), found.group(2)


def dotnet_failure_diagnostic(stdout, stderr):
    """Project only known integration-test families from private test output."""
    text = (stdout + b'\n' + stderr).decode('utf-8', errors='replace')
    text = re.sub(r'\x1b\[[0-9;]*[A-Za-z]', '', text)
    classes = (
        'ContainerRuntimeTests','FakeOidcIssuerTests','HealthTests','ImageEvidenceSecretTests',
        'KeycloakContainerTests','LocalBootstrapTests','MigrationTests','OperationalSecurityTests',
        'ReadinessTests','RolePrivilegeTests','SmokeTests','StructuredLoggingTests',
        'AuditEndpointsTests','ContactConcurrencyTests','ContactCursorTests','ContactEmailSchemaTests',
        'ContactEndpointsTests','ContactListTests','ContactSchemaTests','ContactStreamingTests',
        'ContactTagFoundationTests','CrmCollectionCursorTests','CustomFieldEndpointsTests',
        'CustomFieldSchemaTests','InviteAcceptanceTests','InviteManagementTests','InviteSchemaTests',
        'InviteSecurityTests','MembershipEndpointsTests','MembershipParsingTests','OidcAuthenticationTests',
        'TagEndpointsTests','TagFieldConcurrencyTests','TenantAuthorizationTests','TenantEndpointsTests',
        'TenantIsolationTests','TenantLifecycleSchemaTests','TenantProvisioningTests','TenantSchemaTests',
        'TimelineConcurrencyTests','TimelineEndpointsTests','TimelineSchemaTests',
    )
    matches=[]
    for name in classes:
        escaped=re.escape(name)
        if (re.search(rf'Integration\.{escaped}\.[^\r\n]*\[FAIL\]',text)
                or re.search(rf'Failed Integration\.{escaped}\.',text)):
            matches.append(re.sub(r'(?<!^)(?=[A-Z])','_',name).upper()+'_FAILED')
    compose_families = {family for family, _ in _compose_failure_records(text)}
    for name, token in (
        ('LocalStackTests', 'COMPOSE_LOCAL_STACK_TESTS_FAILED'),
        ('LocalStackFailureTests', 'COMPOSE_LOCAL_STACK_FAILURE_TESTS_FAILED'),
        ('LocalStackSecretTests', 'COMPOSE_LOCAL_STACK_SECRET_TESTS_FAILED'),
    ):
        if name in compose_families:
            matches.append(token)
    # Only a Compose failure's own xUnit error-message line may establish its
    # startup timeout; unrelated log lines must not be attributed to the test.
    lines = text.splitlines()
    for index, line in enumerate(lines[:-2]):
        if not re.match(r'^\s*Failed\s+Integration\.Compose\.', line):
            continue
        if not next(_compose_failure_records(line), None) or lines[index + 1].strip() != 'Error Message:':
            continue
        detail_line = lines[index + 2].strip()
        stage, separator, detail = detail_line.partition(': ')
        if separator and stage in ('operation', 'initial startup') and 'LOCAL_START_TIMEOUT' in detail.split(', '):
            if 'LOCAL_START_TIMEOUT' not in matches:
                matches.append('LOCAL_START_TIMEOUT')
        record = next(_compose_failure_records(line), None)
        if record == ('LocalStackTests', 'Api_uses_trusted_tls_backchannel_with_unchanged_public_issuer'):
            safe_stage = re.fullmatch(
                r'reasonToken=(COMPOSE_TLS_STARTUP_FAILED|COMPOSE_TLS_DISCOVERY_FAILED|COMPOSE_TLS_MANAGEMENT_FAILED) '
                r'correlationId=[0-9a-f]{32}( cause=LOCAL_START_TIMEOUT)?', detail_line)
            if safe_stage:
                if safe_stage.group(1) not in matches:
                    matches.append(safe_stage.group(1))
                if safe_stage.group(2) and safe_stage.group(1) == 'COMPOSE_TLS_STARTUP_FAILED' and 'LOCAL_START_TIMEOUT' not in matches:
                    matches.append('LOCAL_START_TIMEOUT')
    if 'Container operation failed: build exit 1' in text:
        matches.append('CONTAINER_IMAGE_BUILD_FAILED')
    process_failures = {
        'TEST_RUN_ABORTED': ('Test Run Aborted','test run was aborted'),
        'TESTHOST_FAILED': ('testhost','Test host process'),
        'MSBUILD_FAILED': (' error MSB','MSBUILD : error'),
        'DOCKER_UNAVAILABLE': ('Cannot connect to the Docker daemon','docker: not found'),
        'PERMISSION_FAILED': ('Permission denied','UnauthorizedAccessException'),
        'DISK_EXHAUSTED': ('No space left on device',),
        'OUT_OF_MEMORY': ('Out of memory','OutOfMemoryException'),
        'NO_TESTS_MATCHED': ('No test matches the given testcase filter',),
    }
    for token, markers in process_failures.items():
        if any(marker in text for marker in markers): matches.append(token)
    return matches or ['OTHER_TESTS_FAILED']

def dotnet_failure_methods(stdout, stderr):
    """Return only fixed, source-reviewed method identifiers marked FAIL by xUnit."""
    text=re.sub(r'\x1b\[[0-9;]*[A-Za-z]','',(stdout+b'\n'+stderr).decode('utf-8',errors='replace'))
    allowed=frozenset((
        'Nonlocal_hostname_fails_before_startup_without_secret_output',
        'Idp_database_initializer_creates_nonprivileged_owner',
        'Cold_import_has_two_pkce_clients_and_canonical_discovery',
        'Warm_start_preserves_realm_and_detects_drift',
        'Host_port_override_changes_discovery_consistently',
        'Api_uses_only_application_secret_as_nonroot',
        'Unreadable_or_missing_secret_fails_closed',
        'Arguments_cannot_override_file_configuration',
        'Published_images_contain_no_sdk_source_or_fixture',
        'Image_config_history_and_build_context_exclude_sentinels',
        'Postgres_missing_file_has_safe_diagnostic',
        'Root_or_invalid_uid_cannot_build_runtime',
        'Postgres_reads_private_file_then_uses_canonical_privilege_drop',
        'Keycloak_private_files_and_data_work_as_nonroot_with_management_health',
    ))
    observed=[]
    for line in text.splitlines():
        if '[FAIL]' not in line: continue
        found=re.search(r'Integration\.(?:ContainerRuntimeTests|KeycloakContainerTests)\.([A-Za-z0-9_]+)',line)
        if found and found.group(1) in allowed and found.group(1) not in observed: observed.append(found.group(1))
    for family, name in _compose_failure_records(text):
        if name in _COMPOSE_FAILURE_METHODS[family] and name not in observed: observed.append(name)
    return observed or ['OTHER']


def vitest_failure_files(command):
    """Read only a bounded job-owned report and return reviewed filenames."""
    try:
        args = [x.split('=', 1)[1] for x in command if isinstance(x, str) and x.startswith('--outputFile=')]
        if len(args) != 1: return ['OTHER']
        path = Path(args[0])
        parent = path.parent
        if (not path.is_absolute() or parent.is_symlink() or path.is_symlink()
                or not parent.name.startswith('ci-job-')
                or parent.parent.resolve() != Path(tempfile.gettempdir()).resolve()
                or not path.is_file() or path.stat().st_size > 8 * 1024 * 1024):
            return ['OTHER']
        report = json.loads(path.read_text())
        results = report['testResults']
        if not isinstance(results, list): return ['OTHER']
        found = set()
        for row in results:
            if not isinstance(row, dict) or row.get('status') != 'failed': continue
            name = row.get('name')
            if not isinstance(name, str): continue
            for allowed in _FRONTEND_TEST_FILES:
                if name.endswith('/apps/crm-web/' + allowed): found.add(allowed)
        return sorted(found) if found else ['OTHER']
    except (OSError, ValueError, TypeError, KeyError, RecursionError):
        return ['OTHER']

def execute(command, timeout=3300, env=None):
    try:
        process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   start_new_session=True, env=env)
    except OSError:
        raise JobError('CI_PREREQUISITE_MISSING') from None
    try:
        stdout, stderr = process.communicate(timeout=timeout)
    except subprocess.TimeoutExpired:
        stop_process_group(process)
        process.communicate()
        raise JobError('CI_COMMAND_TIMEOUT') from None
    except BaseException:
        stop_process_group(process)
        process.communicate()
        raise
    review_output = b''
    if tuple(command[:2]) == (sys.executable, 'scripts/contracts/compatibility.py') and process.returncode == 0:
        review_output = stdout
    elif tuple(command[:2]) == ('bash', 'scripts/check-plan3.sh'):
        sections = re.findall(rb'^===== compatibility: [^\r\n]* \(exit 0, [0-9]+s\) =====\r?\n(.*?)(?=^===== |\Z)',
                              stdout, re.MULTILINE | re.DOTALL)
        if len(sections) == 1:
            review_output = sections[0]
    if len(review_output) <= 1024 * 1024:
        for line in review_output.splitlines():
            try:
                result = json.loads(line)
                for field, basis in (('securityReview', 'maintainer-reviewed-adr-019'),
                                     ('contactReview', 'maintainer-reviewed-adr-020-021')):
                    review = result.get(field, {})
                    if (result.get('reason') == 'CONTRACT_BREAKING_REVIEWED'
                            and review.get('basis') == basis
                            and review.get('independentVerification') is False):
                        print(json.dumps({'reasonToken': 'CONTRACT_BREAKING_REVIEWED',
                                          'basis': basis,
                                          'independentVerification': False}))
                        break
            except (ValueError, TypeError, AttributeError, RecursionError):
                continue
    if process.returncode:
        # Fixed command families only: never echo argv, paths or child output.
        stages={('dotnet','restore'):'dotnet-restore',('dotnet','build'):'dotnet-build',
                ('dotnet','test'):'dotnet-test',('npm','ci'):'npm-install',
                ('docker','pull'):'image-pull',('bash','scripts/check-plan3.sh'):'plan3',
                (sys.executable,'scripts/contracts/check.py'):'contract-generated-check',
                ('bash','tests/ClientConsumers/run.sh'):'contract-consumers',
                (sys.executable,'scripts/contracts/compatibility.py'):'contract-compatibility',
                ('bash','tests/Plan3/aggregate-reporting.sh'):'plan3-aggregate-reporting',
                ('bash','tests/Plan3/ci-environment.sh'):'plan3-ci-environment',
                ('bash','tests/Plan3/clean-export.sh'):'plan3-clean-export',
                ('bash','tests/Plan3/negative-cases.sh'):'plan3-negative-cases',
                ('bash','tests/Plan3/scratch-cleanup.sh'):'plan3-scratch-cleanup',
                (sys.executable,'-m'):'python-suite',('node','-e'):'browser-launch'}
        stage=stages.get(tuple(command[:2]),'other')
        browser_prefix = ('npm','exec','--workspace','apps/crm-web','--')
        if tuple(command[:5]) == browser_prefix:
            browser_stages = {
                ('playwright','test','tests/e2e/browser-oidc.spec.ts'): 'browser-provider',
                ('playwright','test','tests/e2e/contacts.spec.ts'): 'browser-contacts',
                ('vitest','run'): 'browser-vitest',
            }
            stage = browser_stages.get(tuple(command[5:8]), browser_stages.get(tuple(command[5:7]), stage))
        diagnostic={'reasonToken':'CI_COMMAND_FAILED','commandStage':stage}
        if stage == 'browser-vitest': diagnostic['vitestFailedFiles'] = vitest_failure_files(command)
        if stage == 'plan3': diagnostic.update(plan3_failure_diagnostic(stdout,stderr))
        elif stage == 'plan3-clean-export':
            nested = plan3_failure_diagnostic(stdout,stderr)
            if nested['plan3Step'] != 'other': diagnostic.update(nested)
        elif stage=='dotnet-test':
            diagnostic['dotnetTests'] = dotnet_failure_diagnostic(stdout,stderr)
            diagnostic['dotnetMethods'] = dotnet_failure_methods(stdout,stderr)
        print(json.dumps(diagnostic))
        raise JobError('CI_COMMAND_FAILED')
    return stdout.decode().strip()


def native_suite(command, suites, name, format, output, env=None):
    try:
        execute(command, env=env)
    finally:
        if output.is_file():
            payload = output.read_text()
            suites.append({'name':name,'format':format,
                           'report':payload if format == 'trx' else json.loads(payload)})


def cleanup_resources(owner):
    """Use fresh label inspection; never infer ownership from resource names."""
    if len(owner) != 32 or any(char not in '0123456789abcdef' for char in owner):
        raise JobError('CI_RESOURCE_OWNERSHIP_INVALID')
    for kind in ('container','network','volume'):
        listing = ['docker',kind,'ls','-q']
        if kind == 'container':listing.append('-a')
        ids = execute(listing+['--filter','label=io.crm-centric.ci-owner='+owner], timeout=30).splitlines()
        for ident in ids:
            objects = json.loads(execute(['docker',kind,'inspect',ident],timeout=30))
            if len(objects) != 1:
                raise JobError('CI_RESOURCE_OWNERSHIP_INVALID')
            resource = objects[0]
            labels = resource.get('Config',{}).get('Labels',{}) if kind == 'container' else resource.get('Labels',{})
            if not labels or labels.get('io.crm-centric.ci-owner') != owner:
                continue
            command = ['docker',kind,'rm']
            if kind == 'container':command.append('-f')
            execute(command+[ident], timeout=60)


@contextmanager
def job_resources():
    # Each invocation has a fresh owner, even when run/attempt identities repeat.
    previous = os.environ.get('CI_RESOURCE_OWNER')
    owner = uuid.uuid4().hex
    os.environ['CI_RESOURCE_OWNER'] = owner
    try:
        yield
    finally:
        try:
            cleanup_resources(owner)
        finally:
            if previous is None:os.environ.pop('CI_RESOURCE_OWNER',None)
            else:os.environ['CI_RESOURCE_OWNER'] = previous


@contextmanager
def execution_mode(mode, job, evidence):
    """MVP executes real fixtures without invoking deferred scanner hooks."""
    if mode not in ('mvp','hardening'):
        raise JobError('CI_JOB_INPUT_INVALID')
    keys=('CI_IMAGE_JOB','CI_IMAGE_EVIDENCE')
    previous={key:os.environ.get(key) for key in keys}
    try:
        for key in keys:os.environ.pop(key,None)
        if mode=='hardening' and job in ('backend','compose','web-browser'):
            os.environ.update(CI_IMAGE_JOB=job,CI_IMAGE_EVIDENCE=str(evidence))
        yield
    finally:
        for key,value in previous.items():
            if value is None:os.environ.pop(key,None)
            else:os.environ[key]=value


def tool_versions():
    return {'node':execute(['node','--version']).lstrip('v'),
            'npm':execute(['npm','--version']), 'python':platform.python_version(),
            'dotnet':execute(['dotnet','--version']),
            'docker':execute(['docker','version','--format','{{.Server.Version}}']),
            'compose':execute(['docker','compose','version','--short'])}

def prepare_browser():
    execute(['npm','exec','--workspace','apps/crm-web','--','playwright','install','chromium'])
    # A real launch verifies native OS libraries and the locked browser binary.
    execute(['node','-e', "(async () => { const browser = await require('@playwright/test').chromium.launch(); await browser.close(); })().catch(() => process.exit(1))"],timeout=60)


def plan3_commands(identity, mode="hardening"):
    return [
        ['bash','tests/Plan3/aggregate-reporting.sh','--base-ref',identity.base_sha],
        ['bash','tests/Plan3/ci-environment.sh'],
        ['bash','tests/Plan3/clean-export.sh','--sha',identity.candidate_sha,
         '--base-ref',identity.base_sha,'--with-browser']+(['--mvp-reviewed-policy'] if mode=='mvp' else []),
        ['bash','tests/Plan3/negative-cases.sh','--base-ref',identity.base_sha],
        ['bash','tests/Plan3/scratch-cleanup.sh'],
    ]


def atomic_write(output, report):
    """Only sanitized reports may reach the exact artifact path, atomically."""
    output.parent.mkdir(parents=True,exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode='w', dir=output.parent,
                prefix='.'+output.stem+'-', suffix='.tmp', delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(report,stream)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary,output)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def publish_report(report, output, identity, mode="hardening"):
    from scripts.ci.summary import parse_cases
    # The validation input contains native diagnostic payloads. Keep it private
    # and outside the always-uploaded artifact tree even on cancellation.
    with tempfile.TemporaryDirectory(prefix='ci-private-validation-') as directory:
        private = Path(directory)/'native.json'
        private.write_text(json.dumps(report))
        parsed = parse_report('normalized',private,identity,mode=mode)
    for suite in report['suites']:
        if 'format' in suite:
            name = suite['name']
            cases = parse_cases(suite['format'],suite['report'])
            suite.clear(); suite.update(name=name, tests=cases)
    report.update(passed=parsed.passed,failed=parsed.failed,skipped=parsed.skipped,total=parsed.total)
    atomic_write(output,report)
    return parsed


def run_ci_python_suites(run):
    failures = []
    for name, directory in (('ci', 'tests/CI'), ('benchmark', 'tests/Benchmarks')):
        try:
            run(name, directory)
        except JobError as failure:
            failures.append(failure)
    if failures:
        raise failures[0]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('job', choices=['backend','compose','contracts-sdk','web-browser','ci-tests'])
    parser.add_argument('--mode', choices=['mvp','hardening'], default='hardening')
    parser.add_argument('--revision', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    def cancelled(signum, frame):
        raise JobError('CI_COMMAND_CANCELLED')
    signal.signal(signal.SIGTERM, cancelled)
    signal.signal(signal.SIGINT, cancelled)
    started = time.monotonic()
    correlation = str(uuid.uuid4())
    report = {'job':args.job,'status':'failure','command_id':args.job,
              'suites':[],'tools':{},'artifacts':[], 'correlation_id':correlation}
    token = 'CI_JOB_FAILED'
    try:
        revision = json.loads(args.revision.read_text())
        identity = RunIdentity(os.environ['GITHUB_RUN_ID'], os.environ['GITHUB_RUN_ATTEMPT'],
                               revision['candidate_sha'], revision['base_sha'], revision['merge_sha'])
        report['identity'] = vars(identity)
        if execute(['git','rev-parse','HEAD']) != identity.candidate_sha:
            raise JobError('CI_CANDIDATE_UNAVAILABLE')
        versions = tool_versions()
        try:
            verify_versions(versions)
        except ValueError:
            raise JobError('CI_TOOLCHAIN_MISMATCH') from None
        report['tools'] = {**versions, 'runner_image':os.environ.get('ImageOS','local'),
                           'runner_version':os.environ.get('ImageVersion','local')}
        if args.mode=='mvp' and args.job in ('backend','compose','web-browser'):
            report['image_hardening']={'status':'not-evaluated'}
        with tempfile.TemporaryDirectory(prefix='ci-job-') as directory, job_resources(), execution_mode(args.mode,args.job,Path(directory)/'image-evidence'):
            scratch = Path(directory)
            def py_suite(name, directory):
                output = scratch / (name+'.json')
                try:
                    execute([sys.executable,'-m','scripts.ci.unittest_report',directory,str(output)])
                finally:
                    if output.is_file():
                        report['suites'].append({'name':name,'tests':json.loads(output.read_text())})
            if args.job == 'ci-tests':
                run_ci_python_suites(py_suite)
            elif args.job in ('backend','compose'):
                execute(['dotnet','restore','CrmPlatform.sln','--locked-mode'])
                execute(['dotnet','build','CrmPlatform.sln','-c','Release','--no-restore'])
                category = 'Category=Compose' if args.job == 'compose' else 'Category!=Compose'
                native_suite(['dotnet','test','tests/Integration/Integration.csproj','-c','Release',
                         '--no-build','--no-restore','--filter',category,
                         '--logger','trx;LogFileName=results.trx','--logger','console;verbosity=normal',
                         '--results-directory',str(scratch)],
                             report['suites'],'compose' if args.job == 'compose' else 'non-compose',
                             'trx',scratch/'results.trx')
                if args.job == 'backend': py_suite('local','tests/Local')
            else:
                execute(['npm','ci','--ignore-scripts','--no-audit','--no-fund'])
                image = json.loads(Path('tools/contracts/toolchain.json').read_text())['dotnetImage']
                execute(['docker','pull',image])
                prepare_browser()
                if args.job == 'contracts-sdk':
                    py_suite('contract','tests/Contract')
                    execute([sys.executable,'scripts/contracts/check.py'])
                    execute(['bash','tests/ClientConsumers/run.sh'])
                    execute([sys.executable,'scripts/contracts/compatibility.py','--base-ref',identity.base_sha]+(['--mvp-reviewed-policy'] if args.mode=='mvp' else []))
                    for command in plan3_commands(identity,args.mode):
                        execute(command)
                else:
                    vitest = scratch/'vitest.json'; browser = scratch/'browser.json'; provider = scratch/'provider.json'; contacts = scratch/'contacts.json'
                    env = {**os.environ,'PLAYWRIGHT_JSON_OUTPUT_NAME':str(browser),
                           'PLAYWRIGHT_HTML_OPEN':'never', 'CI_PLAYWRIGHT_SPLIT':'1'}
                    # Plan 3 owns real API stack startup, readiness and teardown.
                    # Reporter args are supplied through the workspace-local npm config below.
                    execute(['bash','scripts/check-plan3.sh','--base-ref',identity.base_sha,'--with-browser']+(['--mvp-reviewed-policy'] if args.mode=='mvp' else []),env=env)
                    provider_env = {**os.environ, 'PLAYWRIGHT_JSON_OUTPUT_NAME':str(provider),
                                    'PLAYWRIGHT_HTML_OPEN':'never', 'CRM_REAL_OIDC_TEST':'1'}
                    execute(['npm','exec','--workspace','apps/crm-web','--','playwright','test',
                             'tests/e2e/browser-oidc.spec.ts','--project=chromium',
                             '--grep','real provider signs in isolated tenants and terminates provider session'],env=provider_env)
                    contacts_env = {**provider_env, 'PLAYWRIGHT_JSON_OUTPUT_NAME':str(contacts)}
                    execute(['npm','exec','--workspace','apps/crm-web','--','playwright','test',
                             'tests/e2e/contacts.spec.ts','--project=chromium'],env=contacts_env)
                    execute(['npm','exec','--workspace','apps/crm-web','--','vitest','run',
                             '--reporter=json','--outputFile='+str(vitest)])
                    report['suites'].extend([
                        {'name':'vitest','format':'vitest','report':json.loads(vitest.read_text())},
                        {'name':'browser','format':'playwright','report':merge_browser_evidence(
                            json.loads(browser.read_text()), json.loads(provider.read_text()), json.loads(contacts.read_text()))}])
            if args.mode=='hardening' and args.job in ('backend','compose','web-browser'):
                from scripts.ci.image_identity import verify_job_evidence
                from scripts.ci.scan import ScanError
                records = [json.loads(p.read_text()) for p in (scratch/'image-evidence').glob('*.json')]
                try:
                    verify_job_evidence(records,args.job,identity.run_id)
                except ScanError:
                    raise JobError('CI_IMAGE_EVIDENCE_INCOMPLETE') from None
                report['image_evidence'] = records
        report['status'] = 'success'
        report['elapsed_seconds'] = time.monotonic()-started
        publish_report(report,args.output,identity,mode=args.mode)
        token = 'CI_JOB_COMPLETE'
    except (JobError, ReportError, ValueError, KeyError, OSError) as failure:
        report['status'] = 'failure'
        token = failure.token if isinstance(failure,JobError) else (failure.reason_token if isinstance(failure,ReportError) else 'CI_JOB_INPUT_INVALID')
    # Retain safe discovered counts on failure; missing suites still fail aggregation.
    for suite in report['suites']:
        if 'format' in suite:
            from scripts.ci.summary import parse_cases
            name = suite['name']
            try:
                cases = parse_cases(suite['format'],suite['report'])
            except ReportError:
                cases = []
            suite.clear(); suite.update(name=name,tests=cases)
    cases = [case for suite in report['suites'] for case in suite['tests']]
    report.update(passed=sum(c['outcome']=='passed' for c in cases),
                  failed=sum(c['outcome']=='failed' for c in cases),
                  skipped=sum(c['outcome']=='skipped' for c in cases), total=len(cases))
    report['reason_token'] = token
    report['elapsed_seconds'] = time.monotonic()-started
    atomic_write(args.output,report)
    print(json.dumps({'job':args.job,'status':report['status'],'reasonToken':token,'correlationId':correlation}))
    return 0 if report['status'] == 'success' else 1

if __name__ == '__main__':
    sys.exit(main())
