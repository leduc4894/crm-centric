import subprocess
import tarfile
import tempfile
import unittest
from pathlib import Path

from scripts.ci.surface_restore import LOCK, verify


class SurfaceRestoreProfileTests(unittest.TestCase):
    def test_checked_in_profile_measures_empty_surface_restore(self):
        root = Path(__file__).resolve().parents[2]
        with tempfile.TemporaryDirectory() as tmp:
            archive = Path(tmp) / 'source.tar'
            subprocess.run(['git', 'archive', '--format=tar', '-o', str(archive), 'HEAD'], cwd=root, check=True)
            tree = Path(tmp) / 'tree'
            tree.mkdir()
            with tarfile.open(archive) as source:
                source.extractall(tree, filter='data')
            self.assertEqual(LOCK, verify(tree))
