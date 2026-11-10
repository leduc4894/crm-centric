import type { components } from '../../../../sdks/typescript/crm/schema.js';

type Rule = components['schemas']['AutomationRule'];
const local: Rule = {
  ruleId: 'sample', version: 1, triggerEventType: 'crm.contact.created.v1',
  condition: { all: [{ field: '/data/score', op: 'eq', value: null }] },
  actions: [{ type: 'crm.addTag.v1', input: { tag: 'sample' } }],
};
const remote: Rule = {
  ruleId: 'sample', version: 1, triggerEventType: 'crm.contact.created.v1',
  condition: { field: '/data/score', op: 'gt', value: 100 },
  actions: [
    { type: 'crm.createNote.v1', input: { text: 'sample' } },
    { type: 'module.send.v1', installationId: '00000000-0000-4000-8000-000000000001', input: { message: 'sample' } },
  ],
};
void [local, remote];
