"""Known-answer consumer fixtures; no server signature verifier is implemented."""
import hashlib
import hmac
import json
from pathlib import Path
import unittest

FIXTURE = Path(__file__).resolve().parents[2] / "docs/contracts/webhook-signature.fixture.json"


class WebhookFixtureTests(unittest.TestCase):
    def test_known_answer_uses_exact_raw_bytes(self):
        self.assertTrue(FIXTURE.exists(), "Missing portable signature fixture")
        f = json.loads(FIXTURE.read_text(encoding="utf-8"))
        key = bytes.fromhex(f["testOnlyKeyHex"])
        body = f["bodyUtf8"].encode("utf-8")
        prefix = "\n".join(["crm-webhook-v1", "POST", "/api/v1/integrations/events",
                            f["installationId"], f["keyId"], f["timestamp"], f["eventId"]]) + "\n"
        expected = hmac.new(key, prefix.encode("utf-8") + body, hashlib.sha256).hexdigest()
        self.assertEqual("v1=" + expected, f["signature"])
        self.assertEqual(hashlib.sha256(body).hexdigest(), f["bodySha256"])
        self.assertEqual(json.loads(body)["id"], f["eventId"])

    def test_metadata_tampering_and_json_reserialization_change_signature(self):
        self.assertTrue(FIXTURE.exists(), "Missing portable signature fixture")
        f = json.loads(FIXTURE.read_text(encoding="utf-8"))
        fields = ["crm-webhook-v1", "POST", "/api/v1/integrations/events",
                  f["installationId"], f["keyId"], f["timestamp"], f["eventId"]]
        key = bytes.fromhex(f["testOnlyKeyHex"])
        original = f["bodyUtf8"].encode("utf-8")
        for i in range(1, 7):
            changed = fields.copy()
            changed[i] += "x"
            actual = hmac.new(key, ("\n".join(changed) + "\n").encode() + original, hashlib.sha256).hexdigest()
            self.assertNotEqual("v1=" + actual, f["signature"])
        pretty = json.dumps(json.loads(original), indent=2).encode("utf-8")
        actual = hmac.new(key, ("\n".join(fields) + "\n").encode() + pretty, hashlib.sha256).hexdigest()
        self.assertNotEqual("v1=" + actual, f["signature"])


if __name__ == "__main__":
    unittest.main()
