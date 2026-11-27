"""Validate the captured real create-tenant response against its OpenAPI schema.

Run the TenantProvisioningTests valid-provisioner test with detailed console output,
then pass the test log path to this script using the locked contract Python environment.
"""
import json
from pathlib import Path
import sys

import yaml
from jsonschema import Draft202012Validator, FormatChecker

root = Path(__file__).resolve().parents[3]
api = yaml.safe_load((root / "docs/contracts/crm-api.openapi.yaml").read_text(encoding="utf-8"))
response_spec = api["paths"]["/api/v1/tenants"]["post"]["responses"]["201"]
schema_ref = response_spec["content"]["application/json"]["schema"]["$ref"]
assert schema_ref == "#/components/schemas/Tenant"
schema = api["components"]["schemas"]["Tenant"]
marker = "TENANT_RESPONSE_JSON:"
lines = [line.split(marker, 1)[1].strip() for line in Path(sys.argv[1]).read_text(encoding="utf-8").splitlines() if marker in line]
if len(lines) != 1:
    raise SystemExit(f"Expected exactly one captured real response, found {len(lines)}")
value = json.loads(lines[0])
Draft202012Validator(schema, format_checker=FormatChecker()).validate(value)
print("Real POST /api/v1/tenants response satisfies OpenAPI 201 Tenant schema")
