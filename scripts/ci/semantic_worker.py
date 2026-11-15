"""Unprivileged worker: run the selected immutable policy's semantic evaluator.

Only the independent coordinator may schedule this worker; it is never called
by the signature verifier. Its stdout is captured and normalized by the caller.
"""
import json
from pathlib import Path
import sys
import subprocess


def main():
    try:
        source, baseline, base_contract_root, candidate, base_sha, binary = map(Path, sys.argv[1:])
        sys.path.insert(0, str(source))
        from scripts.contracts.compatibility import evaluate_contract_snapshots
        # The selected policy's TypeScript surface scanner resolves its pinned
        # modules from this isolated source tree, never the host checkout.
        subprocess.run(['npm', 'ci', '--offline', '--ignore-scripts', '--no-audit', '--no-fund'],
                       cwd=source, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                       timeout=300, check=True)
        contracts = tuple(sorted((path.relative_to(base_contract_root).as_posix(), path.read_bytes())
                                 for path in base_contract_root.rglob('*') if path.is_file()))
        policy = json.loads((source / 'tools/contracts/oasdiff.json').read_bytes())
        report = evaluate_contract_snapshots(str(base_sha), [name for name, _ in contracts], candidate,
                                            {}, policy, binary, immutable_base=contracts,
                                            policy_sources=(baseline, source))
        reason = report['reason']
    except Exception:
        reason = 'CONTRACT_TOOL_ERROR'
    print(json.dumps({'reason': reason}))


if __name__ == '__main__':
    main()
