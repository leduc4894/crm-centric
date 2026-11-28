import unittest
from pathlib import Path
import yaml

class ContactListContractTests(unittest.TestCase):
    def test_list_contract(self):
        api=yaml.safe_load((Path(__file__).resolve().parents[2]/'docs/contracts/crm-api.openapi.yaml').read_text())
        operation=api['paths']['/api/v1/contacts']['get']
        self.assertEqual('listContacts',operation['operationId'])
        self.assertEqual('#/components/schemas/ContactPage',operation['responses']['200']['content']['application/json']['schema']['$ref'])
        self.assertEqual('#/components/responses/Problem',operation['responses']['400']['$ref'])
        self.assertEqual({'items','nextCursor'},set(api['components']['schemas']['ContactPage']['required']))
        params=operation['parameters']
        self.assertTrue({'q','tagId','updatedSince'} <= {p.get('name') for p in params})
        self.assertTrue({'#/components/parameters/Limit','#/components/parameters/Cursor'} <= {p.get('$ref') for p in params})
        self.assertIn('null',api['components']['schemas']['ContactPage']['properties']['nextCursor']['type'])
        for status in ('400','401','403','503'):
            self.assertEqual('#/components/responses/Problem',operation['responses'][status]['$ref'])
        description=operation.get('description','').lower()
        for term in ('literal','updated_at','updatedsince','inclusive','collation','snapshot'):
            self.assertIn(term,description)
