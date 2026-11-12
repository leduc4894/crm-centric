"""Candidate-owned MVP measurement of one fixed empty restore closure.

This is local CI evidence, never independent attestation or a finding waiver.
Only hash-pinned input bytes execute; arbitrary candidate MSBuild never runs.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import uuid

LOCK='tools/contracts/surface/dotnet/packages.lock.json'
PROJECT='tools/contracts/surface/dotnet/Surface.csproj'
SDK='10.0.403'
PROFILE={'global.json': 'bd2f7421218da3954279a68a63015aeabda5b7122ec8b47a2f7585b7e284a98d',
 'Directory.Build.props': '2e049cd5d207ac34dcdd6caeee41ca3fd67c65a550c551387142c01880276763',
 'Directory.Packages.props': '2e2a436556f6cc786eeff91afaf94aadd7e6f6b09b7fa6dd2783991fd3bd2448',
 'tools/contracts/surface/dotnet/Surface.csproj': '4bed4f2743d82801c717756ab03c3f77e8fe02ee99055cb5d9fdbac66ea7252f',
 'tools/contracts/surface/dotnet/packages.lock.json': '03eeadc5ef377c17f787ab65f41fb4c8a9c936bb7f7f4171111fdeec8a81cb46'}

class SurfaceRestoreError(Exception):
    def __init__(self):
        self.reason_token='CI_SCAN_INCOMPLETE'
        self.correlation_id=str(uuid.uuid4())
        super().__init__(self.reason_token)


def verify(tree):
    """Measure actual offline restore, not a caller assertion or assets file.

    Host SDK installation is trusted just as other pinned CI tool installations
    are. Neither this candidate-owned profile nor its result authenticates policy.
    """
    try:
        tree=Path(tree).resolve()
        inputs={}
        for name,digest in PROFILE.items():
            source=tree/name
            if any(p.is_symlink() for p in (source,*source.parents) if p!=tree.parent):
                raise SurfaceRestoreError()
            data=source.read_bytes()
            if hashlib.sha256(data).hexdigest()!=digest:raise SurfaceRestoreError()
            inputs[name]=data
        # Reviewed ancestor closure: reject all additional automatic MSBuild,
        # NuGet, SDK selector and response inputs, including case variants.
        ancestors=[tree,*(tree/p for p in ('tools','tools/contracts','tools/contracts/surface','tools/contracts/surface/dotnet'))]
        for directory in ancestors:
            for source in directory.iterdir():
                lower=source.name.lower()
                effective=(lower.startswith('directory.build.') or lower=='directory.packages.props'
                           or lower in ('nuget.config','global.json') or lower.endswith('.rsp'))
                if effective and source.relative_to(tree).as_posix() not in PROFILE:
                    raise SurfaceRestoreError()
        # SDK imports project.user and project-specific obj/*.props/targets,
        # beyond ancestor Directory.Build.*. Bound the entire project subtree
        # instead of guessing which extra file names MSBuild might import.
        # Program.cs is the only permitted non-profile file; restore never
        # copies or compiles it. No directories, generated obj, tasks or assets.
        permitted=set(PROFILE)|{'tools/contracts/surface/dotnet/Program.cs'}
        for source in (tree/Path(PROJECT).parent).rglob('*'):
            if source.is_symlink() or not source.is_file() or source.relative_to(tree).as_posix() not in permitted:
                raise SurfaceRestoreError()
        executable=shutil.which(os.environ.get('CI_DOTNET','dotnet'))
        if not executable:raise SurfaceRestoreError()
        executable=str(Path(executable).resolve())
        with tempfile.TemporaryDirectory(prefix='ci-surface-') as directory:
            root=Path(directory)
            for name,data in inputs.items():
                output=root/name;output.parent.mkdir(parents=True,exist_ok=True);output.write_bytes(data)
            home=root/'home';home.mkdir()
            config=root/'offline.config'
            config.write_text('<configuration><packageSources><clear /></packageSources></configuration>')
            environment={'PATH':os.defpath,'HOME':str(home),'DOTNET_CLI_HOME':str(home),
                         'DOTNET_ROOT':str(Path(executable).parent),'DOTNET_MULTILEVEL_LOOKUP':'0',
                         'DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_SKIP_FIRST_TIME_EXPERIENCE':'1',
                         'LANG':'C.UTF-8','LC_ALL':'C.UTF-8','TMPDIR':str(root)}
            def run(arguments):
                result=subprocess.run([executable,*arguments],cwd=root,env=environment,
                                      stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=180)
                if result.returncode:raise SurfaceRestoreError()
                return result.stdout
            if run(['--version']).strip()!=SDK.encode():raise SurfaceRestoreError()
            for configuration in ('Debug','Release'):
                obj=root/'obj'/configuration
                run(['restore',PROJECT,'--locked-mode','--force-evaluate','--no-cache',
                     '--disable-build-servers','--configfile',str(config),
                     '-p:Configuration='+configuration,'-p:TargetFramework=net10.0',
                     '-p:NuGetAudit=false','-p:RestorePackagesPath='+str(root/'packages'),
                     '-p:MSBuildProjectExtensionsPath='+str(obj)+'/',
                     '-p:DirectoryBuildPropsPath='+str(root/'Directory.Build.props'),
                     '-p:ImportDirectoryBuildTargets=false'])
                if (root/LOCK).read_bytes()!=inputs[LOCK]:raise SurfaceRestoreError()
                assets=json.loads((obj/'project.assets.json').read_text())
                if (assets.get('targets')!={'net10.0':{}} or assets.get('libraries')!={}
                    or assets.get('projectFileDependencyGroups')!={'net10.0':[]}
                    or set(assets['project']['frameworks'])!={'net10.0'}
                    or assets['project']['restore']['frameworks']['net10.0'].get('projectReferences')!={}):
                    raise SurfaceRestoreError()
        return LOCK
    except (OSError,ValueError,KeyError,TypeError,subprocess.SubprocessError):
        raise SurfaceRestoreError() from None


def measurement_metadata():
    """Safe bounded receipt; descriptive local evidence, never caller authority."""
    return {'status':'measured-local','profile':'surface-net10-v1','sdk':SDK,
            'profile_sha256':hashlib.sha256(json.dumps(PROFILE,sort_keys=True,separators=(',',':')).encode()).hexdigest(),
            'target_sha256':hashlib.sha256(LOCK.encode()).hexdigest(),
            'configurations':['Debug','Release'],'nuget_packages':0}
