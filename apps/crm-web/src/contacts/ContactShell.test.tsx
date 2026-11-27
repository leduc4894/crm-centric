// @vitest-environment jsdom
import { act, cleanup, fireEvent, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { renderContactRoute as renderShell } from './testRouter';
const a = 'ABCDEF01-0000-4000-8000-000000000001', b = '10000000-0000-4000-8000-000000000002';
let tenant: string | null = null;
let identity: string | null = 'principal';
const auth = { getAccessToken: () => 'synthetic', invalidate: vi.fn(), getSnapshot: () => ({ status: 'authenticated', issuer: 'issuer', subject: identity }) };
vi.mock('../tenant/TenantProvider', () => ({ useTenant: () => ({ selectedTenantId: tenant, identity: identity && JSON.stringify(['issuer', identity]) }) }));
let authState = { status: 'authenticated', issuer: 'issuer', subject: 'principal' };
vi.mock('../auth/AuthProvider', () => ({ useAuthClient: () => auth, useAuth: () => ({ state: authState }) }));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); tenant = null; identity = 'principal'; authState = { status: 'authenticated', issuer: 'issuer', subject: 'principal' }; });
it('does not display a cached list after editing a record when the new list read fails', async () => {
  const contact = { id: a, displayName: 'Old name', version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
  let lists = 0, finishList: ((response: Response) => void) | undefined;
  vi.stubGlobal('fetch', async (req: Request) => {
    const path = new URL(req.url).pathname;
    if (path.endsWith('/contacts')) {
      if (++lists === 1) return Response.json({ items: [contact], nextCursor: null });
      return new Promise<Response>(resolve => { finishList = resolve; });
    }
    if (path.endsWith(a)) return Response.json(req.method === 'PATCH' ? { ...contact, displayName: 'New name', version: 2 } : contact, { headers: { ETag: req.method === 'PATCH' ? '"v2"' : '"v1"' } });
    return Response.json({ items: [], nextCursor: null });
  });
  history.replaceState(null, '', '/app/contacts'); tenant = a; renderShell();
  fireEvent.click(await screen.findByRole('link', { name: 'Old name' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Chỉnh sửa' }));
  fireEvent.change(screen.getByLabelText('Tên liên hệ'), { target: { value: 'New name' } });
  fireEvent.click(screen.getByRole('button', { name: 'Lưu' }));
  await screen.findByRole('heading', { name: 'New name' });
  fireEvent.click(screen.getByRole('link', { name: 'Liên hệ' }));
  await waitFor(() => expect(finishList).toBeDefined());
  expect(screen.queryByText('Old name')).toBeNull();
  await act(async () => finishList!(new Response(null, { status: 503 })));
  await screen.findByRole('alert');
  expect(screen.queryByText('Old name')).toBeNull();
});
it('opens the dedicated record and follows browser history', async () => {
  const record = { id: a, displayName: 'Demo', version: 1, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' };
  vi.stubGlobal('fetch', async (req: Request) => Response.json(new URL(req.url).pathname.endsWith(a) ? record : { items: [record], nextCursor: null }));
  history.replaceState(null, '', '/app/contacts/' + a); tenant = a; renderShell();
  expect(await screen.findByRole('heading', { name: 'Demo' })).toBeTruthy();
  act(() => screen.getByRole('link', { name: 'Liên hệ' }).click());
  await screen.findByRole('link', { name: 'Demo' }); act(() => screen.getByRole('link', { name: 'Demo' }).click());
  expect(await screen.findByRole('heading', { name: 'Demo' })).toBeTruthy();
  act(() => history.back()); await screen.findByRole('link', { name: 'Demo' });
  act(() => history.forward()); await screen.findByRole('heading', { name: 'Demo' });
});
it('resets immediately and ignores delayed previous tenant responses', async () => {
  history.replaceState(null, '', '/app/contacts');
  const pending: Array<(r: Response) => void> = [];
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => pending.push(resolve))));
  const view = renderShell(); expect(fetch).not.toHaveBeenCalled();
  tenant = a; view.rerender(); await waitFor(() => expect(pending).toHaveLength(1));
  tenant = b; view.rerender(); await waitFor(() => expect(pending).toHaveLength(2));
  await act(async () => pending[0]!(Response.json({ items: [], nextCursor: null }, { status: 403 })));
  expect(screen.queryByRole('alert')).toBeNull();
  identity = null; tenant = null; view.rerender();
  await act(async () => pending[1]!(new Response(null, { status: 401 })));
  expect(auth.invalidate).not.toHaveBeenCalled();
});
it('removes visible records synchronously on tenant and principal changes', async () => {
  history.replaceState(null, '', '/app/contacts');
  tenant = a;
  const pending: Array<(response: Response) => void> = [];
  vi.stubGlobal('fetch', () => new Promise<Response>(resolve => pending.push(resolve)));
  const view = renderShell();
  const record = { id: a, displayName: 'Tenant A only', version: 1, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' };
  const page = (displayName: string) => Response.json({ items: [{ ...record, displayName }], nextCursor: null });
  await waitFor(() => expect(pending).toHaveLength(1));
  await act(async () => pending[0]!(page(record.displayName)));
  expect(await screen.findByRole('link', { name: record.displayName })).toBeTruthy();
  tenant = b; view.rerender();
  expect(screen.queryByText(record.displayName)).toBeNull();
  await waitFor(() => expect(pending).toHaveLength(2));
  identity = 'next-principal'; view.rerender();
  await waitFor(() => expect(pending).toHaveLength(3));
  await act(async () => pending[2]!(page('Current')));
  await act(async () => pending[1]!(page('Previous principal')));
  expect(screen.queryByText('Previous principal')).toBeNull();
  expect(await screen.findByRole('link', { name: 'Current' })).toBeTruthy();
  identity = null; view.rerender();
  expect(screen.queryByText('Current')).toBeNull();
});
it.each(['tenant','principal','contact'])('discards pending activity and visible attribute drafts when the %s changes', async dimension => {
  tenant=a;
  history.replaceState(null,'','/app/contacts/'+a);
  let finish: ((response:Response)=>void) | undefined;
  const page=(items:unknown[]=[])=>Response.json({items,nextCursor:null});
  vi.stubGlobal('fetch',async(req:Request)=>{
    const path=new URL(req.url).pathname;
    if(path.endsWith('/timeline') && !finish) return new Promise<Response>(resolve=>{finish=resolve;});
    if(path.endsWith(a) || path.endsWith(b)) return Response.json({id:path.split('/').at(-1),displayName:'Profile',version:1,createdAt:'2026-01-01',updatedAt:'2026-01-01'});
    return page();
  });
  const view=renderShell();
  const input=await screen.findByLabelText('Tên thẻ mới');
  fireEvent.change(input,{target:{value:'Old draft'}});
  if(dimension==='tenant') tenant=b;
  else if(dimension==='principal') identity='new';
  else await act(async () => { await view.router.navigate({ to: '/app/contacts/$contactId', params: { contactId: b } }); });
  view.rerender();
  expect(screen.queryByDisplayValue('Old draft')).toBeNull();
  await screen.findByText('Chưa có hoạt động.');
  await act(async()=>finish!(page([{id:b,contactId:a,occurredAt:'2026-01-01',source:'crm',type:'note',inboxId:null,summary:'Old activity'}])));
  expect(screen.queryByText('Old activity')).toBeNull();
});

it('hides the previous session immediately when the same principal authenticates again', async () => {
  history.replaceState(null, '', '/app/contacts'); tenant = a;
  const pending: Array<(response: Response) => void> = [];
  vi.stubGlobal('fetch', () => new Promise<Response>(resolve => pending.push(resolve)));
  const view = renderShell();
  const page = (displayName: string) => Response.json({ items: [{ id: a, displayName, version: 1, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' }], nextCursor: null });
  await waitFor(() => expect(pending).toHaveLength(1));
  await act(async () => pending[0]!(page('Previous session')));
  await screen.findByText('Previous session');
  authState = { status: 'authenticated', issuer: 'issuer', subject: 'principal' };
  view.rerender();
  expect(screen.queryByText('Previous session')).toBeNull();
  await waitFor(() => expect(pending).toHaveLength(2));
});

it('disposes field and note drafts and delete confirmation when navigating to another contact', async () => {
  tenant = a;
  history.replaceState(null, '', '/app/contacts/' + a);
  let writes = 0;
  vi.stubGlobal('fetch', async (req: Request) => {
    const path = new URL(req.url).pathname;
    if (req.method !== 'GET') writes++;
    if (path.endsWith('/custom-fields')) return Response.json({ items: [{ id: '20000000-0000-4000-8000-000000000003', key: 'amount', displayName: 'Amount', dataType: 'number', required: false, version: 1 }], nextCursor: null });
    if (path.includes('/fields/')) return Response.json({ value: 1 });
    if (path.endsWith(a) || path.endsWith(b)) return Response.json({ id: path.split('/').at(-1), displayName: path.endsWith(a) ? 'First' : 'Second', version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' }, { headers: { ETag: '"v1"' } });
    return Response.json({ items: [], nextCursor: null });
  });
  const view = renderShell();
  await screen.findByRole('heading', { name: 'First' });
  fireEvent.change(await screen.findByLabelText('Amount'), { target: { value: '42' } });
  fireEvent.change(screen.getByLabelText('Ghi chú'), { target: { value: 'Private note' } });
  fireEvent.click(screen.getByRole('button', { name: 'Xóa liên hệ' }));
  expect(screen.getByRole('dialog')).toBeTruthy();
  await act(async () => { await view.router.navigate({ to: '/app/contacts/$contactId', params: { contactId: b } }); });
  await screen.findByRole('heading', { name: 'Second' });
  expect(screen.queryByRole('dialog')).toBeNull();
  expect((screen.getByLabelText('Ghi chú') as HTMLTextAreaElement).value).toBe('');
  await waitFor(() => expect((screen.getByLabelText('Amount') as HTMLInputElement).value).toBe('1'));
  expect(writes).toBe(0);
});

it('does not navigate away from the new contact when an old delete returns after the route changes', async () => {
  tenant = a;
  history.replaceState(null, '', '/app/contacts/' + a);
  let finishDelete: ((response: Response) => void) | undefined;
  let writes = 0;
  vi.stubGlobal('fetch', async (req: Request) => {
    const path = new URL(req.url).pathname;
    if (req.method === 'DELETE') { writes++; return new Promise<Response>(resolve => { finishDelete = resolve; }); }
    if (path.endsWith(a) || path.endsWith(b)) return Response.json({ id: path.split('/').at(-1), displayName: path.endsWith(a) ? 'First' : 'Second', version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' }, { headers: { ETag: '"v1"' } });
    return Response.json({ items: [], nextCursor: null });
  });
  const view = renderShell();
  await screen.findByRole('heading', { name: 'First' });
  fireEvent.click(screen.getByRole('button', { name: 'Xóa liên hệ' }));
  fireEvent.click(screen.getByRole('button', { name: 'Xác nhận xóa' }));
  await waitFor(() => expect(finishDelete).toBeDefined());
  await act(async () => { await view.router.navigate({ to: '/app/contacts/$contactId', params: { contactId: b } }); });
  await screen.findByRole('heading', { name: 'Second' });
  await act(async () => finishDelete!(new Response(null, { status: 204 })));
  expect(location.pathname).toBe('/app/contacts/' + b);
  expect(screen.getByRole('heading', { name: 'Second' })).toBeTruthy();
  expect(writes).toBe(1);
});

it('reads a conflicted record again after leaving and returning without resending its write', async () => {
  tenant = a;
  history.replaceState(null, '', '/app/contacts/' + a);
  let reads = 0, writes = 0;
  vi.stubGlobal('fetch', async (req: Request) => {
    const path = new URL(req.url).pathname;
    if (req.method === 'PATCH') { writes++; return new Response(null, { status: 412 }); }
    if (path.endsWith(a)) { reads++; return Response.json({ id: a, displayName: 'First', version: reads, createdAt: '2026-01-01', updatedAt: '2026-01-01' }, { headers: { ETag: '"v' + reads + '"' } }); }
    return Response.json({ items: [], nextCursor: null });
  });
  const view = renderShell();
  await screen.findByRole('heading', { name: 'First' });
  fireEvent.click(screen.getByRole('button', { name: 'Chỉnh sửa' }));
  fireEvent.change(screen.getByLabelText('Tên liên hệ'), { target: { value: 'Draft' } });
  fireEvent.click(screen.getByRole('button', { name: 'Lưu' }));
  await screen.findByRole('alert');
  expect((screen.getByLabelText('Tên liên hệ') as HTMLInputElement).value).toBe('Draft');
  await act(async () => { await view.router.navigate({ to: '/app/contacts' }); });
  await screen.findByText('Chưa có liên hệ.');
  await act(async () => { await view.router.navigate({ to: '/app/contacts/$contactId', params: { contactId: a } }); });
  await screen.findByRole('heading', { name: 'First' });
  expect(reads).toBe(2);
  expect(writes).toBe(1);
  expect(screen.queryByDisplayValue('Draft')).toBeNull();
});

it('does not open a late created contact after the user navigates to a different record', async () => {
  tenant = a; history.replaceState(null, '', '/app/contacts');
  let finishCreate: ((response: Response) => void) | undefined;
  let writes = 0;
  vi.stubGlobal('fetch', async (req: Request) => {
    const path = new URL(req.url).pathname;
    if (req.method === 'POST') { writes++; return new Promise<Response>(resolve => { finishCreate = resolve; }); }
    if (path.endsWith(b)) return Response.json({ id: b, displayName: 'Selected', version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' }, { headers: { ETag: '"v1"' } });
    return Response.json({ items: [], nextCursor: null });
  });
  const view = renderShell();
  fireEvent.click(await screen.findByRole('button', { name: 'Thêm liên hệ' }));
  fireEvent.change(screen.getByLabelText('Tên liên hệ'), { target: { value: 'Pending creation' } });
  fireEvent.click(screen.getByRole('button', { name: 'Tạo' }));
  await waitFor(() => expect(finishCreate).toBeDefined());
  await act(async () => { await view.router.navigate({ to: '/app/contacts/$contactId', params: { contactId: b } }); });
  await screen.findByRole('heading', { name: 'Selected' });
  await act(async () => finishCreate!(Response.json({ id: a, displayName: 'Pending creation', version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' }, { status: 201, headers: { ETag: '"v1"' } })));
  expect(location.pathname).toBe('/app/contacts/' + b);
  expect(screen.getByRole('heading', { name: 'Selected' })).toBeTruthy();
  expect(writes).toBe(1);
});

it.each([403, 404])('shows a safe error after navigating to a forbidden or deleted record (%i)', async status => {
  tenant = a; history.replaceState(null, '', '/app/contacts/' + a);
  vi.stubGlobal('fetch', async (req: Request) => {
    const path = new URL(req.url).pathname;
    if (path.endsWith(b)) return Response.json({ reasonToken: status === 403 ? 'TENANT_FORBIDDEN' : 'NOT_FOUND' }, { status });
    if (path.endsWith(a)) return Response.json({ id: a, displayName: 'Visible', version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' }, { headers: { ETag: '"v1"' } });
    return Response.json({ items: [], nextCursor: null });
  });
  const view = renderShell(); await screen.findByRole('heading', { name: 'Visible' });
  await act(async () => { await view.router.navigate({ to: '/app/contacts/$contactId', params: { contactId: b } }); });
  await screen.findByRole('alert');
  expect(screen.queryByText('Visible')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Chỉnh sửa' })).toBeNull();
});

it('does not let a previous tenant mutation redirect after A to B to A', async () => {
  tenant = a; history.replaceState(null, '', '/app/contacts/' + a);
  let finishDelete: ((response: Response) => void) | undefined;
  let reads = 0, writes = 0;
  vi.stubGlobal('fetch', async (req: Request) => {
    const path = new URL(req.url).pathname;
    if (req.method === 'DELETE') { writes++; return new Promise<Response>(resolve => { finishDelete = resolve; }); }
    if (path.endsWith(a)) return Response.json({ id: a, displayName: 'View ' + ++reads, version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' }, { headers: { ETag: '"v1"' } });
    return Response.json({ items: [], nextCursor: null });
  });
  const view = renderShell(); await screen.findByRole('heading', { name: 'View 1' });
  fireEvent.click(screen.getByRole('button', { name: 'Xóa liên hệ' }));
  fireEvent.click(screen.getByRole('button', { name: 'Xác nhận xóa' }));
  await waitFor(() => expect(finishDelete).toBeDefined());
  tenant = b; view.rerender(); await screen.findByRole('heading', { name: 'View 2' });
  tenant = a; view.rerender(); await screen.findByRole('heading', { name: 'View 3' });
  await act(async () => finishDelete!(new Response(null, { status: 204 })));
  expect(location.pathname).toBe('/app/contacts/' + a);
  expect(screen.getByRole('heading', { name: 'View 3' })).toBeTruthy();
  expect(writes).toBe(1);
});

it('does not reuse the list after an uncertain write and back navigation', async () => {
  tenant = a; history.replaceState(null, '', '/app/contacts');
  const record = { id: a, displayName: 'Old list', version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
  let lists = 0, writes = 0, finishList: ((response: Response) => void) | undefined;
  vi.stubGlobal('fetch', async (req: Request) => {
    const path = new URL(req.url).pathname;
    if (path.endsWith('/contacts')) {
      if (++lists === 1) return Response.json({ items: [record], nextCursor: null });
      return new Promise<Response>(resolve => { finishList = resolve; });
    }
    if (req.method === 'PATCH') { writes++; throw new TypeError('connection lost'); }
    if (path.endsWith(a)) return Response.json(record, { headers: { ETag: '"v1"' } });
    return Response.json({ items: [], nextCursor: null });
  });
  renderShell(); fireEvent.click(await screen.findByRole('link', { name: 'Old list' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Chỉnh sửa' }));
  fireEvent.change(screen.getByLabelText('Tên liên hệ'), { target: { value: 'Draft' } });
  fireEvent.click(screen.getByRole('button', { name: 'Lưu' })); await screen.findByRole('alert');
  act(() => history.back()); await waitFor(() => expect(finishList).toBeDefined());
  expect(screen.queryByText('Old list')).toBeNull();
  await act(async () => finishList!(Response.json({ items: [], nextCursor: null })));
  await screen.findByText('Chưa có liên hệ.');
  expect(writes).toBe(1);
});
