"""RED proposal tests for ADR-021; no runtime or consumer rollout proof."""
from pathlib import Path
import re
import unittest

import yaml

ROOT = Path(__file__).resolve().parents[2]


class ContactPreconditionIdentityContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.api = yaml.safe_load((ROOT / "docs/contracts/crm-api.openapi.yaml").read_text())
        cls.operation = cls.api["paths"]["/api/v1/contacts"]["post"]

    def test_conditional_post_exposes_expected_contact_identity(self):
        headers = []
        for parameter in self.operation.get("parameters", []):
            ref = parameter.get("$ref")
            if ref and ref.startswith("#/components/parameters/"):
                parameter = self.api["components"]["parameters"][ref.rsplit("/", 1)[1]]
            if parameter.get("in") == "header" and parameter.get("name", "").lower() == "x-contact-id":
                headers.append(parameter)
        self.assertEqual(1, len(headers), "conditional POST has no expected contact identity header")
        self.assertFalse(headers[0].get("required", False), "ordinary create must omit the header")
        self.assertEqual("string", headers[0]["schema"]["type"])
        self.assertEqual("uuid", headers[0]["schema"]["format"])
        for status in ("400", "412", "428"):
            self.assertEqual("#/components/responses/Problem", self.operation["responses"][status]["$ref"])

    def test_generated_typescript_consumer_can_send_contact_identity(self):
        schema = (ROOT / "sdks/typescript/crm/schema.d.ts").read_text()
        start = schema.index("    createContact: {")
        header = schema[start:schema.index("        requestBody:", start)]
        self.assertRegex(header, r'"X-Contact-Id"\?:\s*string', "generated client cannot send expected contact ID")
        self.assertRegex(header, r'"If-Match"\?:\s*string')

    def test_contract_requires_both_contact_identity_and_version_on_update(self):
        description = self.operation.get("description", "").lower()
        self.assertIn("x-contact-id", description, "contract does not bind update to contact identity")
        self.assertIn("if-match", description)
        self.assertIn("412", description)
        self.assertIn("428", description)

    def test_old_consumer_version_alone_cannot_distinguish_replacement(self):
        # Two API response examples show the information collision, not a
        # running-server assertion. Exact behavior requires PostgreSQL tests.
        first = {"id": "00000000-0000-4000-8000-000000000001", "version": 1}
        replacement = {"id": "00000000-0000-4000-8000-000000000002", "version": 1}
        self.assertNotEqual(first["id"], replacement["id"])
        self.assertEqual(f'"v{first["version"]}"', f'"v{replacement["version"]}"')
        self.assertIn("x-contact-id", self.operation.get("description", "").lower(),
                      "version-only consumer cannot bind its update to the original resource")
