import { afterEach, expect, it, vi } from 'vitest';
import { createCrmClient } from './crmClient';
import { exactNumber } from './losslessJson';

const id = '10000000-0000-4000-8000-000000000001';
const scope = new AbortController();
const invalidate = vi.fn();
const client = () => createCrmClient({ origin: 'http://localhost', tenantId: id, token: 'synthetic', signal: scope.signal, invalidate });
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); invalidate.mockClear(); });
it('uses contract query and tenant authenticated request options', async () => {
  const fetcher = vi.fn<(req: Request) => Promise<Response>>(async () => Response.json({ items: [], nextCursor: null }));
  vi.stubGlobal('fetch', fetcher);
  await client().listContacts({ q: 'A & B', limit: 10 });
  const req = fetcher.mock.calls[0]![0];
  expect(new URL(req.url).pathname).toBe('/api/v1/contacts');
  expect(new URL(req.url).searchParams.get('q')).toBe('A & B');
  expect(req.credentials).toBe('omit'); expect(req.redirect).toBe('error');
  expect(req.headers.get('authorization')).toBe('Bearer synthetic');
  expect(req.headers.get('x-tenant-id')).toBe(id);
});
it.each(['1e131072', '1e-16384', '123456789012345678901', '-0.1234500', '1.00E+0'])('round trips %s exactly', async token => {
  const fetcher = vi.fn(async (req: Request) => {
    if (req.method === 'GET') return new Response('{"value":' + token + '}');
    expect(await req.text()).toBe('{"value":' + token + '}');
    expect(req.headers.get('if-match')).toBe('"v1"');
    return Response.json({ id, displayName: 'A', version: 2, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' });
  });
  vi.stubGlobal('fetch', fetcher);
  const crm = client();
  const { data } = await crm.getField(id, id);
  expect(data).toEqual(exactNumber(token));
  await crm.setField(id, id, data, '"v1"');
});
it.each([401, 403, 404, 412, 503])('closes HTTP %i with safe diagnostics', async status => {
  vi.stubGlobal('fetch', async () => Response.json({ reasonToken: 'secret@example.com', correlationId: id }, { status }));
  await expect(client().listContacts()).rejects.toMatchObject({ status, reasonToken: 'WEB_CRM_API_ERROR', correlationId: id });
  expect(invalidate).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
});
it('rejects malformed response bodies', async () => {
  vi.stubGlobal('fetch', async () => Response.json({ items: [{}], nextCursor: null }));
  await expect(client().listContacts()).rejects.toMatchObject({ reasonToken: 'WEB_CRM_CONTRACT_INVALID' });
});
it('aborts on timeout', async () => {
  vi.useFakeTimers();
  vi.stubGlobal('fetch', (req: Request) => new Promise((_resolve, reject) => req.signal.addEventListener('abort', () => reject(req.signal.reason))));
  const result = expect(client().listContacts()).rejects.toMatchObject({ reasonToken: 'WEB_CRM_TIMEOUT' });
  await vi.advanceTimersByTimeAsync(10000);
  await result;
});
it.each(['{"value":1,"value":true}', '\u00a0{"value":1}', '{"value":01}'])('rejects ambiguous or invalid field JSON', async body => {
  vi.stubGlobal('fetch', async () => new Response(body));
  await expect(client().getField(id, id)).rejects.toMatchObject({ reasonToken: 'WEB_CRM_CONTRACT_INVALID' });
});
it('reads equivalent escaped JSON member names without losing numeric precision', async () => {
  vi.stubGlobal('fetch', async () => new Response('{"v\\u0061lue":123456789012345678901}'));
  expect((await client().getField(id, id)).data).toEqual(exactNumber('123456789012345678901'));
});
it('refreshes a missing note ETag before the next conditional write', async () => {
  const record = { id, displayName: 'Demo', version: 2, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' };
  const note = { id, contactId: id, inboxId: null, source: 'crm', type: 'note', occurredAt: record.updatedAt, summary: 'Demo' };
  const seen: string[] = [];
  vi.stubGlobal('fetch', async (req: Request) => {
    seen.push(req.method);
    if (req.method === 'POST') return Response.json(note, { status: 201 });
    if (req.method === 'GET') return Response.json(record, { headers: { etag: '"v2"' } });
    expect(req.headers.get('if-match')).toBe('"v2"');
    return Response.json(record);
  });
  const crm = client();
  const created = await crm.createNote(id, { text: 'Demo' }, '"v1"');
  await crm.patchContact(id, { displayName: 'Demo' }, await crm.requireETag(id, created.etag));
  expect(seen).toEqual(['POST', 'GET', 'PATCH']);
});
it('aborts an in-flight operation on caller cancellation', async () => {
  const controller = new AbortController();
  let request: Request | undefined;
  vi.stubGlobal('fetch', (req: Request) => { request = req; return new Promise((_resolve, reject) => req.signal.addEventListener('abort', () => reject(req.signal.reason))); });
  const result = expect(client().listContacts({}, controller.signal)).rejects.toMatchObject({ name: 'AbortError' });
  controller.abort();
  await result;
  expect(request?.signal.aborted).toBe(true);
});
it('preserves the platform correlation ID and permission reason', async () => {
  const correlationId = id.replaceAll('-', '');
  vi.stubGlobal('fetch', async () => Response.json({ reasonToken: 'TENANT_ACCESS_DENIED', correlationId }, { status: 403 }));
  await expect(client().listContacts()).rejects.toMatchObject({ correlationId, reasonToken: 'TENANT_ACCESS_DENIED' });
});
it('rejects malformed IDs before dispatching a scoped operation', async () => {
  const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher);
  await expect(client().getContact('..')).rejects.toMatchObject({ reasonToken: 'WEB_CRM_CONTRACT_INVALID' });
  expect(fetcher).not.toHaveBeenCalled();
});
it('refuses an untyped caller supplying a JavaScript number before any field write', async () => {
  const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher);
  // @ts-expect-error An untyped consumer can bypass the public FieldValue type.
  await expect(client().setField(id, id, 1.1, '"v1"')).rejects.toMatchObject({ reasonToken: 'WEB_CRM_CONTRACT_INVALID' });
  expect(fetcher).not.toHaveBeenCalled();
});
it('requires 204 before reporting contact deletion', async () => {
  vi.stubGlobal('fetch', vi.fn()
    .mockResolvedValueOnce(new Response(null, { status: 202 }))
    .mockResolvedValueOnce(new Response(null, { status: 204 })));
  const crm = client();
  await expect(crm.deleteContact(id, '"v1"')).rejects.toMatchObject({ reasonToken: 'WEB_CRM_CONTRACT_INVALID' });
  expect((await crm.deleteContact(id, '"v1"')).status).toBe(204);
});
it('accepts both contract upsert outcomes and rejects unrelated successful statuses', async () => {
  const record = { id, displayName: 'Demo', version: 1, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' };
  const sequence = [200, 201, 202, 201, 200];
  const note = { id, contactId: id, inboxId: null, source: 'crm', type: 'note', occurredAt: record.createdAt, summary: 'Demo' };
  vi.stubGlobal('fetch', async (req: Request) => Response.json(req.url.endsWith('/notes') ? note : record, { status: sequence.shift()! }));
  const crm = client();
  for (const status of [200, 201]) expect((await crm.createContact({ displayName: 'Demo' })).status).toBe(status);
  await expect(crm.createContact({ displayName: 'Demo' })).rejects.toMatchObject({ reasonToken: 'WEB_CRM_CONTRACT_INVALID' });
  await expect(crm.getContact(id)).rejects.toMatchObject({ reasonToken: 'WEB_CRM_CONTRACT_INVALID' });
  await expect(crm.createNote(id, { text: 'Demo' }, '"v1"')).rejects.toMatchObject({ reasonToken: 'WEB_CRM_CONTRACT_INVALID' });
});
