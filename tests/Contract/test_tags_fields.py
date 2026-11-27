import unittest
from pathlib import Path

import yaml
from jsonschema import Draft202012Validator


class TagsFieldsContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        api = yaml.safe_load((Path(__file__).resolve().parents[2] / 'docs/contracts/crm-api.openapi.yaml').read_text())
        cls.schemas = api['components']['schemas']
        cls.parameters = api['components']['parameters']
        cls.paths = api['paths']

    def test_operations(self):
        for path, method, operation, scope, request, status, response in [
            ('tags', 'get', 'listTags', 'contacts.read', None, '200', 'TagPage'),
            ('tags', 'post', 'createTag', 'contacts.write', 'CreateTag', '201', 'Tag'),
            ('contacts/{id}/tags', 'get', 'listContactTags', 'contacts.read', None, '200', 'TagPage'),
            ('contacts/{id}/tags/{tagId}', 'put', 'addContactTag', 'contacts.write', None, '200', 'Contact'),
            ('contacts/{id}/tags/{tagId}', 'delete', 'removeContactTag', 'contacts.write', None, '200', 'Contact'),
            ('custom-fields', 'get', 'listCustomFields', 'contacts.read', None, '200', 'FieldDefinitionPage'),
            ('custom-fields', 'post', 'createCustomField', 'fields.manage', 'CreateField', '201', 'FieldDefinition'),
            ('contacts/{id}/fields/{fieldId}', 'get', 'getContactField', 'contacts.read', None, '200', 'SetFieldValue'),
            ('contacts/{id}/fields/{fieldId}', 'put', 'setContactField', 'contacts.write', 'SetFieldValue', '200', 'Contact'),
        ]:
            with self.subTest(operation=operation):
                op = self.paths['/api/v1/' + path][method]
                self.assertEqual(operation, op['operationId'])
                self.assertEqual(['crm.' + scope], op['x-required-scopes'])
                params = {p.get('$ref', '').split('/')[-1] for p in op['parameters']}
                self.assertIn('TenantContext', params)
                success = op['responses'][status]
                self.assertEqual('#/components/schemas/' + response, success['content']['application/json']['schema']['$ref'])
                if request:
                    self.assertTrue(op['requestBody']['required'])
                    self.assertEqual('#/components/schemas/' + request, op['requestBody']['content']['application/json']['schema']['$ref'])
                else:
                    self.assertNotIn('requestBody', op)
                if response == 'Contact':
                    self.assertIn('ETag', success['headers'])
                    self.assertIn('IfMatch', params)
                    for code in ('400', '412', '428'):
                        self.assertEqual('#/components/responses/Problem', op['responses'][code]['$ref'])
                    self.assertIn('no-op', op.get('description', ''))
                if operation.startswith('list'):
                    self.assertTrue({'Limit', 'Cursor'} <= params)
                    for term in ('UUID', 'ascending', 'snapshot', 'tenant'):
                        self.assertIn(term, op.get('description', ''))
                    page = self.schemas[response]
                    self.assertEqual({'items', 'nextCursor'}, set(page['required']))
                    self.assertEqual({'string', 'null'}, set(page['properties']['nextCursor']['type']))

    def test_input_and_pagination_semantics(self):
        self.assertEqual({'type': 'integer', 'minimum': 1, 'maximum': 100, 'default': 20}, self.parameters['Limit']['schema'])
        self.assertEqual(2048, self.parameters['Cursor']['schema']['maxLength'])
        field = self.schemas['CreateField']['properties']
        self.assertEqual({'string', 'number', 'boolean', 'date'}, set(field['dataType']['enum']))
        self.assertEqual('^[a-z][a-z0-9_]{0,63}$', field['key']['pattern'])
        self.assertIn('false', self.paths['/api/v1/custom-fields']['post'].get('description', ''))
        value = self.schemas['SetFieldValue']
        self.assertEqual(['value'], value['required'])
        self.assertEqual({'string', 'number', 'boolean', 'null'}, set(value['properties']['value']['type']))
        ops = self.paths['/api/v1/contacts/{id}/fields/{fieldId}']
        for term in ('YYYY-MM-DD', 'calendar', 'null', 'required'):
            self.assertIn(term, ops['put']['description'])
        self.assertIn('404', ops['get'].get('description', ''))
        self.assertIn('lower(btrim(name))', self.paths['/api/v1/tags']['post'].get('description', ''))

    def test_request_fixtures(self):
        tag = Draft202012Validator(self.schemas['CreateTag'])
        for name in (' ', ' A ', 'a' * 64):
            self.assertTrue(tag.is_valid({'name': name}))
        for body in ({}, {'name': ''}, {'name': 'a' * 65}, {'name': 'a', 'tenant_id': 'other'}):
            self.assertFalse(tag.is_valid(body))
        field = Draft202012Validator(self.schemas['CreateField'])
        definition = {'key': 'a_1', 'displayName': 'Date', 'dataType': 'date'}
        self.assertTrue(field.is_valid(definition))
        for patch in ({'key': 'A'}, {'dataType': 'object'}, {'displayName': ''}, {'required': None}):
            self.assertFalse(field.is_valid(definition | patch))
        value = Draft202012Validator(self.schemas['SetFieldValue'])
        for scalar in ('2024-02-29', 'ordinary text', 123456789012345678901, True, None):
            self.assertTrue(value.is_valid({'value': scalar}))
        for body in ({}, {'value': []}, {'value': {}}, {'value': True, 'extra': None}):
            self.assertFalse(value.is_valid(body))
