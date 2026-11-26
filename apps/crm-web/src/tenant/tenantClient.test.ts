// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { listTenants } from './tenantClient';

const tenant = { id: '11111111-1111-4111-8111-111111111111', name: 'Example', status: 'active' };
afterEach(() => vi.unstubAllGlobals());
describe('listTenants', () => {
  it('sends bearer only to the same-origin tenant list and makes cursors available', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({ items: [tenant], nextCursor: 'next+page' }), { status: 200 }));
    vi.stubGlobal('fetch', fetcher);
    expect(await listTenants('synthetic-token', 'previous')).toEqual({ items: [tenant], nextCursor: 'next+page' });
    const [url, init] = fetcher.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/v1/tenants?cursor=previous');
    expect(new Headers(init.headers).get('authorization')).toBe('Bearer synthetic-token');
    expect(new Headers(init.headers).has('x-tenant-id')).toBe(false);
    expect(init.credentials).toBe('omit'); expect(init.redirect).toBe('error');
  });
  it('rejects malformed pages, cursor and server payload without leaking it', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ items: [{ ...tenant, status: 'unknown' }], nextCursor: null }))));
    await expect(listTenants('synthetic-token')).rejects.toThrow(/WEB_TENANT_CONTRACT_INVALID [\w-]+/);
    await expect(listTenants('synthetic-token', 'x'.repeat(2049))).rejects.toThrow(/WEB_TENANT_CONTRACT_INVALID [\w-]+/);
  });
  it('classifies 401 and safe server errors without disclosing payload', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('sensitive-payload', { status: 401 })));
    await expect(listTenants('synthetic-token')).rejects.toMatchObject({ reasonToken: 'WEB_TENANT_UNAUTHORIZED', status: 401 });
  });
  it('bounds unresponsive requests', async () => {
    vi.useFakeTimers();
    vi.stubGlobal('fetch', vi.fn((_url, options: RequestInit) => new Promise((_resolve, reject) => options.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError'))))));
    const pending = listTenants('synthetic-token'); const assertion = expect(pending).rejects.toMatchObject({ reasonToken: 'WEB_TENANT_TIMEOUT' });
    await vi.advanceTimersByTimeAsync(10001); await assertion; vi.useRealTimers();
  });
});
