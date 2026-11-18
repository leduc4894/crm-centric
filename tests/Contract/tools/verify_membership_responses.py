"""Validate captured real MembershipEndpointsTests responses against OpenAPI."""
import json
from pathlib import Path
import re
import sys
import yaml
from jsonschema import Draft202012Validator, FormatChecker

root = Path(__file__).resolve().parents[3]
api = yaml.safe_load((root / "docs/contracts/crm-api.openapi.yaml").read_text(encoding="utf-8"))
marker = "MEMBERSHIP_CONTRACT:"
rows = [json.loads(line.split(marker, 1)[1].strip()) for line in Path(sys.argv[1]).read_text(encoding="utf-8").splitlines() if marker in line]
assert sorted(row["status"] for row in rows) == [200, 200, 200, 400, 403, 404, 409, 412, 428]
refs = {"membership": "Membership", "page": "MembershipPage", "problem": "Problem"}
for row in rows:
    schema = {"$ref": "#/components/schemas/" + refs[row["kind"]], "components": api["components"]}
    Draft202012Validator(schema, format_checker=FormatChecker()).validate(row["body"])
    if row["kind"] == "membership":
        assert re.fullmatch(r'"v[1-9][0-9]*"', row["etag"])
print(f"{len(rows)} real membership and Problem responses satisfy OpenAPI schemas and ETag format")
