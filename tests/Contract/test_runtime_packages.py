"""Offline consumer runtime closure follows the verified npm lock."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]

class RuntimePackagesTests(unittest.TestCase):
    def test_exact_transitive_closure_and_missing_package_rejected(self):
        spec=importlib.util.spec_from_file_location('runtime_packages',ROOT/'scripts/contracts/runtime_packages.py')
        module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            def save(path,data):
                p=root/path;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(data))
            save('sdks/typescript/crm/package.json',{'dependencies':{'first':'1.0.0'}})
            save('sdks/typescript/operations/package.json',{'dependencies':{'first':'1.0.0'}})
            save('package-lock.json',{'packages':{'node_modules/first':{'version':'1.0.0','dependencies':{'second':'^2.0.0'}},'node_modules/second':{'version':'2.0.0'}}})
            save('node_modules/first/package.json',{'name':'first','version':'1.0.0','dependencies':{'second':'^2.0.0'}})
            save('node_modules/second/package.json',{'name':'second','version':'2.0.0'})
            self.assertEqual([root/'node_modules/first',root/'node_modules/second'],module.runtime_packages(root))
            save('node_modules/second/package.json',{'name':'wrong','version':'2.0.0'})
            with self.assertRaisesRegex(ValueError,'SDK_RUNTIME_PACKAGES_INVALID'):module.runtime_packages(root)
            save('node_modules/second/package.json',{'name':'second','version':'3.0.0'})
            with self.assertRaisesRegex(ValueError,'SDK_RUNTIME_PACKAGES_INVALID'):module.runtime_packages(root)
            (root/'node_modules/second/package.json').unlink()
            with self.assertRaisesRegex(ValueError,'SDK_RUNTIME_PACKAGES_INVALID'):module.runtime_packages(root)
