"""Check package boundaries without printing package contents."""
import json
import pathlib
import sys
import tarfile
import zipfile

root = pathlib.Path(sys.argv[1])
for path in (root / 'packages').glob('*.tgz'):
    with tarfile.open(path) as archive:
        for member in archive.getmembers():
            if member.isfile() and member.name.endswith(('.js', '.d.ts', '.json')):
                content = archive.extractfile(member).read()
                if any(needle in content for needle in (b'Platform.Api', b'Platform.Infrastructure', b'apps/crm-web')):
                    raise SystemExit('SDK_PACKAGE_BOUNDARY_INVALID')
for path in (root / 'feed').glob('*.nupkg'):
    with zipfile.ZipFile(path) as archive:
        for name in archive.namelist():
            if name.endswith(('.nuspec', '.dll')):
                content = archive.read(name)
                if any(needle in content for needle in (b'Platform.Api', b'Platform.Infrastructure', b'apps/crm-web')):
                    raise SystemExit('SDK_PACKAGE_BOUNDARY_INVALID')
print('SDK_PACKAGE_BOUNDARY_OK')
