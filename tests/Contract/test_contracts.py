"""Executable contracts, not proof of runtime authentication or durability."""
import copy
import hashlib
import json
from pathlib import Path
import unittest

import yaml
from jsonschema import Draft202012Validator, FormatChecker
from openapi_spec_validator import validate
from referencing import Registry, Resource
from referencing.jsonschema import DRAFT202012

ROOT = Path(__file__).resolve().parents[2]
CONTRACTS = ROOT / "docs/contracts"


def load(name):
    return json.loads((CONTRACTS / name).read_text(encoding="utf-8"))


class ContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = load("demo-module.manifest.json")
        cls.rule = {
            "ruleId": "engaged", "version": 1,
            "triggerEventType": "demo.activity.created.v1",
            "triggerInstallationId": "75629fa8-bcb4-47d7-938f-393236a7cc01",
            "condition": {"field": "/data/score", "op": "gt", "value": 100},
            "actions": [{"type": "crm.addTag.v1", "input": {"tag": "engaged"}}],
        }
        cls.api = yaml.safe_load((CONTRACTS / "crm-api.openapi.yaml").read_text(encoding="utf-8"))

    def valid(self, schema, value):
        errors = list(Draft202012Validator(load(schema), format_checker=FormatChecker()).iter_errors(value))
        self.assertFalse(errors, "; ".join(e.message for e in errors))

    def invalid(self, schema, value):
        self.assertTrue(list(Draft202012Validator(load(schema), format_checker=FormatChecker()).iter_errors(value)))

    def test_all_schemas_are_valid(self):
        for path in CONTRACTS.glob("*.schema.json"):
            with self.subTest(path=path.name):
                Draft202012Validator.check_schema(load(path.name))

    def test_manifest_sample_and_event_sample(self):
        self.valid("module-manifest.schema.json", self.manifest)
        event = json.loads((ROOT / "docs/examples/02-SAMPLE-EVENT.json").read_text(encoding="utf-8"))
        self.valid("demo-event.schema.json", event)

    def test_manifest_can_describe_remote_panel_and_action(self):
        self.assertEqual(self.manifest["schemaVersion"], "2")
        self.assertIn("event.trigger", self.manifest["capabilities"])
        self.assertEqual(self.manifest["actions"][0]["id"], "demo.echo.v1")
        self.assertIn("inputSchema", self.manifest["actions"][0])
        self.assertIn("outputSchema", self.manifest["panels"][0])

    def test_manifest_requires_descriptors_for_advertised_capabilities(self):
        for capability, field in [("automation.action", "actions"), ("contact.panel", "panels"),
                                  ("event.trigger", "events"), ("navigation", "navigationUrl"),
                                  ("health", "healthUrl")]:
            for replacement in [None, []] if field in {"actions", "panels", "events"} else [None]:
                with self.subTest(capability=capability, replacement=replacement):
                    manifest = copy.deepcopy(self.manifest)
                    if replacement is None:
                        manifest.pop(field, None)
                    else:
                        manifest[field] = replacement
                    self.invalid("module-manifest.schema.json", manifest)

    def test_manifest_pinned_demo_schemas_match_local_bytes(self):
        refs = [event["schema"] for event in self.manifest.get("events", []) if isinstance(event, dict)]
        refs += [a[key] for a in self.manifest.get("actions", []) for key in ["inputSchema", "outputSchema"]]
        refs += [panel["outputSchema"] for panel in self.manifest.get("panels", [])]
        self.assertEqual(len(refs), 4)
        for schema_ref in refs:
            path = CONTRACTS / schema_ref["uri"].rsplit("/", 1)[-1]
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), schema_ref["sha256"])

    def test_legacy_manifest_consumer_is_deliberately_rejected(self):
        path = ROOT / "tests/Contract/fixtures/legacy-module-v1.json"
        self.assertTrue(path.exists(), "Keep the actual old consumer for migration evidence")
        self.invalid("module-manifest.schema.json", json.loads(path.read_text(encoding="utf-8")))

    def test_manifest_rejects_legacy_unknown_fields_and_bad_descriptor(self):
        self.assertIn("actions", self.manifest, "Manifest sample needs action descriptors")
        for mutate in [lambda m: m.update(schemaVersion="1"), lambda m: m.update(script="run()"),
                       lambda m: m.update(platformApiVersion="^99.0"),
                       lambda m: m["actions"][0].update(endpoint="http://demo.example.net/action"),
                       lambda m: m["actions"][0].pop("requiredScopes")]:
            with self.subTest(mutation=mutate):
                value = copy.deepcopy(self.manifest)
                mutate(value)
                self.invalid("module-manifest.schema.json", value)

    def test_rule_exists_does_not_need_value(self):
        rule = copy.deepcopy(self.rule)
        rule["condition"] = {"field": "/data/score", "op": "exists"}
        self.valid("automation-rule.schema.json", rule)
        rule["condition"]["value"] = True
        self.invalid("automation-rule.schema.json", rule)

    def test_rule_supports_nested_all_any(self):
        rule = copy.deepcopy(self.rule)
        rule["condition"] = {"all": [rule["condition"], {"any": [
            {"field": "/data/activityKind", "op": "eq", "value": "milestone"},
            {"field": "/data/score", "op": "exists"}]}]}
        self.valid("automation-rule.schema.json", rule)

    def test_demo_rule_consumer_matches_manifest_capabilities(self):
        path = ROOT / "docs/examples/03-SAMPLE-RULE.json"
        self.assertTrue(path.exists(), "Demo E2E needs a complete rule consumer fixture")
        rule = json.loads(path.read_text(encoding="utf-8"))
        self.valid("automation-rule.schema.json", rule)
        self.assertEqual(rule["triggerEventType"], self.manifest["events"][0]["type"])
        remote = rule["actions"][1]
        self.assertEqual(remote["type"], self.manifest["actions"][0]["id"])
        self.assertEqual(remote["installationId"], rule["triggerInstallationId"])
        self.valid("demo-echo-input.schema.json", remote["input"])

    def test_rule_rejects_wrong_types_unknown_operators_and_empty_groups(self):
        for condition in [{"field": "/data/score", "op": "gt", "value": "100"},
                          {"field": "/data/score", "op": "eval", "value": "1+1"},
                          {"all": []}, {"any": []},
                          {"field": "/data/score", "op": "eq", "value": {}},
                          {"field": "/data/score", "op": "exists", "script": "x"}]:
            with self.subTest(condition=condition):
                self.invalid("automation-rule.schema.json", dict(self.rule, condition=condition))

    def test_rule_depth_boundary(self):
        condition = self.rule["condition"]
        for _ in range(4):
            condition = {"all": [condition]}
        self.valid("automation-rule.schema.json", dict(self.rule, condition=condition))
        self.invalid("automation-rule.schema.json", dict(self.rule, condition={"all": [condition]}))

    def test_remote_rule_binds_trigger_and_action_installations(self):
        value = copy.deepcopy(self.rule)
        value["actions"] = [{"type": "demo.echo.v1", "input": {"message": "hello"}}]
        self.invalid("automation-rule.schema.json", value)
        value["actions"][0]["installationId"] = "5639df41-90d3-4987-98ae-aea7882bb982"
        self.valid("automation-rule.schema.json", value)
        value.pop("triggerInstallationId")
        self.invalid("automation-rule.schema.json", value)

    def test_crm_builtin_action_rejects_remote_destination_and_invalid_input(self):
        value = copy.deepcopy(self.rule)
        value["actions"][0]["installationId"] = "5639df41-90d3-4987-98ae-aea7882bb982"
        self.invalid("automation-rule.schema.json", value)
        value["actions"][0].pop("installationId")
        value["actions"][0]["input"] = {"script": "tag()"}
        self.invalid("automation-rule.schema.json", value)

    def test_descriptors_require_external_entity_type(self):
        for field in ["events", "actions", "panels"]:
            with self.subTest(field=field):
                value = copy.deepcopy(self.manifest)
                self.assertEqual(value[field][0].get("entityType"), "demo.member")
                value[field][0].pop("entityType")
                self.invalid("module-manifest.schema.json", value)

    def test_remote_contact_reference_preserves_entity_type(self):
        path = CONTRACTS / "remote-panel-request.schema.json"
        self.assertTrue(path.exists(), "Remote contact reference needs a typed consumer contract")
        for entity_type in ["demo.member", "demo.organization"]:
            self.valid(path.name, {"contactRef": {"entityType": entity_type, "externalId": "same-id"}})
        for contact_ref in ["same-id", {"externalId": "same-id"},
                            {"entityType": "demo.member", "externalId": "same-id", "tenant_id": "forged"}]:
            self.invalid(path.name, {"contactRef": contact_ref})

    def test_demo_event_rejects_missing_identity_and_unknown_data(self):
        event = json.loads((ROOT / "docs/examples/02-SAMPLE-EVENT.json").read_text(encoding="utf-8"))
        for field in ["source", "id"]:
            value = copy.deepcopy(event)
            value[field] = ""
            self.invalid("demo-event.schema.json", value)
        event["data"]["tenant_id"] = "forged"
        self.invalid("demo-event.schema.json", event)

    def test_openapi_is_valid_and_operation_ids_unique(self):
        validate(self.api)
        ids = [op["operationId"] for path in self.api["paths"].values()
               for method, op in path.items() if method in {"get", "post", "put", "patch", "delete"}]
        self.assertEqual(len(ids), len(set(ids)))

    def test_p0_consumers_have_required_operations(self):
        required = {
            ("/api/v1/tenants", "post"), ("/api/v1/tenants", "get"),
            ("/api/v1/invites", "post"), ("/api/v1/invites/{id}", "delete"),
            ("/api/v1/invites/accept", "post"), ("/api/v1/memberships", "get"),
            ("/api/v1/memberships/{id}", "patch"),
            ("/api/v1/contacts/{id}", "patch"), ("/api/v1/contacts/{id}", "delete"),
            ("/api/v1/tags", "post"), ("/api/v1/contacts/{id}/tags/{tagId}", "put"),
            ("/api/v1/custom-fields", "post"), ("/api/v1/contacts/{id}/fields/{fieldId}", "put"),
            ("/api/v1/contacts/{id}/timeline", "get"),
            ("/api/v1/contacts/{id}/identity-links", "post"),
            ("/api/v1/contacts/{id}/identity-links/{linkId}", "delete"),
            ("/api/v1/integrations/installations/{id}", "patch"),
            ("/api/v1/integrations/installations/{id}", "delete"),
            ("/api/v1/integrations/installations/{id}/approvals", "post"),
            ("/api/v1/integrations/external-links", "post"),
            ("/api/v1/integrations/unmatched-events/{id}/resolve", "post"),
            ("/api/v1/integrations/dead-letters/{id}/replay", "post"),
            ("/api/v1/automations/rules", "post"),
            ("/api/v1/automations/rules/{id}", "put"),
            ("/api/v1/automations/rules/{id}/status", "patch"),
            ("/api/v1/automations/executions", "get"), ("/api/v1/audit", "get"),
        }
        present = {(p, m) for p, path in self.api["paths"].items() for m in path}
        self.assertFalse(required - present, str(sorted(required - present)))

    def test_all_api_errors_use_problem_details(self):
        self.assertIn("Problem", self.api["components"]["schemas"])
        for path in self.api["paths"].values():
            for method, op in path.items():
                if method not in {"get", "post", "put", "patch", "delete"}:
                    continue
                with self.subTest(operation=op["operationId"]):
                    for status, response in op["responses"].items():
                        if status == "default" or status.startswith(("4", "5")):
                            if "$ref" in response:
                                response = self.api["components"]["responses"][response["$ref"].split("/")[-1]]
                            self.assertIn("application/problem+json", response.get("content", {}))
        schema = self.api["components"]["schemas"]["Problem"]
        for field in ["status", "reasonToken", "correlationId"]:
            self.assertIn(field, schema["required"])

    def test_mutation_bodies_reject_forged_tenant(self):
        resource = Resource.from_contents(self.api, default_specification=DRAFT202012)
        registry = Registry().with_resource("urn:crm:api", resource)
        examples = [
            ("/api/v1/contacts", "post", {"displayName": "Test"}),
            ("/api/v1/invites", "post", {"email": "member@example.net", "role": "agent"}),
            ("/api/v1/integrations/installations", "post", {"manifestUrl": "https://demo.example.net/manifest.json"}),
        ]
        for path, method, example in examples:
            self.assertIn(path, self.api["paths"])
            schema = self.api["paths"][path][method]["requestBody"]["content"]["application/json"]["schema"]
            consumer_schema = {"$ref": "urn:crm:api" + schema["$ref"]}
            validator = Draft202012Validator(consumer_schema, registry=registry, format_checker=FormatChecker())
            self.assertFalse(list(validator.iter_errors(example)))
            self.assertTrue(list(validator.iter_errors(dict(example, tenant_id="forged"))))

    def test_contact_mutations_require_if_match(self):
        for method in ["patch", "delete"]:
            self.assertIn(method, self.api["paths"]["/api/v1/contacts/{id}"])
            op = self.api["paths"]["/api/v1/contacts/{id}"][method]
            params = [self.api["components"]["parameters"][p["$ref"].split("/")[-1]] if "$ref" in p else p
                      for p in op["parameters"]]
            self.assertTrue(any(p["name"] == "If-Match" and p["required"] for p in params))
            self.assertIn("412", op["responses"])
            self.assertIn("428", op["responses"])

    def test_webhook_requires_installation_bound_key_and_signed_metadata(self):
        op = self.api["paths"]["/api/v1/integrations/events"]["post"]
        params = [self.api["components"]["parameters"][p["$ref"].split("/")[-1]] if "$ref" in p else p
                  for p in op["parameters"]]
        names = {p["name"] for p in params if p["required"]}
        self.assertTrue({"X-Installation-Id", "X-Webhook-Key-Id", "X-Event-Id", "X-Event-Timestamp"} <= names)

    def test_rule_list_exposes_internal_resource_id(self):
        self.assertIn("id", self.api["components"]["schemas"]["RuleSummary"]["required"])

    def test_panel_request_requires_destination_installation(self):
        operation = self.api["paths"]["/api/v1/contacts/{id}/panels/{panelId}"]["get"]
        self.assertTrue(any(p.get("name") == "installationId" and p.get("required")
                            for p in operation["parameters"]))

    def test_invite_token_is_returned_only_at_creation(self):
        content = self.api["paths"]["/api/v1/invites"]["post"]["responses"]["201"]["content"]
        self.assertEqual(content["application/json"]["schema"]["$ref"], "#/components/schemas/InviteCreated")
        self.assertIn("token", self.api["components"]["schemas"]["InviteCreated"]["required"])
        self.assertNotIn("token", self.api["components"]["schemas"]["Invite"]["properties"])

    def test_transport_event_id_rejects_non_ascii_and_control_characters(self):
        header = self.api["components"]["parameters"]["EventId"]["schema"]
        event = json.loads((ROOT / "docs/examples/02-SAMPLE-EVENT.json").read_text(encoding="utf-8"))
        for event_id in ["evt\nforged", "evt\rforged", "evt ü", "evt id"]:
            with self.subTest(event_id=event_id):
                self.assertTrue(list(Draft202012Validator(header).iter_errors(event_id)))
                self.invalid("demo-event.schema.json", dict(event, id=event_id))


if __name__ == "__main__":
    unittest.main()
