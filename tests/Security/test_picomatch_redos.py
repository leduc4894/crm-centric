import os
import subprocess
import unittest


class PicomatchSecurityTests(unittest.TestCase):
    def test_repeated_wildcards_do_not_block_event_loop(self):
        patterns = ('+(*)', '+(*a)', '*(*a)', '+(**a)', '+(*{a,b})',
                    '+(*a|b)', '+(**a|b)', '+(a*|b*)', '+(a|**a)', '+(*a|z*)')
        for pattern in patterns:
            with self.subTest(pattern=pattern):
                result = subprocess.run(
                    ['node', '-e', 'require("picomatch")(process.argv[1])("a".repeat(30)+"/")', pattern],
                    stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=2, check=False,
                    env=os.environ.copy())
                self.assertEqual(0, result.returncode, result.stderr.decode(errors='replace'))

    def test_ordinary_globs_keep_matching(self):
        code = '''const p = require("picomatch");
          if (!p("src/**/*.ts")("src/app/example.ts") ||
              p("src/**/*.ts")("src/app/example.js") ||
              !p("+(foo|bar).ts")("foobar.ts")) process.exit(1);'''
        result = subprocess.run(['node', '-e', code], stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, timeout=2, check=False)
        self.assertEqual(0, result.returncode, result.stderr.decode(errors='replace'))

    def test_legacy_opt_out_cannot_disable_wildcard_guard(self):
        for pattern in ('+(*a)', '+(*a|b)'):
            with self.subTest(pattern=pattern):
                result = subprocess.run(
                    ['node', '-e', 'require("picomatch")(process.argv[1], {maxExtglobRecursion:false})("a".repeat(30)+"/")', pattern],
                    stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=2, check=False)
                self.assertEqual(0, result.returncode, result.stderr.decode(errors='replace'))


if __name__ == '__main__':
    unittest.main()
