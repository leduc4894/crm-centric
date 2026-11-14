"""Policy boundaries; removing checks must turn these cases red."""
import contextlib
from dataclasses import replace
from datetime import datetime, timezone, timedelta
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest

from scripts.ci import scan
from scripts.ci.run_safe import private_command, SafeProcessError
from scripts.ci.summary import RunIdentity

NOW = datetime(2026, 11, 11, tzinfo=timezone.utc)
RUN = RunIdentity('42', '1', 'a'*40, 'b'*40, None)

class SecurityPolicyTests(unittest.TestCase):
    def test_missing_class_or_tool_gap_prevents_repository_pass(self):
        summary=scan.ScanSummary(RUN,'pass','CI_SCAN_OK','00000000-0000-4000-8000-000000000000',scanned_count=1)
        complete={kind:summary for kind in ['semgrep','trivy','gitleaks']}
        self.assertEqual('pass',scan.combine_scans(complete,(),RUN).status)
        self.assertEqual('incomplete',scan.combine_scans({'semgrep':summary},(),RUN).status)
        self.assertEqual('incomplete',scan.combine_scans(complete,('CI_TOOL_DEPENDENCY_COVERAGE',),RUN).status)
        complete['trivy']=replace(summary,identity=replace(RUN,run_attempt='2'))
        self.assertEqual('incomplete',scan.combine_scans(complete,(),RUN).status)

    def test_severity_and_secret_verification_never_make_unknown_safe(self):
        for severity in ['HIGH', 'CRITICAL']:
            self.assertEqual('blocked', scan.verdict('vulnerability', severity))
        for status in ['verified', 'unverified', 'revoked', 'offline']:
            self.assertEqual('blocked', scan.verdict('secret', 'LOW', verification=status))
        self.assertEqual('incomplete', scan.verdict('vulnerability', 'UNRECOGNIZED'))
        self.assertEqual('pass', scan.verdict('sast', 'LOW'))

    def test_license_allow_deny_unknown(self):
        self.assertEqual('pass', scan.license_verdict(['MIT', 'Apache-2.0']))
        for licenses in [[], ['UNKNOWN'], ['GPL-3.0-only'], ['MIT OR imaginary']]:
            self.assertEqual('review-required', scan.license_verdict(licenses))

    def test_database_freshness_uses_upstream_not_download(self):
        for metadata in [{}, {'DownloadedAt': NOW.isoformat()}, {'UpdatedAt': (NOW-timedelta(hours=25)).isoformat()}, {'UpdatedAt': (NOW+timedelta(hours=1)).isoformat()}]:
            with self.assertRaises(scan.ScanError): scan.require_fresh(metadata, NOW)
        scan.require_fresh({'UpdatedAt': (NOW-timedelta(hours=23)).isoformat()}, NOW)

    def test_exception_requires_exact_trusted_approval_and_expiry(self):
        finding = '1'*64
        entry = dict(finding_id=finding, owner='security-team', reason='reviewed false positive', expires='2026-11-12T00:00:00Z')
        approval=hashlib.sha256(json.dumps(entry,sort_keys=True,separators=(',',':')).encode()).hexdigest()
        entry['approval']=approval
        self.assertTrue(scan.exception_applies(finding, [entry], {approval}, NOW))
        for changed in [dict(entry, finding_id='*'), dict(entry, expires='2026-11-10T00:00:00Z'), dict(entry, approval='3'*64), dict(entry, owner=''), dict(entry, reason=''), dict(entry, expires='2027-01-01T00:00:00Z')]:
            self.assertFalse(scan.exception_applies(finding, [changed], {approval}, NOW))
        self.assertFalse(scan.exception_applies(finding, [entry], set(), NOW))

    def test_missing_or_partial_inventory_cannot_pass(self):
        with self.assertRaises(scan.ScanError): scan.require_inventory({'a','b'}, {'a'})
        with self.assertRaises(scan.ScanError): scan.require_inventory(set(), set())
        scan.require_inventory({'a'}, {'a'})

    def test_parsers_reject_empty_malformed_or_unrecognized_reports(self):
        for kind, raw in [('semgrep', {}), ('semgrep', {'results': [], 'errors': [{'message':'sensitive'}], 'paths':{'scanned':['a.cs']}}), ('trivy', {}), ('gitleaks', {})]:
            with self.subTest(kind=kind), self.assertRaises(scan.ScanError): scan.parse_findings(kind, raw)

    def test_package_inventory_cannot_accept_partial_transitive_discovery(self):
        expected={'package-lock.json':frozenset({('lodash','4.17.19'),('transitive','1.0.0')})}
        raw={'Results':[{'Class':'lang-pkgs','Target':'package-lock.json','Packages':[{'Name':'lodash','Version':'4.17.19'}]}]}
        with self.assertRaises(scan.ScanError): scan.require_package_inventory(raw,expected)
        raw['Results'][0]['Packages'].append({'Name':'transitive','Version':'1.0.0'})
        scan.require_package_inventory(raw,expected)

    def test_ecosystem_package_names_preserve_punctuation_and_sbom_identity(self):
        from scripts.ci.sbom import source_sbom
        cases=[('package-lock.json',{'lockfileVersion':3,'packages':{'node_modules/example.pkg':{'version':'1.0.0'}}},'example.pkg','example-pkg','npm'),
               ('packages.lock.json',{'dependencies':{'net10.0':{'Microsoft.EntityFrameworkCore.Abstractions':{'resolved':'1.0.0'}}}},'microsoft.entityframeworkcore.abstractions','microsoft-entityframeworkcore-abstractions','nuget')]
        for filename,raw,name,wrong,kind in cases:
            with self.subTest(kind=kind),tempfile.TemporaryDirectory() as directory:
                path=Path(directory)/filename;path.write_text(json.dumps(raw))
                expected=scan.lock_packages(path)
                self.assertEqual(expected,frozenset({(name,'1.0.0')}))
                sbom=source_sbom({filename:expected},RUN,'1.0.0')
                self.assertEqual(sbom['components'][0]['purl'],'pkg:'+kind+'/'+name+'@1.0.0')
                report={'Results':[{'Class':'lang-pkgs','Target':filename,'Packages':[{'Name':wrong,'Version':'1.0.0'}]}]}
                with self.assertRaises(scan.ScanError):scan.require_package_inventory(report,{filename:expected})
                report['Results'][0]['Packages'][0]['Name']=name.upper()
                scan.require_package_inventory(report,{filename:expected})
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'requirements.txt';path.write_text('Example_Pkg.Name==1.0.0\n')
            self.assertEqual(scan.lock_packages(path),frozenset({('example-pkg-name','1.0.0')}))

    def test_trivy_separate_license_sections_do_not_hide_packages(self):
        raw={'SchemaVersion':2,'Results':[
            {'Target':'package-lock.json','Class':'lang-pkgs','Packages':[{'Name':'example','Version':'1.0.0','Licenses':['GPL-3.0-only']}], 'Vulnerabilities':[{'Severity':'HIGH'}]},
            {'Target':'package-lock.json','Class':'license','Licenses':[{'Name':'GPL-3.0-only'}]},
            {'Target':'Loose File License(s)','Class':'license-file'}]}
        findings,inventory=scan.parse_findings('trivy',raw)
        self.assertEqual({'package-lock.json'},inventory)
        self.assertTrue(any(f['kind']=='vulnerability' and f['verdict']=='blocked' for f in findings))
        self.assertTrue(any(f['kind']=='license' and f['verdict']=='review-required' for f in findings))

    def test_secret_identity_preserves_path_rule_and_location(self):
        record={'RuleID':'fixture-rule','File':'first.txt','StartLine':1,'Secret':'REDACTED'}
        records=[record,dict(record,File='second.txt'),dict(record,StartLine=2),dict(record,RuleID='another-rule')]
        findings,_=scan.parse_findings('gitleaks',records)
        self.assertEqual(4,len({row['finding_id'] for row in findings}))
        self.assertTrue(all(row['verdict']=='blocked' for row in findings))

    def test_normalization_drops_every_scanner_owned_string(self):
        raw = [{'RuleID':'private\nSENTINEL', 'Secret':'SENTINEL', 'Match':'U0VOVElORUw=', 'File':'SENTINEL', 'StartLine':1, 'Verified':False}]
        findings, inventory = scan.parse_findings('gitleaks', raw)
        encoded=json.dumps(findings)
        self.assertNotIn('SENTINEL',encoded)
        self.assertNotIn('U0VOVElORUw=',encoded)
        self.assertEqual('blocked',findings[0]['verdict'])
        self.assertRegex(findings[0]['finding_id'], '^[a-f0-9]{64}$')

