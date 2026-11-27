import unittest
from pathlib import Path

import yaml


class TimelineContract(unittest.TestCase):
    def test_existing_operations_and_updated_contact_etag(self):
        spec = yaml.safe_load(Path("docs/contracts/crm-api.openapi.yaml").read_text())
        paths = spec["paths"]
        schemas = spec["components"]["schemas"]
        base = "/api/v1/contacts/{id}"
        listing = paths[base + "/timeline"]["get"]
        create = paths[base + "/notes"]["post"]
        self.assertEqual(listing["operationId"], "listTimeline")
        self.assertEqual(create["operationId"], "createContactNote")
        self.assertIn({"$ref": "#/components/parameters/IfMatch"}, create["parameters"])
        for op, status, body in [(listing, "200", "TimelineEntryPage"), (create, "201", "TimelineEntry")]:
            self.assertEqual(op["responses"][status]["content"]["application/json"]["schema"],
                             {"$ref": "#/components/schemas/" + body})
        for name in ["source", "type"]:
            self.assertIn(name, schemas["TimelineEntry"]["required"])
            self.assertEqual(schemas["TimelineEntry"]["properties"][name], {"type": "string"})
        self.assertEqual(schemas["CreateNote"]["properties"]["text"],
                         {"type": "string", "minLength": 1, "maxLength": 1000})
        self.assertEqual(create["responses"]["201"]["headers"]["ETag"]["schema"],
                         {"type": "string", "pattern": '^"v[1-9][0-9]*"$'})
