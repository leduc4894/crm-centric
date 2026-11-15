"""Download contract tools and verify the existing reviewed SHA-256 pins."""
import hashlib
import io
import json
from pathlib import Path
import tarfile
from urllib.request import urlopen


def download(url, digest):
    with urlopen(url, timeout=120) as response:
        data = response.read(100 * 1024 * 1024)
    if hashlib.sha256(data).hexdigest() != digest:
        raise ValueError('CI_DOWNLOAD_CHECKSUM_MISMATCH')
    return data


def main():
    tools = Path('.local/plan3-tools'); tools.mkdir(parents=True,exist_ok=True)
    toolchain = json.loads(Path('tools/contracts/toolchain.json').read_text())
    version = toolchain['corvusCli']
    (tools/f'corvus.json.cli.{version}.nupkg').write_bytes(download(
        f'https://api.nuget.org/v3-flatcontainer/corvus.json.cli/{version}/corvus.json.cli.{version}.nupkg',
        toolchain['corvusPackageSha256']))
    policy = json.loads(Path('tools/contracts/oasdiff.json').read_text())
    archive = download(f'https://github.com/oasdiff/oasdiff/releases/download/v{policy["version"]}/{policy["archive"]}',policy['archiveSha256'])
    with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
        data = tar.extractfile('oasdiff').read()
    if hashlib.sha256(data).hexdigest() != policy['binarySha256']:
        raise ValueError('CI_DOWNLOAD_CHECKSUM_MISMATCH')
    binary = Path(policy['binaryPath']); binary.parent.mkdir(parents=True,exist_ok=True)
    binary.write_bytes(data); binary.chmod(0o755)

if __name__ == '__main__':
    try:
        main()
    except Exception:
        import uuid
        print(json.dumps({'reasonToken':'CI_CONTRACT_PROVISION_FAILED','correlationId':str(uuid.uuid4())}))
        raise SystemExit(1)
