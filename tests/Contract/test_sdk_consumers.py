"""Exercise SDKs through installed local packages and a captured HTTP boundary."""
import os
import pathlib
import subprocess
import tempfile
import unittest

import prerequisites

ROOT = pathlib.Path(__file__).resolve().parents[2]

# The packaged consumers need the installed node_modules and the pinned .NET
# image in the local docker image store. A checkout without those prerequisites
# skips this module with the reason of the missing prerequisite; it never fails
# for a missing tool it never provisioned.
@unittest.skipUnless(prerequisites.CONSUMERS[0], prerequisites.CONSUMERS[1])
class PackagedConsumers(unittest.TestCase):
    def test_independent_packaged_consumers(self):
        # A hosted npm ci has tarballs but no guaranteed registry metadata.
        # The independent consumer must work with an entirely fresh npm cache.
        with tempfile.TemporaryDirectory(prefix='consumer-npm-cache-') as cache:
            env={**os.environ,'npm_config_cache':cache}
            result = subprocess.run(['bash', str(ROOT / 'tests/ClientConsumers/run.sh')], cwd=ROOT,
                                    capture_output=True, text=True, env=env)
        self.assertEqual(result.returncode, 0, 'Packaged consumer verification failed (output suppressed to protect diagnostics).')
        self.assertIn('PACKAGED_CONSUMERS_OK', result.stdout)
        self.assertNotIn('synthetic-secret-sentinel-not-a-credential', result.stdout + result.stderr)

if __name__ == '__main__':
    unittest.main()
