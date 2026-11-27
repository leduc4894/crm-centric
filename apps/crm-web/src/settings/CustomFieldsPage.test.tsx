// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryHistory, RouterProvider } from '@tanstack/react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { AuthClient } from '../auth/authClient';
import { AuthProvider } from '../auth/AuthProvider';
import { createAppRouter } from '../router';

vi.mock('../health/HealthStatus', () => ({ HealthStatus: () => null }));
window.scrollTo = () => {};
const a = '11111111-1111-4111-8111-111111111111', b = '22222222-2222-4222-8222-222222222222';
const id = '33333333-3333-4333-8333-333333333333';
const definition = (name: string) => ({ id, key: 'amount', displayName: name, dataType: 'number', required: false, version: 1 });
const page = (items: object[], nextCursor: string | null = null) => Response.json({ items, nextCursor });
const problem = (reasonToken: string, status: number) => Response.json({ reasonToken, correlationId: id }, { status });
const tenants = { items: [{ id: a, name: 'Tenant A', status: 'active' }, { id: b, name: 'Tenant B', status: 'active' }], nextCursor: null };
function mount(handler: (req: Request) => Promise<Response> | Response) {
  const auth = new AuthClient({ issuer: 'https://id.example', clientId: 'web', callbackUrl: 'https://app.example/auth/callback', logoutUrl: 'https://app.example/', allowLocalHttp: false });
  Reflect.set(auth, 'accessToken', 'synthetic'); Reflect.set(auth, 'expires', Date.now() + 60_000);
  Reflect.apply(Reflect.get(auth, 'setState'), auth, [{ status: 'authenticated', issuer: 'https://id.example', subject: 'user' }]);
  vi.stubGlobal('fetch', (request: Request | string) => typeof request === 'string' ? Promise.resolve(Response.json(tenants)) : handler(request));
  const router = createAppRouter(createMemoryHistory({ initialEntries: ['/app/settings/custom-fields'] }));
  render(<AuthProvider client={auth}><RouterProvider router={router} /></AuthProvider>);
}
const select = async (name = 'Tenant A') => { fireEvent.click(await screen.findByRole('button', { name })); };
const draft = async () => { fireEvent.change(screen.getByLabelText('Khóa trường'), { target: { value: 'amount' } }); fireEvent.change(screen.getByLabelText('Tên trường'), { target: { value: 'Amount' } }); fireEvent.change(screen.getByLabelText('Kiểu dữ liệu'), { target: { value: 'number' } }); await waitFor(() => { expect((screen.getByLabelText('Khóa trường') as HTMLInputElement).value).toBe('amount'); expect((screen.getByLabelText('Tên trường') as HTMLInputElement).value).toBe('Amount'); }); };
const submit = () => fireEvent.click(screen.getByRole('button', { name: 'Tạo trường' }));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it('opens the direct settings route without fetching until a tenant is selected and follows opaque page cursors', async () => {
  const urls: string[] = [];
  mount(req => { urls.push(req.url); return page([definition(urls.length === 1 ? 'First' : 'Second')], urls.length === 1 ? 'opaque-1' : null); });
  expect(await screen.findByText('Chọn tenant để xem định nghĩa trường.')).toBeTruthy();
  await screen.findByRole('button', { name: 'Tenant A' }); expect(urls).toHaveLength(0);
  await select(); await screen.findByText('First'); fireEvent.click(screen.getByRole('button', { name: 'Tải thêm' }));
  await screen.findByText('Second'); expect(new URL(urls[1]!).searchParams.get('cursor')).toBe('opaque-1');
});

it.each(['owner', 'admin', 'unknown'])('lets the server authorize creation for %s and refreshes the list', async () => {
  let created = false; const posts: Request[] = [];
  mount(req => { if (req.method === 'POST') { posts.push(req); created = true; return Response.json(definition('Amount'), { status: 201 }); } return page(created ? [definition('Amount')] : []); });
  await select(); await screen.findByRole('button', { name: 'Tạo trường' }); await draft(); submit();
  await screen.findByText('Amount', { selector: 'li' });
  expect(posts).toHaveLength(1); expect(await posts[0]!.clone().json()).toEqual({ key: 'amount', displayName: 'Amount', dataType: 'number', required: false });
  expect(posts[0]!.headers.get('X-Tenant-Id')).toBe(a); expect(posts[0]!.headers.has('If-Match')).toBe(false);
});

