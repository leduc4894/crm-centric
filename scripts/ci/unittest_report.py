"""Execute real unittest discovery and emit only stable identities/outcomes."""
import argparse
import contextlib
import io
import json
from pathlib import Path
import sys
import unittest

class Result(unittest.TestResult):
    def __init__(self):
        super().__init__()
        self.cases = []
    def addSuccess(self, test):
        super().addSuccess(test); self.cases.append({'id':test.id(), 'outcome':'passed'})
    def addError(self, test, err):
        super().addError(test, err); self.cases.append({'id':test.id(), 'outcome':'failed'})
    def addFailure(self, test, err):
        super().addFailure(test, err); self.cases.append({'id':test.id(), 'outcome':'failed'})
    def addSkip(self, test, reason):
        super().addSkip(test, reason); self.cases.append({'id':test.id(), 'outcome':'skipped'})
    def addExpectedFailure(self, test, err):
        super().addExpectedFailure(test, err); self.cases.append({'id':test.id(), 'outcome':'failed'})
    def addUnexpectedSuccess(self, test):
        super().addUnexpectedSuccess(test); self.cases.append({'id':test.id(), 'outcome':'failed'})
    def addSubTest(self, test, subtest, err):
        super().addSubTest(test, subtest, err)
        if err is not None:
            self.cases.append({'id':subtest.id(), 'outcome':'failed'})

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('directory'); parser.add_argument('output', type=Path)
    args = parser.parse_args()
    result = Result()
    # Raw diagnostics may contain payloads. Never publish them.
    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
        unittest.defaultTestLoader.discover(args.directory).run(result)
    args.output.write_text(json.dumps(result.cases))
    counts = {key:sum(t['outcome'] == key for t in result.cases) for key in ('passed','failed','skipped')}
    print(json.dumps({'total':len(result.cases), **counts}))
    return 0 if result.cases and result.wasSuccessful() and not counts['skipped'] else 1

if __name__ == '__main__':
    sys.exit(main())
