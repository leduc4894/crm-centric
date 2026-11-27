import { expect, it } from 'vitest';
import { contactMethods } from './contactsProxy';
const id = '10000000-0000-4000-8000-000000000001';
it('allowlists only contact API resources and supported methods', () => {
  expect(contactMethods('/api/v1/contacts?limit=10')).toEqual(['GET', 'POST']);
  expect(contactMethods('/api/v1/contacts/' + id + '/notes')).toEqual(['POST']);
  for (const path of ['/api/v1/contacts/' + id + '/identity-links', '/api/v1/contacts/%2e%2e', '/auth/callback', '/logout', '//example.test/api/v1/contacts']) expect(contactMethods(path)).toBeUndefined();
});

import { createServer as httpServer } from 'node:http';
import { createServer } from 'vite';
import { createWebConfig } from './devServer';

it('relays a conditional note body and ETag without forwarding ambient credentials', async () => {
  const calls: { headers: Record<string, unknown>; body: string }[] = [];
  const api = httpServer(async (req, res) => {
    if (req.method === 'GET' && req.url === '/api/v1/contacts/ABCDEF01-0000-4000-8000-000000000001') {
      res.writeHead(200, { etag: '"v3"' }); res.end('{}'); return;
    }
    let body = '';
    for await (const chunk of req) body += chunk;
    calls.push({ headers: req.headers, body });
    res.writeHead(201, { etag: '"v2"', 'set-cookie': 'sample=unused' }); res.end('{}');
  });
  await new Promise<void>(resolve => api.listen(0, '127.0.0.1', resolve));
  const address = api.address(); if (!address || typeof address === 'string') throw Error('TCP port missing');
  const server = await createServer({ ...createWebConfig({ LOCAL_API_PORT: String(address.port) }), configFile: false, appType: 'custom' });
  try {
    await server.listen(0);
    const addr = server.httpServer?.address(); if (!addr || typeof addr === 'string') throw Error('TCP port missing');
    const path = 'http://127.0.0.1:' + addr.port + '/api/v1/contacts/' + id + '/notes';
    const result = await fetch(path, { method: 'POST', headers: { authorization: 'Bearer synthetic', 'x-tenant-id': id, 'if-match': '"v1"', cookie: 'sample=unused', 'content-type': 'application/json' }, body: '{"text":"Demo"}' });
    expect(result.status).toBe(201);
    expect(result.headers.get('etag')).toBe('"v2"'); expect(result.headers.has('set-cookie')).toBe(false);
    expect(calls[0]?.headers.cookie).toBeUndefined();
    expect(calls[0]).toMatchObject({ body: '{"text":"Demo"}', headers: { authorization: 'Bearer synthetic', 'x-tenant-id': id, 'if-match': '"v1"' } });
    expect((await fetch(path)).status).toBe(405); expect(calls).toHaveLength(1);
    const uppercaseRecord = await fetch('http://127.0.0.1:' + addr.port + '/api/v1/contacts/ABCDEF01-0000-4000-8000-000000000001');
    expect(uppercaseRecord.status).toBe(200);
    expect(uppercaseRecord.headers.get('etag')).toBe('"v3"');
  } finally { await server.close(); await new Promise<void>(resolve => api.close(() => resolve())); }
});
