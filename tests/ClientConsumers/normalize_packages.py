"""Normalize ZIP timestamps of locally built NuGet artifacts before locked restore."""
from pathlib import Path
from tempfile import NamedTemporaryFile
import os
import sys
import zipfile

for path in Path(sys.argv[1]).glob('*.nupkg'):
    with zipfile.ZipFile(path) as original, NamedTemporaryFile(dir=path.parent, delete=False) as temp:
        temporary = Path(temp.name)
        with zipfile.ZipFile(temp, 'w', compression=zipfile.ZIP_DEFLATED) as normalized:
            for name in sorted(original.namelist()):
                info = original.getinfo(name)
                info.date_time = (1980, 1, 1, 0, 0, 0)
                normalized.writestr(info, original.read(name))
    os.replace(temporary, path)
