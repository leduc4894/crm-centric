"""Real SDK proof of the one supported empty NuGet target; no skips."""
import os
from pathlib import Path
import shutil
import subprocess
import json
import tempfile
import unittest
try:
    from scripts.ci import surface_restore
except ImportError:
    surface_restore=None
from scripts.ci import scan

ROOT=Path(__file__).resolve().parents[2]

class SurfaceEmptyRestoreTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(surface_restore,"Bounded empty restore verifier is missing")
    def stage(self, root):
        for name in surface_restore.PROFILE:
            destination=root/name
            destination.parent.mkdir(parents=True,exist_ok=True)
            shutil.copyfile(ROOT/name,destination)

    def test_real_pinned_restore_proves_both_configurations(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.stage(root)
            self.assertEqual(surface_restore.LOCK,surface_restore.verify(root))
            (root/'requirements.txt').write_text('example==1.0\n')
            with scan.dependency_inputs(root,measure_surface=True) as inputs:
                self.assertEqual([surface_restore.measurement_metadata()],list(inputs.empty_restores))
                self.assertNotIn(surface_restore.LOCK,inputs.expected_inventory)
                self.assertNotIn('CI_UNLOCKED_DEPENDENCIES',inputs.gaps)

    def test_unchanged_empty_lock_plus_imported_package_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.stage(root)
            lock=(root/surface_restore.LOCK).read_bytes()
            props=root/'Directory.Build.props'
            props.write_text(props.read_text().replace('</Project>','<ItemGroup><PackageReference Include="xunit" /></ItemGroup></Project>'))
            self.assertEqual(lock,(root/surface_restore.LOCK).read_bytes())
            with self.assertRaises(surface_restore.SurfaceRestoreError):surface_restore.verify(root)

    def test_new_ancestor_targets_cannot_forge_assets(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.stage(root)
            (root/'tools/Directory.Build.targets').write_text('<Project><Target Name="Restore"><Message Text="forged" /></Target></Project>')
            with self.assertRaises(surface_restore.SurfaceRestoreError):surface_restore.verify(root)

    def test_profile_changes_missing_inputs_and_automatic_configs_fail_closed(self):
        for name in surface_restore.PROFILE:
            with self.subTest(input=name),tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.stage(root)
                (root/name).unlink()
                with self.assertRaises(surface_restore.SurfaceRestoreError):surface_restore.verify(root)
        for name in ('tools/NuGet.Config','tools/contracts/global.json',
                     'tools/contracts/surface/Directory.Packages.props','MSBuild.rsp'):
            with self.subTest(input=name),tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.stage(root)
                (root/name).write_text('{}')
                with self.assertRaises(surface_restore.SurfaceRestoreError):surface_restore.verify(root)

    def test_changed_lock_and_symlink_inputs_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.stage(root)
            (root/surface_restore.LOCK).write_text('{"version":2,"dependencies":{}}')
            with self.assertRaises(surface_restore.SurfaceRestoreError):surface_restore.verify(root)
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.stage(root)
            (root/'Directory.Build.props').unlink()
            (root/'Directory.Build.props').symlink_to(ROOT/'Directory.Build.props')
            with self.assertRaises(surface_restore.SurfaceRestoreError):surface_restore.verify(root)

    def test_default_scanner_does_not_accept_empty_lock_receipt(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.stage(root)
            (root/'requirements.txt').write_text('example==1.0\n')
            with self.assertRaises(scan.ScanError):
                with scan.dependency_inputs(root):pass

    def test_safe_receipt_roundtrip_rejects_forgery_and_is_no_input_authority(self):
        from scripts.ci.summary import RunIdentity
        run=RunIdentity('surface','1','a'*40,'b'*40,None)
        receipt=surface_restore.measurement_metadata()
        evidence=scan.mvp_evidence({},(),run,[receipt])
        self.assertEqual(evidence,scan.validate_mvp_evidence(evidence,run))
        for value in (1,False):
            evidence['empty_restores'][0]['nuget_packages']=value
            with self.assertRaises(scan.ScanError):scan.validate_mvp_evidence(evidence,run)

    def test_real_sdk_automatic_user_and_obj_imports_cannot_escape_closure(self):
        for name in ('Surface.csproj.user','obj/Surface.csproj.extra.props','obj/Surface.csproj.extra.targets'):
            with self.subTest(input=name),tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.stage(root)
                lock=(root/surface_restore.LOCK).read_bytes()
                extra=root/Path(surface_restore.PROJECT).parent/name
                extra.parent.mkdir(parents=True,exist_ok=True)
                extra.write_text('<Project><ItemGroup><PackageReference Include="xunit" /></ItemGroup></Project>')
                result=subprocess.run([os.environ.get('CI_DOTNET','dotnet'),'msbuild',surface_restore.PROJECT,
                                       '-getItem:PackageReference'],cwd=root,capture_output=True,timeout=180)
                self.assertEqual(0,result.returncode,'Real SDK import evaluation failed')
                self.assertIn('xunit',[item['Identity'] for item in json.loads(result.stdout)['Items']['PackageReference']])
                self.assertEqual(lock,(root/surface_restore.LOCK).read_bytes())
                with self.assertRaises(surface_restore.SurfaceRestoreError):surface_restore.verify(root)

    def test_unknown_empty_project_retains_scanner_failure(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            (root/'Unknown.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk" />')
            (root/'packages.lock.json').write_text('{"version":1,"dependencies":{"net10.0":{}}}')
            with self.assertRaises(scan.ScanError):
                with scan.dependency_inputs(root):pass

if __name__=='__main__':unittest.main()
