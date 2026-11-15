"""Real pinned SDK checks for standalone fixture dependency closure; never skip.

Set CI_DOTNET to the provisioned pinned SDK executable when it is not on PATH.
Native tool diagnostics are captured and never returned by these assertions.
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
FIXTURE=Path('tests/Contract/fixtures/sdk/CorvusProbe.csproj')

class DotnetLockfileTests(unittest.TestCase):
    def run_dotnet(self, root, *args):
        executable=os.environ.get('CI_DOTNET','dotnet')
        return subprocess.run([executable,*args],cwd=root,stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE,timeout=180)

    def stage(self, root):
        for name in ('global.json','Directory.Build.props','Directory.Packages.props'):
            shutil.copyfile(ROOT/name,root/name)
        for name in ('sdks/dotnet/Crm.Client','tests/Contract/fixtures/sdk'):
            shutil.copytree(ROOT/name,root/name,ignore=shutil.ignore_patterns('bin','obj'))

    def test_application_central_versions_remain_enabled(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.stage(root)
            project=Path('src/Platform.Api/Platform.Api.csproj')
            (root/project).parent.mkdir(parents=True)
            shutil.copyfile(ROOT/project,root/project)
            for path,expected in ((project,b'true'),(FIXTURE,b'false'),(Path('sdks/dotnet/Crm.Client/Crm.Client.csproj'),b'false')):
                result=self.run_dotnet(root,'msbuild',str(path),'-getProperty:ManagePackageVersionsCentrally')
                self.assertEqual(0,result.returncode,'Project evaluation failed')
                self.assertEqual(expected,result.stdout.strip(),'Central version isolation escaped its scope')

    def test_fixture_locked_restore_build_and_rule_roundtrip(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.stage(root)
            self.assertTrue((root/FIXTURE.parent/'packages.lock.json').is_file(), 'Fixture must commit its own lock')
            self.assertEqual(0,self.run_dotnet(root,'restore',str(FIXTURE),'--locked-mode').returncode,'Locked fixture restore failed')
            self.assertEqual(0,self.run_dotnet(root,'build',str(FIXTURE),'--no-restore').returncode,'Fixture consumer build failed')
            result=self.run_dotnet(root,'run','--project',str(FIXTURE),'--no-restore','--no-build')
            self.assertEqual(0,result.returncode,'Fixture consumer execution failed')
            self.assertEqual(2,result.stdout.count(b'SDK_RULE_ROUNDTRIP_OK'))

    def test_fixture_lock_tamper_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.stage(root)
            lock=root/FIXTURE.parent/'packages.lock.json'
            self.assertTrue(lock.is_file(),'Fixture must commit its own lock')
            data=json.loads(lock.read_text())
            for framework in data['dependencies'].values():
                for package in framework.values():
                    if package.get('type')!='Project':package['resolved']='0.0.0'
            lock.write_text(json.dumps(data))
            self.assertNotEqual(0,self.run_dotnet(root,'restore',str(FIXTURE),'--locked-mode').returncode,'Tampered lock must fail')

if __name__=='__main__':unittest.main()
