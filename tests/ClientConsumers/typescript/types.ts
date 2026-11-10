import type { components } from '@crm-centric/crm-client';
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