it.each(['agent', 'operator'])('shows server 403 for %s while preserving the draft', async () => {
  let posts = 0; mount(req => req.method === 'POST' ? (posts++, problem('TENANT_FORBIDDEN', 403)) : page([]));
  await select(); await screen.findByRole('button', { name: 'Tạo trường' }); await draft(); submit();
  expect((await screen.findByRole('alert')).textContent).toContain('TENANT_FORBIDDEN');
  expect((screen.getByLabelText('Khóa trường') as HTMLInputElement).value).toBe('amount'); expect(posts).toBe(1);
});

it('preserves duplicate and uncertain drafts and does not retry failed writes', async () => {
  let posts = 0; mount(req => req.method === 'POST' ? (++posts === 1 ? problem('FIELD_KEY_CONFLICT', 409) : Promise.reject(new Error('offline'))) : page([]));
  await select(); await screen.findByRole('button', { name: 'Tạo trường' }); await draft(); submit();
  expect((await screen.findByRole('alert')).textContent).toContain('FIELD_KEY_CONFLICT');
  expect((screen.getByLabelText('Tên trường') as HTMLInputElement).value).toBe('Amount'); submit();
  await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('WEB_CRM_NETWORK_ERROR'));
  expect(posts).toBe(2); expect((screen.getByLabelText('Tên trường') as HTMLInputElement).value).toBe('Amount');
});

it('drops old list and create completions when switching tenant', async () => {
  let oldList!: (response: Response) => void, oldCreate!: (response: Response) => void;
  let aReads = 0; let bReads = 0;
  mount(req => {
    if (req.headers.get('X-Tenant-Id') === b) { bReads++; return page([definition('Current B')]); }
    if (req.method === 'POST') return new Promise<Response>(resolve => { oldCreate = resolve; });
    if (++aReads === 1) return new Promise<Response>(resolve => { oldList = resolve; });
    return page([]);
  });
  await select(); await waitFor(() => expect(oldList).toBeDefined()); await select('Tenant B'); await screen.findByText('Current B');
  await act(async () => oldList(page([definition('Stale A')]))); expect(screen.queryByText('Stale A')).toBeNull();
  await select(); await screen.findByRole('button', { name: 'Tạo trường' }); await draft(); submit(); await waitFor(() => expect(oldCreate).toBeDefined());
  await select('Tenant B'); await screen.findByText('Current B'); const count = bReads;
  await act(async () => oldCreate(Response.json(definition('Stale created'), { status: 201 })));
  expect(screen.queryByText('Stale created')).toBeNull(); expect(bReads).toBe(count);
});

it('reports a cyclic page cursor rather than silently hiding the rest of the list', async () => {
  mount(req => page([definition(new URL(req.url).searchParams.has('cursor') ? 'Second' : 'First')], 'repeat'));
  await select(); await screen.findByText('First'); fireEvent.click(screen.getByRole('button', { name: 'Tải thêm' }));
  expect((await screen.findByRole('alert')).textContent).toContain('WEB_CRM_CONTRACT_INVALID');
});

