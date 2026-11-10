"""Read-only verification primitives for generated client trees."""
import sys
sys.dont_write_bytecode = True
from pathlib import Path, PurePosixPath
from urllib.parse import urlsplit


def check_generated_tree(committed, generated):
    """Return a safe reason if generated files differ, without changing either tree."""
    committed, generated = Path(committed), Path(generated)
    if not committed.is_dir() or not generated.is_dir():
        return 'SDK_GENERATED_MISMATCH'
    def files(root):
        result = {}
        for path in root.rglob('*'):
            if path.is_symlink():
                return None
            if path.is_file():
                result[path.relative_to(root)] = path
        return result
    left, right = files(committed), files(generated)
    if left is None or right is None or left.keys() != right.keys():
        return 'SDK_GENERATED_MISMATCH'
    if any(left[name].read_bytes() != right[name].read_bytes() for name in left):
        return 'SDK_GENERATED_MISMATCH'
    return None


def check_local_refs(document):
    """Reject URI references that could cause network or external file access."""
    if isinstance(document, dict):
        for name, value in document.items():
            if name in ('$ref', '$dynamicRef', '$recursiveRef'):
                if not isinstance(value, str):
                    return 'CONTRACT_NONLOCAL_REF'
                ref = urlsplit(value)
                if ref.scheme or ref.netloc or ref.query or '%' in ref.path or '\\' in ref.path or ref.path.startswith('/') or '..' in PurePosixPath(ref.path).parts:
                    return 'CONTRACT_NONLOCAL_REF'
            if check_local_refs(value):
                return 'CONTRACT_NONLOCAL_REF'
    elif isinstance(document, list):
        for value in document:
            if check_local_refs(value):
                return 'CONTRACT_NONLOCAL_REF'
    return None


def main():
    import argparse
    import json
    import tempfile
    import uuid
    import zipfile
    ROOT = Path(__file__).resolve().parents[2]

    parser = argparse.ArgumentParser()
    parser.add_argument('--output-root', type=Path, default=ROOT)
    args = parser.parse_args()
    reason = None
    try:
        from scripts.contracts.generate import generate
        with tempfile.TemporaryDirectory(prefix='crm-sdk-check-') as temporary:
            generate(Path(temporary))
            reason = check_generated_tree(args.output_root / 'sdks', Path(temporary) / 'sdks')
    except (ImportError, KeyError, TypeError):
        reason = 'SDK_TOOLCHAIN_UNAVAILABLE'
    except (OSError, ValueError, RuntimeError, RecursionError, zipfile.BadZipFile) as error:
        reason = str(error) if type(error) is RuntimeError else 'SDK_CHECK_ERROR'
    print(json.dumps({'reason': reason or 'SDK_GENERATED_CURRENT', 'correlationId': str(uuid.uuid4())}))
    return 1 if reason else 0




def ref_files(path, root):
    """Return the validated transitive local input closure for a document."""
    import yaml
    root = Path(root).resolve()
    pending, seen = [Path(path)], set()
    while pending:
        current = pending.pop().resolve()
        if not current.is_relative_to(root) or not current.is_file():
            raise RuntimeError('CONTRACT_NONLOCAL_REF')
        if current in seen:
            continue
        seen.add(current)
        try:
            document = yaml.safe_load(current.read_text())
        except (OSError, UnicodeError, yaml.YAMLError):
            raise RuntimeError('CONTRACT_NONLOCAL_REF') from None
        if check_local_refs(document):
            raise RuntimeError('CONTRACT_NONLOCAL_REF')
        def collect(value):
            if isinstance(value, dict):
                for key, item in value.items():
                    if key in ('$ref', '$dynamicRef', '$recursiveRef') and urlsplit(item).path:
                        pending.append(current.parent / urlsplit(item).path)
                    collect(item)
            elif isinstance(value, list):
                for item in value:
                    collect(item)
        collect(document)
    return sorted(seen)


def check_ref_files(path, root):
    try:
        ref_files(path, root)
    except RuntimeError as error:
        return str(error)
    return None


if __name__ == '__main__':
    import sys
    sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
    sys.exit(main())
