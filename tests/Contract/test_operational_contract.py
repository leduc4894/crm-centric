"""Operational contract consumer tests; runtime readiness belongs to later tasks."""
import copy
from pathlib import Path
import unittest

import yaml
from jsonschema import Draft202012Validator, FormatChecker
from openapi_spec_validator import validate

CONTRACTS = Path(__file__).resolve().parents[2] / "docs/contracts"


class OperationalContractTests(unittest.TestCase):
    def contract(self):
        path = CONTRACTS / "platform-operations.openapi.yaml"
        self.assertTrue(path.exists(), "Operational contract has not been implemented")
        return yaml.safe_load(path.read_text(encoding="utf-8"))

    def test_operational_contract_is_valid_and_anonymous(self):
        api = self.contract()
        validate(api)
        self.assertTrue(api["openapi"].startswith("3.1."))
        self.assertEqual(api["security"], [])
        self.assertEqual(set(api["paths"]), {"/health/live", "/health/ready"})
        for route, state in [("/health/live", "live"), ("/health/ready", "ready")]:
            path = api["paths"][route]
            self.assertEqual(set(path), {"get"})
            op = path["get"]
            self.assertEqual(op.get("security", api["security"]), [])
            self.assertFalse(op.get("parameters"))
            self.assertEqual(set(op["responses"]), {"200", "500"} | ({"503"} if state == "ready" else set()))
            for response in op["responses"].values():
                self.assertEqual(response["headers"]["Cache-Control"]["schema"],
                                 {"type": "string", "enum": ["no-store"]})
                correlation = response["headers"]["X-Correlation-Id"]
                self.assertIn("server", correlation["description"].lower())
                self.assertEqual(correlation["schema"]["type"], "string")
                self.assertGreaterEqual(correlation["schema"]["minLength"], 1)
            schema = op["responses"]["200"]["content"]["application/json"]["schema"]
            self.assertEqual(schema, {"type": "object", "additionalProperties": False,
                                     "required": ["status"], "properties": {"status": {"type": "string", "enum": [state]}}})
        business = yaml.safe_load((CONTRACTS / "crm-api.openapi.yaml").read_text())
        self.assertTrue(business["security"], "Business API must retain authentication")

    def test_operational_problem_matches_baseline(self):
        api = self.contract()
        baseline = yaml.safe_load((CONTRACTS / "crm-api.openapi.yaml").read_text())
        problem = api["components"]["schemas"]["Problem"]
        self.assertEqual(problem, baseline["components"]["schemas"]["Problem"])
        validator = Draft202012Validator(problem, format_checker=FormatChecker())
        example = {"type": "about:blank", "title": "Unavailable", "status": 503,
                   "reasonToken": "DATABASE_UNAVAILABLE", "correlationId": "server-generated"}
        validator.validate(example)
        for field in ["reasonToken", "correlationId"]:
            invalid = copy.deepcopy(example)
            invalid.pop(field)
            self.assertFalse(validator.is_valid(invalid))
        self.assertFalse(validator.is_valid(dict(example, unexpected="rejected")))
        reasons = set()
        for path in api["paths"].values():
            for status, response in path["get"]["responses"].items():
                if status == "200":
                    continue
                media = response["content"]["application/problem+json"]
                self.assertEqual(media["schema"], {"$ref": "#/components/schemas/Problem"})
                for example in media["examples"].values():
                    value = example["value"]
                    validator.validate(value)
                    self.assertEqual(value["type"], "urn:crm:problem:" + value["reasonToken"].lower().replace("_", "-"))
                    reasons.add(value["reasonToken"])
                    self.assertEqual(example["value"]["status"], int(status))
        self.assertEqual(reasons, {"DATABASE_UNAVAILABLE", "DATABASE_SCHEMA_NOT_READY", "INTERNAL_ERROR"})

    def test_operational_examples_validate(self):
        api = self.contract()
        for path in api["paths"].values():
            for response in path["get"]["responses"].values():
                for media in response["content"].values():
                    schema = media["schema"]
                    if "$ref" in schema:
                        schema = api["components"]["schemas"][schema["$ref"].split("/")[-1]]
                    validator = Draft202012Validator(schema, format_checker=FormatChecker())
                    self.assertTrue(media["examples"])
                    for example in media["examples"].values():
                        validator.validate(example["value"])