it('does not retry a timed-out create and requires a fresh review while retaining the draft', async () => {
  let posts = 0;
  mount(req => {
    if (req.method !== 'POST') return page([]);
    posts++;
    return new Promise<Response>((_resolve, reject) => req.signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError'))));
  });
  await select(); await screen.findByRole('button', { name: 'Tạo trường' }); await draft();
  vi.useFakeTimers();
  try {
    submit();
    await act(async () => { await vi.advanceTimersByTimeAsync(10_000); });
    expect(screen.getByRole('alert').textContent).toContain('WEB_CRM_TIMEOUT');
    expect((screen.getByLabelText('Tên trường') as HTMLInputElement).value).toBe('Amount');
    expect(screen.getByRole('button', { name: 'Tạo trường' }).matches(':disabled')).toBe(true);
    expect(posts).toBe(1);
  } finally { vi.useRealTimers(); }
});

it('refreshes three already-loaded pages after a successful create without treating a valid cursor as a cycle', async () => {
  const urls: string[] = []; let created = false;
  mount(req => {
    if (req.method === 'POST') { created = true; return Response.json(definition('New'), { status: 201 }); }
    const cursor = new URL(req.url).searchParams.get('cursor'); urls.push(cursor ?? 'first');
    return page([definition(created && cursor === 'two' ? 'New' : cursor ?? 'First')], cursor === null ? 'one' : cursor === 'one' ? 'two' : null);
  });
  await select(); await screen.findByText('First');
  fireEvent.click(screen.getByRole('button', { name: 'Tải thêm' })); await screen.findByText('one');
  fireEvent.click(screen.getByRole('button', { name: 'Tải thêm' })); await screen.findByText('two');
  await draft(); submit(); await screen.findByText('New');
  expect(screen.queryByRole('alert')).toBeNull();
  expect(urls).toEqual(['first', 'one', 'two', 'first', 'one', 'two']);
});

it('shows the list failure reason and correlation and lets the user retry the list', async () => {
  let reads = 0;
  mount(() => ++reads === 1 ? problem('CRM_STORE_UNAVAILABLE', 503) : page([definition('Recovered')]));
  await select();
  const alert = await screen.findByRole('alert');
  expect(alert.textContent).toContain('CRM_STORE_UNAVAILABLE'); expect(alert.textContent).toContain(id);
  fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
  await screen.findByText('Recovered'); expect(reads).toBe(2);
});

it('accepts 200 Unicode scalars and rejects 201 without narrowing the contract', async () => {
  let posts = 0;
  mount(req => req.method === 'POST' ? (posts++, Response.json(definition('Created'), { status: 201 })) : page([]));
  await select(); await screen.findByRole('button', { name: 'Tạo trường' });
  fireEvent.change(screen.getByLabelText('Khóa trường'), { target: { value: 'amount' } });
  fireEvent.change(screen.getByLabelText('Tên trường'), { target: { value: '🙂'.repeat(201) } });
  await waitFor(() => expect((screen.getByLabelText('Khóa trường') as HTMLInputElement).value).toBe('amount'));
  submit(); expect(posts).toBe(0);
  fireEvent.change(screen.getByLabelText('Tên trường'), { target: { value: '🙂'.repeat(200) } });
  submit(); await waitFor(() => expect(posts).toBe(1));
});

it('retains the definition draft and locks creation when a post-write list refresh fails', async () => {
  let posts = 0; let reads = 0;
  mount(req => req.method === 'POST' ? (posts++, Response.json(definition('Created'), { status: 201 })) : (++reads === 1 ? page([]) : problem('CRM_STORE_UNAVAILABLE', 503)));
  await select(); await screen.findByRole('button', { name: 'Tạo trường' }); await draft(); submit();
  await screen.findByRole('button', { name: 'Tải lại dữ liệu' });
  expect((screen.getByLabelText('Tên trường') as HTMLInputElement).value).toBe('Amount');
  expect(screen.getByRole('button', { name: 'Tạo trường' }).matches(':disabled')).toBe(true);
  expect(posts).toBe(1);
});

it('refreshes a list that was still loading when creation succeeded', async () => {
  let release!: (response: Response) => void; let reads = 0; let posts = 0;
  mount(req => {
    if (req.method === 'POST') { posts++; return Response.json(definition('Created'), { status: 201 }); }
    if (++reads === 1) return new Promise<Response>(resolve => { release = resolve; });
    return page([definition('Created')]);
  });
  await select(); await waitFor(() => expect(release).toBeDefined()); await draft(); submit();
  await waitFor(() => expect(posts).toBe(1));
  await act(async () => release(page([])));
  await screen.findByText('Created', { selector: 'li' });
  expect(reads).toBeGreaterThan(1);
});

it('validates the definition key and sends the selected type and required flag without an ETag', async () => {
  const posts: Request[] = [];
  mount(req => { if (req.method === 'POST') { posts.push(req); return Response.json(definition('Created'), { status: 201 }); } return page([]); });
  await select(); await screen.findByRole('button', { name: 'Tạo trường' }); await draft();
  fireEvent.change(screen.getByLabelText('Khóa trường'), { target: { value: 'Bad-key' } });
  fireEvent.submit(screen.getByLabelText('Khóa trường').closest('form')!);
  expect(posts).toHaveLength(0);
  fireEvent.change(screen.getByLabelText('Khóa trường'), { target: { value: 'valid_key' } });
  fireEvent.click(screen.getByLabelText('Bắt buộc'));
  submit(); await waitFor(() => expect(posts).toHaveLength(1));
  expect(await posts[0]!.clone().json()).toEqual({ key: 'valid_key', displayName: 'Amount', dataType: 'number', required: true });
  expect(posts[0]!.headers.has('If-Match')).toBe(false);
});
