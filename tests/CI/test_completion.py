import json
from pathlib import Path
import tempfile
import unittest

class CompletionTests(unittest.TestCase):
    def test_step_failures_never_create_success_and_do_not_echo_payload(self):
        from scripts.ci.completion import complete
        for name, outcome, expected in [('setup','failure','CI_SETUP_FAILED'),('checkout','failure','CI_ACTION_FAILED'),('gate','cancelled','CI_COMMAND_CANCELLED'),('gate','skipped','CI_STEP_NON_SUCCESS')]:
            with self.subTest(name=name,outcome=outcome),tempfile.TemporaryDirectory() as directory:
                path=Path(directory)/'backend.json'
                path.write_text('sensitive-sentinel')
                token=complete('backend',path,{name:{'outcome':outcome,'outputs':{'unsafe':'sensitive-sentinel'}}})
                self.assertEqual(token,expected)
                self.assertNotIn('sensitive-sentinel',path.read_text())
                self.assertEqual(json.loads(path.read_text())['status'],'failure')

    def test_missing_gate_cannot_pass_even_with_success_setup(self):
        from scripts.ci.completion import complete
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'backend.json'
            self.assertEqual(complete('backend',path,{'setup':{'outcome':'success'}}),'CI_STEP_NON_SUCCESS')

    def test_invalid_cli_arguments_never_echo_input(self):
        import subprocess,sys
        for module in ('completion','security'):
            result=subprocess.run([sys.executable,'-m','scripts.ci.'+module,'--unsafe-sensitive-sentinel'],capture_output=True,text=True,timeout=20)
            self.assertNotEqual(result.returncode,0)
            self.assertEqual(result.stderr,'')
            self.assertNotIn('sensitive-sentinel',result.stdout)
            self.assertTrue(json.loads(result.stdout)['reasonToken'].startswith('CI_'))