class SafeExecutionTests(unittest.TestCase):
    def test_raw_failure_and_parser_failure_never_escape_private_directory(self):
        sentinel='SENTINEL\nU0VOVElORUw=\\n'
        out=io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
            with self.assertRaises(SafeProcessError) as failed:
                with private_command(['/usr/bin/python3','-c', 'import sys; print('+repr(sentinel)+'); sys.exit(2)']): pass
        self.assertNotIn('SENTINEL',out.getvalue()+str(failed.exception))
        with private_command(['/usr/bin/python3','-c', 'print('+repr(sentinel)+')']) as result:
            path=result.stdout
            self.assertEqual(0o700,path.parent.stat().st_mode & 0o777)
            self.assertEqual(0o600,path.stat().st_mode & 0o777)
        self.assertFalse(path.exists())

    def test_missing_scanner_and_timeout_are_safe(self):
        for cmd, timeout in [(['/no/scanner'],1),(['/usr/bin/python3','-c','import time; time.sleep(5)'],0.01)]:
            with self.assertRaises(SafeProcessError) as failure:
                with private_command(cmd,timeout=timeout): pass
            self.assertEqual('CI_SCAN_INCOMPLETE',failure.exception.reason_token)

    def test_malformed_scanner_json_is_normalized_without_raw_diagnostics(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); exe=root/'scanner'
            (root/'a.ts').write_text('export const safe = 1;')
            exe.write_text('#!/usr/bin/python3\nimport sys\nif "--version" in sys.argv: print("1.185.0")\nelse: print("SENTINEL\\nU0VOVElORUw=")\n')
            exe.chmod(0o700)
            config=scan.ScannerConfig('semgrep',exe,hashlib.sha256(exe.read_bytes()).hexdigest(),'1.185.0',root/'rules.yml')
            result=scan.run_scanner(config,scan.ScanInput(root,frozenset({'a.ts'})),RUN)
            self.assertEqual('incomplete',result.status)
            published=json.dumps(result.to_dict())
            self.assertNotIn('SENTINEL',published)
            self.assertNotIn('U0VOVElORUw=',published)

    def test_feed_failure_cannot_fall_back_to_cached_or_zero_results(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);exe=root/'scanner';cache=root/'cache'
            (cache/'db').mkdir(parents=True)
            (cache/'db/metadata.json').write_text(json.dumps({'UpdatedAt':datetime.now(timezone.utc).isoformat()}))
            exe.write_text('#!/usr/bin/python3\nimport sys\nif "--version" in sys.argv: print("Version: 0.76.2")\nelse: print("SENTINEL"); sys.exit(2)\n');exe.chmod(0o700)
            config=scan.ScannerConfig('trivy',exe,hashlib.sha256(exe.read_bytes()).hexdigest(),'0.76.2',cache=cache)
            result=scan.run_scanner(config,scan.ScanInput(root,frozenset({'package-lock.json'})),RUN)
            self.assertEqual('incomplete',result.status)
            self.assertNotIn('SENTINEL',json.dumps(result.to_dict()))

    def test_dependency_staging_normalizes_python_and_reports_tool_gap(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            (root/'requirements-contracts.lock').write_text('requests==2.0.0\n')
            (root/'package-lock.json').write_text('{"lockfileVersion":3,"packages":{"node_modules/example":{"version":"1.0.0"}}}')
            (root/'dotnet-tools.json').write_text('{"tools":{}}')
            with scan.dependency_inputs(root) as staged:
                self.assertEqual({'package-lock.json','ci-normalized/requirements-contracts/requirements.txt'},set(staged.expected_inventory))
                self.assertEqual(('CI_TOOL_DEPENDENCY_COVERAGE',),staged.gaps)
                self.assertTrue((staged.root/'ci-normalized/requirements-contracts/requirements.txt').exists())

    def test_export_subst_cannot_change_committed_scanner_input(self):
        import subprocess
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            for args in [['init','-q'],['config','user.name','test'],['config','user.email','test@example.invalid']]:
                subprocess.run(['/usr/bin/git','-C',str(root),*args],check=True,capture_output=True)
            (root/'.gitattributes').write_text('a.ts export-subst\n')
            (root/'a.ts').write_text('export const hash = "$Format:%H$";\n')
            subprocess.run(['/usr/bin/git','-C',str(root),'add','.'],check=True,capture_output=True)
            subprocess.run(['/usr/bin/git','-C',str(root),'commit','-qm','fixture'],check=True,capture_output=True)
            sha=subprocess.check_output(['/usr/bin/git','-C',str(root),'rev-parse','HEAD'],text=True).strip()
            with self.assertRaises(scan.ScanError):
                with scan.candidate_tree(root,RunIdentity('42','1',sha,sha,None)): pass

    def test_unsupported_yaml_lock_or_unlocked_node_project_never_disappears(self):
        for extra,content in [('pnpm-lock.yaml','lockfileVersion: 9.0\n'),('standalone/package.json','{"name":"standalone","dependencies":{"example":"1.0.0"}}'),('requirements.in','example>=1.0.0\n'),('pyproject.toml','[project]\nname = "example"\n'),('standalone/Example.csproj','<Project />')]:
            with self.subTest(extra=extra),tempfile.TemporaryDirectory() as directory:
                root=Path(directory)
                (root/'requirements.txt').write_text('requests==2.0.0\n')
                path=root/extra;path.parent.mkdir(parents=True,exist_ok=True);path.write_text(content)
                with scan.dependency_inputs(root) as inputs:
                    self.assertTrue(inputs.gaps)
                    report=scan.ScanSummary(RUN,'pass','CI_SCAN_OK','00000000-0000-4000-8000-000000000000',scanned_count=1)
                    self.assertEqual('incomplete',scan.combine_scans({name:report for name in ['semgrep','trivy','gitleaks']},inputs.gaps,RUN).status)

    def test_node_workspace_manifest_requires_exact_lock_entry(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'workspace').mkdir()
            manifest={'name':'workspace','version':'1.0.0','dependencies':{'example':'1.0.0'}}
            (root/'workspace/package.json').write_text(json.dumps(manifest))
            (root/'package-lock.json').write_text(json.dumps({'lockfileVersion':3,'packages':{'workspace':manifest,'node_modules/example':{'version':'1.0.0'}}}))
            with scan.dependency_inputs(root) as inputs: self.assertFalse(inputs.gaps)
            manifest['dependencies']['extra']='1.0.0'
            (root/'workspace/package.json').write_text(json.dumps(manifest))
            with scan.dependency_inputs(root) as inputs: self.assertIn('CI_UNLOCKED_DEPENDENCIES',inputs.gaps)

    def test_candidate_archive_does_not_follow_untracked_or_symlink_inputs(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            import subprocess
            for args in [['init','-q'],['config','user.name','test'],['config','user.email','test@example.invalid']]:
                subprocess.run(['/usr/bin/git','-C',str(root),*args],check=True,capture_output=True)
            (root/'a.ts').write_text('export const safe = 1;')
            (root/'link.ts').symlink_to('/etc/passwd')
            subprocess.run(['/usr/bin/git','-C',str(root),'add','.'],check=True,capture_output=True)
            subprocess.run(['/usr/bin/git','-C',str(root),'commit','-qm','fixture'],check=True,capture_output=True)
            sha=subprocess.check_output(['/usr/bin/git','-C',str(root),'rev-parse','HEAD'],text=True).strip()
            run=RunIdentity('42','1',sha,sha,None)
            with self.assertRaises(scan.ScanError):
                with scan.candidate_tree(root,run): pass
            subprocess.run(['/usr/bin/git','-C',str(root),'rm','-q','link.ts'],check=True,capture_output=True)
            subprocess.run(['/usr/bin/git','-C',str(root),'commit','-qm','remove link'],check=True,capture_output=True)
            sha=subprocess.check_output(['/usr/bin/git','-C',str(root),'rev-parse','HEAD'],text=True).strip()
            run=replace(run,candidate_sha=sha)
            (root/'untracked.ts').write_text('eval("bad")')
            with scan.candidate_tree(root,run) as tree:
                self.assertTrue((tree/'a.ts').exists())
                self.assertFalse((tree/'untracked.ts').exists())

    def test_run_scanner_checksum_mismatch_fails_before_execution(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); executable=root/'scanner'
            executable.write_text('#!/bin/sh\necho SENTINEL\n'); executable.chmod(0o700)
            config=scan.ScannerConfig('gitleaks',executable,'0'*64,'8.30.1')
            result=scan.run_scanner(config,scan.ScanInput(root),RUN)
            self.assertEqual('incomplete',result.status)
            self.assertEqual('CI_SCAN_INCOMPLETE',result.reason_token)
            self.assertNotIn('SENTINEL',json.dumps(result.to_dict()))


class MvpScanTests(unittest.TestCase):
    def report(self, status='pass', findings=()):
        return scan.ScanSummary(RUN,status,'CI_SCAN_OK' if status=='pass' else 'CI_SCAN_FINDINGS',
                                '00000000-0000-4000-8000-000000000000',findings,1)

    def test_required_reports_and_only_tool_gap_can_be_deferred(self):
        reports={kind:self.report() for kind in ('gitleaks','trivy')}
        result=scan.combine_mvp_scans(reports,('CI_TOOL_DEPENDENCY_COVERAGE',),RUN)
        self.assertEqual('pass',result.status)
        for gap in ('CI_ECOSYSTEM_COVERAGE','CI_UNLOCKED_DEPENDENCIES'):
            self.assertEqual('incomplete',scan.combine_mvp_scans(reports,(gap,),RUN).status)
        for bad in ({'gitleaks':self.report()},dict(reports,trivy=replace(self.report(),identity=replace(RUN,run_attempt='2'))),dict(reports,trivy=replace(self.report(),status='incomplete'))):
            self.assertEqual('incomplete',scan.combine_mvp_scans(bad,(),RUN).status)

    def test_unreviewed_findings_and_candidate_waiver_cannot_pass(self):
        finding={'finding_id':'a'*64,'kind':'secret','verdict':'blocked'}
        reports={'gitleaks':self.report('blocked',(finding,)),'trivy':self.report()}
        self.assertEqual('blocked',scan.combine_mvp_scans(reports,(),RUN).status)
        reports['gitleaks']=self.report('pass',(dict(finding,approval='candidate'),))
        self.assertEqual('incomplete',scan.combine_mvp_scans(reports,(),RUN).status)
        reports['gitleaks']=self.report()
        reports['trivy']=self.report('blocked',({'finding_id':'b'*64,'kind':'vulnerability','verdict':'blocked'},))
        self.assertEqual('blocked',scan.combine_mvp_scans(reports,(),RUN).status)

if __name__ == '__main__': unittest.main()
