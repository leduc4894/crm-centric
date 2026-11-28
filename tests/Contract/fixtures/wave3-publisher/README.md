# Publisher signature test vector

The manifest is the exact UTF-8 demo manifest at the time of signing, including its final LF. SHA-256 is `82478c0cc6aa2e468f3fe87f73a13f4b2e482bdeb764e5947bf8b9e5083d1c52`. The deterministic seed below is publicly derivable and reserved for this fixture. Never provision this key. To reproduce the sidecar and public key, run in this directory with Python and cryptography installed.

```python
from base64 import urlsafe_b64encode
from hashlib import sha256
import json
from pathlib import Path
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat

seed = sha256(b"CRM wave 3 public test fixture only").digest()
key = Ed25519PrivateKey.from_private_bytes(seed)
signature = key.sign(Path("manifest.json").read_bytes())
sidecar = {"algorithm": "Ed25519", "keyId": "fixture-publisher-1",
           "signature": urlsafe_b64encode(signature).decode("ascii").rstrip("=")}
Path("manifest.json.sig.json").write_bytes((json.dumps(sidecar, indent=2) + "\n").encode())
Path("public.pem").write_bytes(key.public_key().public_bytes(Encoding.PEM, PublicFormat.SubjectPublicKeyInfo))
```
