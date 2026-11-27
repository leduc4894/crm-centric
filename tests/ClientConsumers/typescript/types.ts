import type { components, operations } from '@crm-centric/crm-client';
type Create = components['schemas']['CreateContact'];
type Update = components['schemas']['UpdateContact'];
type Rule = components['schemas']['AutomationRule'];
const create: Create = { displayName: 'sample', email: null };
const update: Update = { phone: null };
// @ts-expect-error displayName is required
const missing: Create = { email: null };
// @ts-expect-error unknown contact properties are not part of the contract
const extra: Create = { displayName: 'sample', tenant_id: 'sample' };
// @ts-expect-error displayName cannot be null
const invalid: Update = { displayName: null };
const rule: Rule = {ruleId: 'sample', version: 1, triggerEventType: 'crm.contact.created.v1', condition: {field: '/data/score', op: 'eq', value: null}, actions: [{type: 'module.send.v1', installationId: '00000000-0000-4000-8000-000000000001', input: {extra: 42}}]};
void [create, update, missing, extra, invalid, rule];

type ConditionalHeaders = operations['createContact']['parameters']['header'];
const upsertHeaders: ConditionalHeaders = { 'X-Tenant-Id': '00000000-0000-4000-8000-000000000001', 'If-Match': '"v1"', 'X-Contact-Id': '00000000-0000-4000-8000-000000000001' };
void upsertHeaders;

import { createApiClient } from '@crm-centric/crm-client';
const client = createApiClient('https://example.invalid');
void client.GET('/api/v1/contacts', { params: { header: { 'X-Tenant-Id': '00000000-0000-4000-8000-000000000001' }, query: { limit: 20, cursor: 'opaque', q: 'literal', tagId: '00000000-0000-4000-8000-000000000001', updatedSince: '2026-01-01T00:00:00Z' } } });
