"""Actual Gitleaks protects uploadable source inventory, including allowed fields."""
from pathlib import Path
import json
import tempfile
import unittest
from scripts.ci.sbom import write_source_evidence
from scripts.ci.scan import ScanError
from scripts.ci.summary import RunIdentity
from test_real_scanners import config,initialize,git

class SourceSbomSecretTests(unittest.TestCase):
    def fixture(self,root,name,release):
        base=initialize(root)
        (root/'package-lock.json').write_text(json.dumps({'lockfileVersion':3,'packages':{
            '':{'name':'fixture','version':'1.0.0'},'node_modules/'+name:{'name':name,'version':release}}}))
        # Inventory must remain incomplete; that must never bypass secret checks.
        (root/'toolchain.json').write_text('{}')
        git(root,'add','.');git(root,'commit','-qm','source fixture')
        return RunIdentity('42','1',git(root,'rev-parse','HEAD'),base,None)

    def test_real_secret_in_package_name_or_version_never_reaches_artifact(self):
        synthetic='ghp_'+'0123456789abcdefABCDEF0123456789abcdef'
        for name,release in ((synthetic,'1.0.0'),('safe',synthetic)):
            with tempfile.TemporaryDirectory() as directory:
                root=Path(directory);run=self.fixture(root,name,release)
                output=root/'artifact/source.json';output.parent.mkdir();output.write_text('stale')
                with self.assertRaises(ScanError): write_source_evidence(root,run,output,secret_scanner=config('gitleaks'))
                self.assertFalse(output.exists())

    def test_real_secret_scan_allows_clean_inventory_and_retains_gaps(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);run=self.fixture(root,'safe','1.0.0');output=root/'artifact/source.json'
            record=write_source_evidence(root,run,output,secret_scanner=config('gitleaks'))
            self.assertTrue(output.is_file())
            self.assertEqual('incomplete',record['status'])
            self.assertIn('CI_TOOL_DEPENDENCY_COVERAGE',record['coverageGaps'])
            self.assertEqual(1,len(record['sbom']['components']))
