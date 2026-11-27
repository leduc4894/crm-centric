"""ADR-020 contract proposal: representative consumers, not a running API test.

The tests intentionally fail against the old POST-only-201 OpenAPI contract. The
objects below represent two responses for the same active tenant/contact; they
are consumer examples, not proof that a server performs the mutation.
"""
import json
import re
import xml.etree.ElementTree as ET
import unittest
from pathlib import Path

import yaml
from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[2]
API_PATH = ROOT / "docs/contracts/crm-api.openapi.yaml"


def legacy_consumer(status):
    """Illustrate a deployed client which treats any response other than 201 as failure."""
    return status == 201


def proposed_consumer(status, etag, body):
    """Classify outcomes while checking version/ETag agreement before using a contact."""
    if status not in (200, 201):
        return "error"
    if not isinstance(body, dict) or not isinstance(body.get("version"), int) or isinstance(body.get("version"), bool):
        raise ValueError("invalid contact")
    if not re.fullmatch(r'"v[1-9][0-9]*"', etag or "") or etag != f'"v{body["version"]}"':
        raise ValueError("invalid ETag")
    return "created" if status == 201 else "updated"


class ContactUpsertConsumerContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.api = yaml.safe_load(API_PATH.read_text())
        cls.operation = cls.api["paths"]["/api/v1/contacts"]["post"]
        cls.contact = cls.api["components"]["schemas"]["Contact"]
        cls.created = {"id": "d28fd7fd-cbd4-4827-b4c8-e574fe26c771", "displayName": "First",
                       "email": "person@example.test", "phone": "123", "version": 1,
                       "createdAt": "2026-11-27T00:00:00Z", "updatedAt": "2026-11-27T00:00:00Z"}
        cls.upserted = {**cls.created, "displayName": "Second", "version": 2,
                        "updatedAt": "2026-11-27T00:01:00Z"}

    def test_old_201_only_consumer_rejects_new_200_and_requires_migration(self):
        self.assertTrue(legacy_consumer(201))
        self.assertFalse(legacy_consumer(200))
        self.assertIn("201", self.operation["responses"])
        self.assertIn("200", self.operation["responses"], "proposed upsert outcome is absent from the public contract")

    def test_new_consumer_handles_created_and_updated_with_strong_etag_and_problems(self):
        responses = self.operation["responses"]
        self.assertEqual("createContact", self.operation["operationId"])
        for status, body, etag, expected in [("201", self.created, '"v1"', "created"),
                                              ("200", self.upserted, '"v2"', "updated")]:
            with self.subTest(status=status):
                self.assertIn(status, responses)
                response = responses[status]
                self.assertEqual("#/components/schemas/Contact", response["content"]["application/json"]["schema"]["$ref"])
                self.assertTrue(response["headers"]["ETag"].get("required", False), "success ETag is required")
                pattern = response["headers"]["ETag"]["schema"]["pattern"]
                self.assertRegex(etag, pattern)
                self.assertFalse(list(Draft202012Validator(self.contact, format_checker=FormatChecker()).iter_errors(body)))
                self.assertEqual(expected, proposed_consumer(int(status), etag, body))
        for status in (400, 401, 403, 409, 503):
            self.assertIn(str(status), responses)
            self.assertEqual("error", proposed_consumer(status, None, None))
        for etag in (None, 'W/"v2"', '"v1"', '*'):
            with self.subTest(etag=etag), self.assertRaises(ValueError):
                proposed_consumer(200, etag, self.upserted)

    def test_breaking_crm_sdks_use_new_package_versions(self):
        csproj = ET.parse(ROOT / "sdks/dotnet/Crm.Client/Crm.Client.csproj")
        self.assertEqual("2.0.0", csproj.findtext("./PropertyGroup/Version"))
        package = json.loads((ROOT / "sdks/typescript/crm/package.json").read_text())
        self.assertEqual("0.2.0", package["version"])

    def test_contract_documents_if_match_when_no_active_email_matches(self):
        description = self.operation["description"]
        self.assertIn("If-Match is supplied but no active contact matches", description)
        self.assertIn("412", description)

    def test_upsert_precondition_contract_keeps_create_possible_and_rejects_stale_versions(self):
        # POST creates without If-Match; on an existing email the server must
        # require a strong matching token and must not mutate on a failure.
        parameters = self.operation.get("parameters", [])
        resolved = []
        for item in parameters:
            reference = item.get("$ref")
            if reference and reference.startswith("#/components/parameters/"):
                item = self.api["components"]["parameters"][reference.rsplit("/", 1)[1]]
            if item.get("in") == "header" and item.get("name", "").lower() == "if-match":
                resolved.append(item)
        self.assertEqual(1, len(resolved), "POST must expose the strong If-Match header")
        parameter = resolved[0]
        self.assertFalse(parameter.get("required", False), "new contacts do not require If-Match")
        pattern = parameter["schema"]["pattern"]
        for value in ('"v1"', '"v2"'):
            self.assertRegex(value, pattern)
        for value in ('W/"v1"', '*', '"v1", "v2"', '"v0"'):
            self.assertNotRegex(value, pattern)
        for status in ("400", "412", "428"):
            self.assertIn(status, self.operation["responses"])
            self.assertEqual("#/components/responses/Problem", self.operation["responses"][status]["$ref"])
            self.assertEqual("error", proposed_consumer(int(status), None, None))
        self.assertIn("200", self.operation["responses"])

    def test_proposed_examples_show_omitted_phone_preserved_and_explicit_null_cleared(self):
        # These are consumer expectations only. Integration tests must send the
        # requests and verify database state and audit/outbox atomicity.
        omitted_phone_request = {"displayName": "Second", "email": "person@example.test"}
        explicit_null_request = {"displayName": "Second", "email": "person@example.test", "phone": None}
        create_schema = self.api["components"]["schemas"]["CreateContact"]
        validator = Draft202012Validator(create_schema, format_checker=FormatChecker())
        self.assertFalse(list(validator.iter_errors(omitted_phone_request)))
        self.assertFalse(list(validator.iter_errors(explicit_null_request)))
        self.assertEqual(self.created["id"], self.upserted["id"])
        self.assertEqual(self.created["phone"], self.upserted["phone"])
        cleared = {**self.upserted, "phone": None, "version": 3}
        self.assertFalse(list(Draft202012Validator(self.contact, format_checker=FormatChecker()).iter_errors(cleared)))
        self.assertEqual("updated", proposed_consumer(200, '"v3"', cleared))
        self.assertIn("200", self.operation["responses"], "upsert response is not yet published")
