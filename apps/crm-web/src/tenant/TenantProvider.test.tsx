// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { AuthProvider } from '../auth/AuthProvider';
import type { AuthClient, AuthState } from '../auth/authClient';
import { TenantProvider, useTenant } from './TenantProvider';

const first = { id: '11111111-1111-4111-8111-111111111111', name: 'First', status: 'active' };
const suspended = { id: '22222222-2222-4222-8222-222222222222', name: 'Suspended', status: 'suspended' };
function fixture() {
  let state: AuthState = { status: 'authenticated', issuer: 'https://id.example/', subject: 'user-a' };
  let listener: (value: AuthState) => void = () => undefined;
  const client = { getSnapshot: () => state, subscribe: (l: typeof listener) => { listener = l; return () => undefined; }, getAccessToken: () => state.status === 'authenticated' ? 'synthetic' : null, login: async () => undefined, logout: async () => undefined, invalidate: () => { state = { status: 'anonymous' }; listener(state); } } as unknown as AuthClient;
  const change = (next: AuthState) => { state = next; listener(next); };
  return { client, change };
}
function Probe() { const tenant = useTenant(); return <><p>{tenant.status}</p><p>{tenant.selectedTenantId ?? 'none'}</p>{tenant.items.map((t) => <button key={t.id} onClick={() => tenant.selectTenant(t.id)}>{t.name}</button>)}<button onClick={() => void tenant.loadMore()}>More</button><p>{tenant.reasonToken}</p></>; }
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
it('loads all requested pages and selects only an active tenant', async () => {
  const f = fixture(); vi.stubGlobal('fetch', vi.fn(async (url: string) => new Response(JSON.stringify(url.includes('?') ? { items: [suspended], nextCursor: null } : { items: [first], nextCursor: 'page2' }))));
  render(<AuthProvider client={f.client}><TenantProvider><Probe /></TenantProvider></AuthProvider>);
  await screen.findByText('First'); act(() => screen.getByText('First').click()); expect(screen.getByText(first.id)).toBeTruthy();
  act(() => screen.getByText('More').click()); await screen.findByText('Suspended'); act(() => screen.getByText('Suspended').click()); expect(screen.getByText(first.id)).toBeTruthy();
});
it('drops pending results after logout or identity change', async () => {
  let resolve!: (value: Response) => void; vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>((r) => { resolve = r; })));
  const f = fixture(); render(<AuthProvider client={f.client}><TenantProvider><Probe /></TenantProvider></AuthProvider>);
  await waitFor(() => expect(resolve).toBeDefined()); act(() => f.change({ status: 'anonymous' }));
  await act(async () => resolve(new Response(JSON.stringify({ items: [first], nextCursor: null }))));
  expect(screen.queryByText('First')).toBeNull(); expect(screen.getByText('none')).toBeTruthy();
});
it('invalidates authentication on a 401', async () => {
  const f = fixture(); vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 401 })));
  render(<AuthProvider client={f.client}><TenantProvider><Probe /></TenantProvider></AuthProvider>);
  await waitFor(() => expect(f.client.getSnapshot().status).toBe('anonymous'));
});
it('never applies an old account response after switching to another account', async () => {
  const pending: ((value: Response) => void)[] = [];
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>((resolve) => pending.push(resolve))));
  const f = fixture(); render(<AuthProvider client={f.client}><TenantProvider><Probe /></TenantProvider></AuthProvider>);
  await waitFor(() => expect(pending).toHaveLength(1));
  act(() => f.change({ status: 'authenticated', issuer: 'https://id.example/', subject: 'user-b' }));
  await waitFor(() => expect(pending).toHaveLength(2));
  await act(async () => pending[1]!(new Response(JSON.stringify({ items: [], nextCursor: null }))));
  await act(async () => pending[0]!(new Response(JSON.stringify({ items: [first], nextCursor: null }))));
  expect(screen.queryByText('First')).toBeNull(); expect(screen.getByText('ready')).toBeTruthy();
});
it('rejects repeated pagination cursors instead of presenting a loop', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ items: [first], nextCursor: 'same' }))));
  const f = fixture(); render(<AuthProvider client={f.client}><TenantProvider><Probe /></TenantProvider></AuthProvider>);
  await screen.findByText('First'); act(() => screen.getByText('More').click());
  await screen.findByText('WEB_TENANT_CONTRACT_INVALID');
});
