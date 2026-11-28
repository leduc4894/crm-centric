"""Manifest trust contract and detached Ed25519 known-answer vector."""
import base64
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

from jsonschema import Draft202012Validator

HERE = Path(__file__).resolve().parent
CONTRACTS = HERE.parents[1] / "docs/contracts"
VECTOR = HERE / "fixtures/wave3-publisher"


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


class PublisherContractTests(unittest.TestCase):
    def test_manifest_schema(self):
        validator = Draft202012Validator(read_json(CONTRACTS / "module-manifest.schema.json"))
        demo = read_json(CONTRACTS / "demo-module.manifest.json")
        validator.validate(demo)
        for change in ({"schemaVersion": "1"}, {"extra": True}, {"version": "1.0.0-beta.1"},
                       {"platformApiVersion": "^2.0"}):
            with self.subTest(change=change):
                self.assertFalse(validator.is_valid(demo | change))

    def test_sidecar_schema(self):
        validator = Draft202012Validator(read_json(CONTRACTS / "publisher-signature.schema.json"))
        sidecar = read_json(VECTOR / "manifest.json.sig.json")
        validator.validate(sidecar)
        for change in ({"algorithm": "HS256"}, {"keyId": "key/1"}, {"signature": "x" * 85},
                       {"signature": "x" * 87}):
            with self.subTest(change=change):
                self.assertFalse(validator.is_valid(sidecar | change))

    def test_exact_bytes_and_independent_verification(self):
        manifest = (VECTOR / "manifest.json").read_bytes()
        self.assertEqual(read_json(CONTRACTS / "demo-module.manifest.json"), json.loads(manifest.decode("utf-8")))
        self.assertEqual("82478c0cc6aa2e468f3fe87f73a13f4b2e482bdeb764e5947bf8b9e5083d1c52",
                         hashlib.sha256(manifest).hexdigest())
        encoded = read_json(VECTOR / "manifest.json.sig.json")["signature"]
        signature = base64.urlsafe_b64decode(encoded + "==")
        self.assertEqual(64, len(signature))
        self.assertEqual(encoded, base64.urlsafe_b64encode(signature).decode().rstrip("="))
        with tempfile.TemporaryDirectory() as directory:
            sig = Path(directory) / "sig"
            body = Path(directory) / "body"
            sig.write_bytes(signature)
            command = ["openssl", "pkeyutl", "-verify", "-pubin", "-inkey", str(VECTOR / "public.pem"),
                       "-rawin", "-in", str(body), "-sigfile", str(sig)]
            for data, expected in ((manifest, 0), (manifest + b" ", 1)):
                body.write_bytes(data)
                result = subprocess.run(command, capture_output=True)
                self.assertEqual(expected, result.returncode, result.stderr)
