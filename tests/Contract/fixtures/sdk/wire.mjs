import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
const { createApiClient } = await import(pathToFileURL(process.argv[2]));
const rule = {
  ruleId: 'sample', version: 1, triggerEventType: 'crm.contact.created.v1',
  condition: { all: [{ field: '/data/score', op: 'eq', value: null }] },
  actions: [
    { type: 'crm.createNote.v1', input: { text: 'sample' } },
    { type: 'module.send.v1', installationId: '00000000-0000-4000-8000-000000000001', input: { message: 'sample' } },
  ],
};
let calls = 0;
globalThis.fetch = async (request) => {
  calls++;
  assert.equal(request.credentials, 'omit');
  assert.deepEqual(await request.json(), rule);
  return new Response(JSON.stringify(rule), { status: 201, headers: { 'Content-Type': 'application/json' } });
};
const client = createApiClient('https://example.invalid');
const { data } = await client.POST('/api/v1/automations/rules', { body: rule, headers: { 'X-Tenant-Id': '00000000-0000-4000-8000-000000000001' } });
assert.equal(calls, 1);
assert.deepEqual(data, rule);
console.log('SDK_RULE_ROUNDTRIP_OK');
