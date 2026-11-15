"""Real Docker/Trivy image positive; unavailable prerequisites fail, never skip."""
import json
from pathlib import Path
import tempfile
import unittest
import uuid
from scripts.ci.image_identity import from_inspect,require_evidence
from scripts.ci.scan import _run_json, scan_image,ScanError
from scripts.ci.run_safe import private_command
from test_real_scanners import config

class RealImageTests(unittest.TestCase):
    def test_vulnerable_synthetic_image_blocks_with_nonempty_sbom(self):
        tag='crm-ci-image-positive-'+uuid.uuid4().hex
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            (root/'Dockerfile').write_text('FROM scratch\nCOPY package.json /app/node_modules/lodash/package.json\n')
            (root/'package.json').write_text(json.dumps({'name':'lodash','version':'4.17.19','license':'MIT'}))
            try:
                with private_command(['docker','build','--tag',tag,str(root)],timeout=300): pass
                identity=from_inspect('compose','6',_run_json(['docker','image','inspect',tag])[0])
                result=scan_image(config('trivy'),identity)
                self.assertEqual('blocked',result['status'])
                self.assertGreater(result['packages'],0)
                self.assertGreater(len(result['sbom']['components']),0)
                self.assertTrue(any(f['verdict']=='blocked' for f in result['findings']))
                with self.assertRaises(ScanError): require_evidence([identity],[result])
            finally:
                with private_command(['docker','image','rm','--force',tag],accepted=(0,1)): pass

    def test_actual_started_container_binds_config_identity(self):
        from scripts.ci.image_identity import observe_container
        from scripts.ci.run_safe import private_command
        from test_real_scanners import REPO
        pins=dict(line.split('=',1) for line in (REPO/'infra/compose/images.lock.env').read_text().splitlines() if line and not line.startswith('#'))
        name='crm-ci-observe-'+uuid.uuid4().hex
        try:
            with private_command(['docker','run','-d','--name',name,'--network=none','--entrypoint','/bin/sh',pins['DOTNET_RUNTIME_IMAGE'],'-c','sleep 60'],timeout=300) as result:
                container=result.stdout.read_text().strip()
            identity=observe_container('compose','6',container)
            actual=_run_json(['docker','container','inspect',container])[0]['Image']
            self.assertEqual(actual,identity.imageId)
            self.assertIsNone(identity.manifestDigest)
            self.assertEqual('linux/amd64',identity.platform)
        finally:
            with private_command(['docker','container','rm','-f',name],accepted=(0,1)): pass
