import tempfile
import unittest
from pathlib import Path

from scripts.contracts import sdk_surface


class SdkLocalDependencyTests(unittest.TestCase):
    def test_surface_snapshot_includes_local_locked_archive(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / 'source'
            archive = source / 'tools/contracts/vendor/picomatch/picomatch-4.0.7-crm-backport.tgz'
            archive.parent.mkdir(parents=True)
            archive.write_bytes(b'locked-archive')
            contracts = root / 'contracts'
            contracts.mkdir()
            snapshot = sdk_surface.prepare(root / 'snapshot', source, contracts, None)
            self.assertEqual(b'locked-archive', (snapshot / archive.relative_to(source)).read_bytes())


if __name__ == '__main__':
    unittest.main()
