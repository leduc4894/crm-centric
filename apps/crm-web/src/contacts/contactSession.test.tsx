// @vitest-environment jsdom
import { act, cleanup, screen, waitFor, within } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { AuthClient } from '../auth/authClient';
import { AuthProvider, useAuth } from '../auth/AuthProvider';
import { TenantProvider, useTenant } from '../tenant/TenantProvider';
import { renderContactRoute } from './testRouter';

const tenantA = '11111111-1111-4111-8111-111111111111';
const tenantB = '22222222-2222-4222-8222-222222222222';
const contactId = '33333333-3333-4333-8333-333333333333';
const tenants = { items: [{ id: tenantA, name: 'A', status: 'active' }, { id: tenantB, name: 'B', status: 'active' }], nextCursor: null };
const page = (name: string, id = contactId, nextCursor: string | null = null) => Response.json({ items: [{ id, displayName: name, version: 1, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' }], nextCursor });
function client() { return new AuthClient({ issuer: 'https://id.example', clientId: 'web', callbackUrl: 'https://app.example/auth/callback', logoutUrl: 'https://app.example/', allowLocalHttp: false }); }
// Seed a synthetic authenticated session without performing a real OIDC exchange. AuthClient's
// real notification, invalidation and expiry methods still drive AuthProvider and TenantProvider.
function signIn(auth: AuthClient, token: string) {
  Reflect.set(auth, 'accessToken', token);
  Reflect.set(auth, 'expires', Date.now() + 60_000);
  Reflect.apply(Reflect.get(auth, 'setState'), auth, [{ status: 'authenticated', issuer: 'https://id.example', subject: 'same-user' }]);
}
function Harness({ children }: { children: React.ReactNode }) {
  const tenant = useTenant(); const auth = useAuth();
  return <><p data-testid="auth">{auth.state.status}</p>{tenant.items.map(item => <button key={item.id} onClick={() => tenant.selectTenant(item.id)}>Tenant {item.name}</button>)}{children}</>;
}
function mount(auth: AuthClient) { history.replaceState(null, '', '/app/contacts'); return renderContactRoute(children => <AuthProvider client={auth}><TenantProvider><Harness>{children}</Harness></TenantProvider></AuthProvider>); }
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it('a real AuthClient invalidation on a contact 401 clears the visible tenant session', async () => {
  const auth = client(); signIn(auth, 'one');
  let resolve!: (response: Response) => void;
  vi.stubGlobal('fetch', (request: Request | string) => typeof request === 'string' ? Promise.resolve(Response.json(tenants)) : new Promise<Response>(done => { resolve = done; }));
  mount(auth); await screen.findByText('Tenant A'); act(() => screen.getByText('Tenant A').click());
  await waitFor(() => expect(resolve).toBeDefined());
  await act(async () => resolve(new Response(null, { status: 401 })));
  await waitFor(() => expect(auth.getSnapshot().status).toBe('error'));
  expect(screen.getByTestId('auth').textContent).toBe('error');
  expect(screen.queryByText('Tenant A')).toBeNull();
});

it('ignores late A and B replies after switching A to B to A and refreshing the same identity', async () => {
  const auth = client(); signIn(auth, 'one');
  const pending: Array<(response: Response) => void> = [];
  vi.stubGlobal('fetch', (request: Request | string) => typeof request === 'string' ? Promise.resolve(Response.json(tenants)) : new Promise<Response>(done => pending.push(done)));
  mount(auth); await screen.findByText('Tenant A');
  act(() => screen.getByText('Tenant A').click()); await waitFor(() => expect(pending).toHaveLength(1));
  act(() => screen.getByText('Tenant B').click()); await waitFor(() => expect(pending).toHaveLength(2));
  act(() => screen.getByText('Tenant A').click()); await waitFor(() => expect(pending).toHaveLength(3));
  act(() => signIn(auth, 'two')); await waitFor(() => expect(pending).toHaveLength(4));
  await act(async () => pending[3]!(page('Current'))); await screen.findByText('Current');
  await act(async () => { pending[0]!(page('Old A')); pending[1]!(page('Old B')); pending[2]!(new Response(null, { status: 401 })); });
  expect(auth.getSnapshot().status).toBe('authenticated');
  expect(screen.queryByText('Old A')).toBeNull(); expect(screen.queryByText('Old B')).toBeNull();
  expect(screen.getByText('Current')).toBeTruthy();
});

it('table_scope_switch_drops_old_rows', async () => {
  const auth = client(); signIn(auth, 'one');
  const pending: Array<(response: Response) => void> = [];
  const requestedTenants: Array<string | null> = [];
  vi.stubGlobal('fetch', (request: Request | string) => {
    if (typeof request === 'string') return Promise.resolve(Response.json(tenants));
    requestedTenants.push(request.headers.get('X-Tenant-Id'));
    return new Promise<Response>(done => pending.push(done));
  });
  const select = async (name: string) => act(() => screen.getByRole('button', { name: 'Tenant ' + name }).click());
  const reply = async (index: number, response: Response) => {
    await waitFor(() => expect(pending[index]).toBeDefined());
    await act(async () => pending[index]!(response));
  };
  mount(auth); await screen.findByText('Tenant A'); await select('A');
  await reply(0, page('First A', contactId, 'more'));
  await screen.findByRole('table');
  act(() => screen.getByRole('button', { name: 'Tải thêm' }).click());
  await waitFor(() => expect(pending).toHaveLength(2));
  await select('B');
  expect(screen.queryByRole('table')).toBeNull();
  await reply(2, page('Tenant B record', tenantB));
  const tenantBTable = within(await screen.findByRole('table'));
  expect(tenantBTable.getAllByRole('link').map(link => link.textContent)).toEqual(['Tenant B record']);
  expect(tenantBTable.getByRole('link').getAttribute('href')).toBe('/app/contacts/' + tenantB);
  await select('A');
  expect(screen.queryByRole('table')).toBeNull();
  await reply(3, page('Returning A', contactId, 'more'));
  await screen.findByRole('table');
  act(() => screen.getByRole('button', { name: 'Tải thêm' }).click());
  await waitFor(() => expect(pending).toHaveLength(5));
  Reflect.set(auth, 'idToken', 'synthetic-id');
  Reflect.set(auth, 'metadata', async () => ({ end_session_endpoint: 'https://id.example/logout' }));
  Reflect.set(Reflect.get(auth, 'env'), 'logout', async () => undefined);
  await act(async () => auth.logout());
  expect(screen.queryByRole('table')).toBeNull();
  act(() => signIn(auth, 'two')); await screen.findByText('Tenant A'); await select('A');
  expect(screen.queryByRole('table')).toBeNull();
  const currentId = '44444444-4444-4444-8444-444444444444';
  await reply(5, page('New session', currentId));
  await screen.findByRole('table');
  await reply(1, page('Late first A'));
  await reply(4, page('Late previous session'));
  expect(requestedTenants).toEqual([tenantA, tenantA, tenantB, tenantA, tenantA, tenantA]);
  const links = within(screen.getByRole('table')).getAllByRole('link');
  expect(links.map(link => link.textContent)).toEqual(['New session']);
  expect(links[0]!.getAttribute('href')).toBe('/app/contacts/' + currentId);
  expect(screen.queryByRole('button', { name: 'Tải thêm' })).toBeNull();
  act(() => links[0]!.click());
  await waitFor(() => expect(location.pathname).toBe('/app/contacts/' + currentId));
});

it('ignores abort-resistant detail reads across tenant switches and logout/login on the same deep link', async () => {
  const auth = client(); signIn(auth, 'one');
  const pending: Array<(response: Response) => void> = [];
  vi.stubGlobal('fetch', (request: Request | string) => {
    if (typeof request === 'string') return Promise.resolve(Response.json(tenants));
    if (new URL(request.url).pathname.endsWith('/contacts/' + contactId)) return new Promise<Response>(done => pending.push(done));
    return Promise.resolve(Response.json({ items: [], nextCursor: null }));
  });
  history.replaceState(null, '', '/app/contacts/' + contactId);
  renderContactRoute(children => <AuthProvider client={auth}><TenantProvider><Harness>{children}</Harness></TenantProvider></AuthProvider>);
  const select = async (name: string) => act(() => screen.getByRole('button', { name: 'Tenant ' + name }).click());
  const contact = (name: string) => Response.json({ id: contactId, displayName: name, version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' }, { headers: { ETag: '"v1"' } });
  await screen.findByText('Tenant A'); await select('A'); await waitFor(() => expect(pending).toHaveLength(1));
  await select('B'); await waitFor(() => expect(pending).toHaveLength(2));
  await act(async () => pending[1]!(contact('Current B')));
  await screen.findByRole('heading', { name: 'Current B' });
  await act(async () => pending[0]!(contact('Old A')));
  expect(screen.queryByText('Old A')).toBeNull();
  expect(screen.getByRole('heading', { name: 'Current B' })).toBeTruthy();
  await select('A'); await waitFor(() => expect(pending).toHaveLength(3));
  Reflect.set(auth, 'idToken', 'synthetic-id');
  Reflect.set(auth, 'metadata', async () => ({ end_session_endpoint: 'https://id.example/logout' }));
  Reflect.set(Reflect.get(auth, 'env'), 'logout', async () => undefined);
  await act(async () => auth.logout());
  act(() => signIn(auth, 'two')); await screen.findByText('Tenant A'); await select('A');
  await waitFor(() => expect(pending).toHaveLength(4));
  await act(async () => pending[3]!(contact('Current session')));
  await screen.findByRole('heading', { name: 'Current session' });
  await act(async () => pending[2]!(new Response(null, { status: 401 })));
  expect(auth.getSnapshot().status).toBe('authenticated');
  expect(screen.queryByText('Old A')).toBeNull(); expect(screen.queryByText('Current B')).toBeNull();
  expect(screen.getByRole('heading', { name: 'Current session' })).toBeTruthy();
});

it('ignores a previous session delete completion after logout and login as the same principal', async () => {
  const auth = client(); signIn(auth, 'one');
  let finishDelete: ((response: Response) => void) | undefined;
  let reads = 0, writes = 0;
  vi.stubGlobal('fetch', (request: Request | string) => {
    if (typeof request === 'string') return Promise.resolve(Response.json(tenants));
    const path = new URL(request.url).pathname;
    if (request.method === 'DELETE') { writes++; return new Promise<Response>(done => { finishDelete = done; }); }
    if (path.endsWith('/contacts/' + contactId)) return Promise.resolve(Response.json({ id: contactId, displayName: 'Session ' + ++reads, version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' }, { headers: { ETag: '"v1"' } }));
    return Promise.resolve(Response.json({ items: [], nextCursor: null }));
  });
  history.replaceState(null, '', '/app/contacts/' + contactId);
  renderContactRoute(children => <AuthProvider client={auth}><TenantProvider><Harness>{children}</Harness></TenantProvider></AuthProvider>);
  await screen.findByText('Tenant A'); act(() => screen.getByRole('button', { name: 'Tenant A' }).click());
  await screen.findByRole('heading', { name: 'Session 1' });
  act(() => screen.getByRole('button', { name: 'Xóa liên hệ' }).click());
  act(() => screen.getByRole('button', { name: 'Xác nhận xóa' }).click());
  await waitFor(() => expect(finishDelete).toBeDefined());
  Reflect.set(auth, 'idToken', 'synthetic-id');
  Reflect.set(auth, 'metadata', async () => ({ end_session_endpoint: 'https://id.example/logout' }));
  Reflect.set(Reflect.get(auth, 'env'), 'logout', async () => undefined);
  await act(async () => auth.logout());
  act(() => signIn(auth, 'two')); await screen.findByText('Tenant A');
  act(() => screen.getByRole('button', { name: 'Tenant A' }).click());
  await screen.findByRole('heading', { name: 'Session 2' });
  await act(async () => finishDelete!(new Response(null, { status: 204 })));
  expect(screen.getByRole('heading', { name: 'Session 2' })).toBeTruthy();
  expect(location.pathname).toBe('/app/contacts/' + contactId);
  expect(writes).toBe(1);
});
