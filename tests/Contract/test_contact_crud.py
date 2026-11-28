"""Contact HTTP and durable event intent contracts; not runtime evidence."""
import copy
import json
from pathlib import Path
import unittest

import yaml
from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[2]
CONTRACTS = ROOT / "docs/contracts"


class ContactCrudContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.api = yaml.safe_load((CONTRACTS / "crm-api.openapi.yaml").read_text())
        if "date-time" not in FormatChecker.checkers:
            raise RuntimeError("date-time format checker unavailable; use the locked contract environment")

    def assert_valid(self, schema, value):
        errors = list(Draft202012Validator(schema, format_checker=FormatChecker()).iter_errors(value))
        self.assertFalse(errors, "; ".join(error.message for error in errors))

    def assert_invalid(self, schema, value):
        self.assertTrue(list(Draft202012Validator(schema, format_checker=FormatChecker()).iter_errors(value)))

    def test_four_operations_and_preconditions(self):
        paths = self.api["paths"]
        create = paths["/api/v1/contacts"]["post"]
        item = paths["/api/v1/contacts/{id}"]
        for operation, name, status in [(create, "createContact", "201"),
                                        (item["get"], "getContact", "200"),
                                        (item["patch"], "updateContact", "200"),
                                        (item["delete"], "softDeleteContact", "204")]:
            self.assertEqual(operation["operationId"], name)
            self.assertIn(status, operation["responses"])
            self.assertIn("400", operation["responses"])
        for operation in [item["patch"], item["delete"]]:
            self.assertIn({"$ref": "#/components/parameters/IfMatch"}, operation["parameters"])
            self.assertIn("412", operation["responses"])
            self.assertIn("428", operation["responses"])
        pattern = self.api["components"]["parameters"]["IfMatch"]["schema"]["pattern"]
        for value in ['"v1"', '"v123"']:
            self.assertRegex(value, pattern)
        for value in ['W/"v1"', '*', '"v0"', '"v1", "v2"']:
            self.assertNotRegex(value, pattern)
        for operation, status in [(create, "201"), (item["get"], "200"), (item["patch"], "200")]:
            self.assertEqual(operation["responses"][status]["headers"]["ETag"]["schema"]["pattern"], pattern)
            self.assertEqual(operation["responses"][status]["content"]["application/json"]["schema"]["$ref"], "#/components/schemas/Contact")

    def test_contact_request_and_response_shapes(self):
        schemas = self.api["components"]["schemas"]
        for name in ["Contact", "CreateContact", "UpdateContact"]:
            schema = schemas[name]
            self.assertFalse(schema["additionalProperties"])
            self.assertEqual(schema["properties"]["displayName"]["minLength"], 1)
            self.assertEqual(schema["properties"]["displayName"]["maxLength"], 200)
            self.assertEqual(schema["properties"]["email"]["maxLength"], 320)
            self.assertEqual(schema["properties"]["phone"]["maxLength"], 40)
        create, update = schemas["CreateContact"], schemas["UpdateContact"]
        self.assert_valid(create, {"displayName": "A", "email": None, "phone": None})
        self.assert_valid(create, {"displayName": "x" * 200, "phone": "1" * 40})
        for value in [{}, {"displayName": ""}, {"displayName": "x" * 201},
                      {"displayName": "A", "phone": "1" * 41},
                      {"displayName": "A", "email": "x" * 310 + "@example.com"},
                      {"displayName": "A", "tenantId": "other"}]:
            self.assert_invalid(create, value)
        self.assert_valid(update, {"email": None})
        self.assert_valid(update, {"phone": None})
        for value in [{}, {"displayName": None}, {"unknown": 1}]:
            self.assert_invalid(update, value)
        contact = {"id": "957b9174-a24a-4f37-bc19-43c360ac18dc", "displayName": "A", "version": 1,
                   "createdAt": "2026-01-01T00:00:00Z", "updatedAt": "2026-01-01T00:00:00Z"}
        self.assert_valid(schemas["Contact"], contact)
        self.assert_invalid(schemas["Contact"], {**contact, "tenantId": "other"})

    def test_versioned_intent_fixtures_reject_invalid_identity_type_time_and_payload(self):
        schema_path = CONTRACTS / "contact-event-intent.schema.json"
        self.assertTrue(schema_path.is_file(), "versioned contact intent schema is missing")
        schema = json.loads(schema_path.read_text())
        Draft202012Validator.check_schema(schema)
        for name in ["created", "updated", "deleted"]:
            with self.subTest(event=name):
                path = CONTRACTS / "fixtures" / f"contact-{name}.v1.json"
                self.assertTrue(path.is_file(), f"fixture missing: {name}")
                event = json.loads(path.read_text())
                self.assertEqual(event["eventType"], f"crm.contact.{name}.v1")
                self.assert_valid(schema, event)
                for key in ["eventId", "tenantId", "contactId", "eventType", "occurredAt", "payload"]:
                    bad = copy.deepcopy(event)
                    del bad[key]
                    self.assert_invalid(schema, bad)
                for key in ["eventId", "tenantId", "contactId"]:
                    self.assert_invalid(schema, {**event, key: "not-a-uuid"})
                self.assert_invalid(schema, {**event, "eventType": "crm.contact.created.v2"})
                self.assert_invalid(schema, {**event, "occurredAt": "yesterday"})
                self.assert_invalid(schema, {**event, "payload": {}})
                self.assert_invalid(schema, {**event, "payload": {"version": 0}})
                self.assert_invalid(schema, {**event, "payload": {"version": 1, "email": "a@example.com"}})
                self.assert_invalid(schema, {**event, "displayName": "Private"})
