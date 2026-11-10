import assert from 'node:assert/strict';
import { createApiClient as operations } from '@crm-centric/operations-client';
import { createApiClient as crm } from '@crm-centric/crm-client';

const id = '00000000-0000-4000-8000-000000000001';
const correlation = 'sample-correlation-01';
const sentinel = 'synthetic-secret-sentinel-not-a-credential';
let calls = 0;
let handler;
globalThis.fetch = async request => { calls++; return handler(request); };
const problem = status => ({ type: 'urn:sample', title: 'sample', status, reasonToken: `TEST_${status}`, correlationId: correlation });
const response = (body, status, extra = {}) => new Response(JSON.stringify(body), {status, headers: {'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json', 'X-Correlation-Id': correlation, ...extra}});
const op = operations('https://example.invalid');
for (const status of [200, 500, 503]) {
  handler = request => {
    assert.equal(request.url, 'https://example.invalid/health/ready');
    assert.equal(request.credentials, 'omit');
    assert.equal(request.headers.has('authorization'), false);
    assert.equal(request.headers.has('x-tenant-id'), false);
    assert.equal(request.headers.has('cookie'), false);
    return response(status === 200 ? {status: 'ready'} : problem(status), status);
  };
  const result = await op.GET('/health/ready');
  assert.equal(result.response.status, status);
  assert.equal(result.response.headers.get('X-Correlation-Id'), correlation);
  if (status === 200) assert.equal(result.data.status, 'ready');
  else assert.equal(result.error.reasonToken, `TEST_${status}`);
}
const business = crm('https://example.invalid');
business.use({onRequest({request}) { request.headers.set('Authorization', `Bearer ${sentinel}`); return request; }});
handler = request => {
  assert.equal(request.headers.get('X-Tenant-Id'), id);
  assert.equal(request.headers.get('Authorization'), `Bearer ${sentinel}`);
  assert.equal(new URL(request.url).searchParams.get('cursor'), 'a+b/=?');
  assert.equal(new URL(request.url).searchParams.get('tagId'), id);
  assert.equal(new URL(request.url).searchParams.get('updatedSince'), '2026-01-01T00:00:00Z');
  assert.equal(new URL(request.url).searchParams.get('limit'), '42');
  return response({items: [], nextCursor: null}, 200);
};
await business.GET('/api/v1/contacts', {params: {header: {'X-Tenant-Id': id}, query: {cursor: 'a+b/=?', tagId: id, updatedSince: '2026-01-01T00:00:00Z', limit: 42}}});
handler = () => response({id, displayName: 'sample', email: null}, 200, {ETag: '"v1"'});
let got = await business.GET('/api/v1/contacts/{id}', {params: {header: {'X-Tenant-Id': id}, path: {id}}});
assert.equal(got.response.headers.get('ETag'), '"v1"');
for (const status of [412, 428]) {
  const before = calls;
  handler = async request => {
    assert.equal(request.headers.get('If-Match'), '"v1"');
    const body = await request.json();
    assert.deepEqual(body, {email: null});
    assert.equal('tenant_id' in body, false);
    return response(problem(status), status);
  };
  got = await business.PATCH('/api/v1/contacts/{id}', {params: {header: {'X-Tenant-Id': id, 'If-Match': '"v1"'}, path: {id}}, body: {email: null}});
  assert.equal(got.response.status, status);
  assert.equal(got.error.reasonToken, `TEST_${status}`);
  assert.equal(got.error.correlationId, correlation);
  assert.equal(calls - before, 1);
}
handler = request => { assert.equal(request.headers.has('Authorization'), false); assert.equal(request.headers.has('X-Tenant-Id'), false); return response({status: 'ready'}, 200); };
await op.GET('/health/ready');
const controller = new AbortController();
handler = request => { assert.equal(request.signal.aborted, true); throw new DOMException('Cancelled', 'AbortError'); };
controller.abort();
await assert.rejects(op.GET('/health/ready', {signal: controller.signal}), {name: 'AbortError'});
const rule = {ruleId: 'sample', version: 1, triggerEventType: 'crm.contact.created.v1', condition: {all: [{field: '/data/score', op: 'eq', value: null}]}, actions: [{type: 'crm.createNote.v1', input: {text: 'sample'}}, {type: 'module.send.v1', installationId: id, input: {message: 'sample', extra: 123}}]};
handler = async request => { assert.deepEqual(await request.json(), rule); return response(rule, 201); };
got = await business.POST('/api/v1/automations/rules', {body: rule, params: {header: {'X-Tenant-Id': id}}});
assert.deepEqual(got.data, rule);
console.log('TYPESCRIPT_WIRE_OK');
