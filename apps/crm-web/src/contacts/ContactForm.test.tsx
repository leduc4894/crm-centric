// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { ContactForm } from './ContactForm';
import type { CrmClient } from './crmClient';
import { renderContactRoute } from './testRouter';
const id = '10000000-0000-4000-8000-000000000001';
let tenant: string | null = id;
const authState = { status: 'authenticated', issuer: 'i', subject: 's' };
const auth = { getAccessToken: () => 'test', getSnapshot: () => ({ status: 'authenticated', issuer: 'i', subject: 's' }) };
vi.mock('../tenant/TenantProvider', () => ({ useTenant: () => ({ identity: '["i","s"]', selectedTenantId: tenant }) }));
vi.mock('../auth/AuthProvider', () => ({ useAuthClient: () => auth, useAuth: () => ({ state: authState }) }));
const record = { id, displayName: 'Original', email: 'demo@example.test', version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
function setup(handler: (req: Request) => Response, detail = true) {
  history.replaceState(null, '', '/app/contacts' + (detail ? '/' + id : ''));
  const calls: Request[] = [];
  vi.stubGlobal('fetch', (req: Request) => { calls.push(req); return Promise.resolve(handler(req)); });
  const view = renderContactRoute();
  return { calls, view };
}
const response = (name = 'Original', tag = '"v1"', status = 200) => Response.json({ ...record, displayName: name }, { status, headers: { ETag: tag } });
const click = async (name: string) => fireEvent.click(await screen.findByRole('button', { name }));
const change = async (label: string, value: string) => fireEvent.change(await screen.findByLabelText(label), { target: { value } });
afterEach(() => { cleanup(); tenant = id; vi.unstubAllGlobals(); });
it('keeps edits on 412, shows refreshed data before deliberate save, and confirms delete', async () => {
  let gets = 0, patches = 0;
  const { calls } = setup(req => {
    if (req.method === 'PATCH') return ++patches === 1 ? new Response(null, { status: 412 }) : response('Local', '"v3"');
    if (req.method === 'DELETE') return new Response(null, { status: 204 });
    return new URL(req.url).pathname.endsWith(id) ? ++gets === 1 ? response() : response('Remote', '"v2"') : Response.json({ items: [], nextCursor: null });
  });
  await click('Chỉnh sửa'); await change('Tên liên hệ', 'Local'); await click('Lưu');
  expect((await screen.findByRole('alert')).textContent).toBeTruthy();
  expect(calls.at(-1)!.headers.get('If-Match')).toBe('"v1"');
  await click('Tải lại dữ liệu'); await screen.findByText('Remote');
  expect((screen.getByLabelText('Tên liên hệ') as HTMLInputElement).value).toBe('Local');
  expect(patches).toBe(1);
  await click('Lưu'); await screen.findByRole('heading', { name: 'Local' });
  expect(calls.at(-1)!.headers.get('If-Match')).toBe('"v2"');
  expect(await calls.at(-1)!.clone().json()).toEqual({ displayName: 'Local' });
  await click('Xóa liên hệ'); expect(screen.getByRole('dialog')).toBeTruthy();
  expect(calls.filter(req => req.method === 'DELETE')).toHaveLength(0);
  await click('Xác nhận xóa'); await screen.findByText('Chưa có liên hệ.');
  expect(calls.find(req => req.method === 'DELETE')!.headers.get('If-Match')).toBe('"v3"');
});
it.each([201, 428])('opens created record or requires explicit existing ID selection after %i', async status => {
  let posts = 0;
  const { calls } = setup(req => {
    if (req.method === 'POST') return ++posts === 1 && status === 428 ? new Response(null, { status }) : response('Saved', '"v2"', posts === 1 ? 201 : 200);
    return new URL(req.url).pathname.endsWith(id) ? response(posts > 0 && (posts === 2 || status === 201) ? 'Saved' : 'Original') : Response.json({ items: [record], nextCursor: null });
  }, false);
  await click('Thêm liên hệ'); await change('Tên liên hệ', 'Draft'); await change('Email', record.email); await click('Tạo');
  if (status === 428) {
    await screen.findByRole('alert');
    expect(calls.filter(req => new URL(req.url).pathname.endsWith(id))).toHaveLength(0);
    await click('Chọn Original'); await click('Cập nhật Original');
    await waitFor(() => expect(calls.filter(req => req.method === 'POST')).toHaveLength(2));
    const selected = calls.filter(req => req.method === 'POST')[1]!;
    expect(selected.headers.get('If-Match')).toBe('"v1"'); expect(selected.headers.get('X-Contact-Id')).toBe(id);
  }
  await screen.findByRole('heading', { name: 'Saved' });
});
it.each([403, 0])('handles a forbidden or uncertain write without retries and clears scoped drafts (%i)', async status => {
  const { calls, view } = setup(req => {
    if (req.method === 'PATCH') {
      if (!status) throw new Error('private@example.test');
      return Response.json({ detail: 'private@example.test' }, { status });
    }
    return new URL(req.url).pathname.endsWith(id) ? response() : Response.json({ items: [], nextCursor: null });
  });
  await click('Chỉnh sửa'); await change('Tên liên hệ', 'Private draft'); await click('Lưu');
  const alert = await screen.findByRole('alert');
  expect(alert.textContent).not.toContain('private@example.test');
  expect(alert.textContent).toContain(status ? 'Chỉ đọc' : 'Chưa xác định');
  expect(calls.filter(req => req.method === 'PATCH')).toHaveLength(1);
  tenant = null; view.rerender();
  await waitFor(() => expect(screen.queryByDisplayValue('Private draft')).toBeNull());
});
it('table_keeps_create_candidates_and_conflict_review', async () => {
  let posts = 0, gets = 0;
  const other = '10000000-0000-4000-8000-000000000002';
  const { calls, view } = setup(req => {
    if (req.method === 'POST') return new Response(null, { status: ++posts === 1 ? 428 : 412 });
    const url = new URL(req.url);
    if (url.pathname.endsWith(id)) return new Response(null, { status: 404 });
    if (url.pathname.endsWith(other)) {
      gets++;
      return Response.json({ ...record, id: other, displayName: gets === 1 ? 'Reviewed Other' : 'Latest Other' }, {
        headers: { ETag: gets === 1 ? '"v27"' : '"v28"' },
      });
    }
    const items = url.searchParams.has('q')
      ? [record, { ...record, id: other, displayName: 'Other' }]
      : [{ ...record, id: '10000000-0000-4000-8000-000000000003', displayName: 'Excluded' }];
    return Response.json({ items, nextCursor: null });
  }, false);
  await within(await screen.findByRole('table')).findByRole('link', { name: 'Excluded' });
  await change('Tìm kiếm', 'active'); await click('Lọc');
  await screen.findByRole('link', { name: 'Original' });
  expect(within(screen.getByRole('table')).getAllByRole('link').map(link => link.textContent)).toEqual(['Original', 'Other']);
  await click('Thêm liên hệ'); await change('Tên liên hệ', 'Draft'); await change('Email', record.email); await click('Tạo');
  await screen.findByRole('button', { name: 'Chọn Original' });
  expect(screen.queryByRole('button', { name: 'Chọn Excluded' })).toBeNull();
  await click('Chọn Other');
  await screen.findByText('Reviewed Other', { selector: 'dd' });
  expect(posts).toBe(1);
  await click('Cập nhật Reviewed Other');
  await screen.findByRole('alert');
  const selected = calls.filter(req => req.method === 'POST')[1]!;
  expect(selected.headers.get('X-Contact-Id')).toBe(other);
  expect(selected.headers.get('If-Match')).toBe('"v27"');
  await click('Tải lại dữ liệu'); await screen.findByText('Latest Other', { selector: 'dd' });
  expect((screen.getByLabelText('Tên liên hệ') as HTMLInputElement).value).toBe('Draft');
  await click('Chọn Original'); await screen.findByRole('alert');
  expect(screen.queryByRole('button', { name: 'Cập nhật Latest Other' })).toBeNull();
  tenant = other; view.rerender();
  expect(screen.queryByDisplayValue('Draft')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Chọn Other' })).toBeNull();
  expect(posts).toBe(2);
});
it('shows uncertain outcome after a real client deadline and waits for an explicit read', async () => {
  let gets = 0, writes = 0;
  history.replaceState(null, '', '/app/contacts/' + id);
  vi.stubGlobal('fetch', (req: Request) => {
    if (req.method === 'GET') {
      if (!new URL(req.url).pathname.endsWith(id)) return Promise.resolve(Response.json({ items: [], nextCursor: null }));
      gets++; return Promise.resolve(response());
    }
    writes++;
    return new Promise<Response>((_, reject) => req.signal.addEventListener('abort', () => reject(req.signal.reason)));
  });
  renderContactRoute();
  await click('Chỉnh sửa'); await change('Tên liên hệ', 'Draft');
  vi.useFakeTimers();
  try {
    fireEvent.click(screen.getByRole('button', { name: 'Lưu' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(10001); });
    expect(screen.getByRole('alert').textContent).toContain('WEB_CRM_TIMEOUT');
    expect(writes).toBe(1); expect(gets).toBe(1);
  } finally { vi.useRealTimers(); }
  await click('Tải lại dữ liệu'); await screen.findByText('Dữ liệu hiện tại');
  expect(gets).toBe(2); expect(writes).toBe(1);
});
it.each(['POST', 'PATCH'])('freezes the submitted draft while %s is pending', async method => {
  const editing = method === 'PATCH';
  history.replaceState(null, '', '/app/contacts' + (editing ? '/' + id : ''));
  let finish: ((response: Response) => void) | undefined;
  vi.stubGlobal('fetch', (req: Request) => {
    if (req.method === method) return new Promise<Response>(resolve => { finish = resolve; });
    return Promise.resolve(new URL(req.url).pathname.endsWith(id) ? response(finish ? 'Submitted' : 'Original') : Response.json({ items: [], nextCursor: null }));
  });
  renderContactRoute();
  await click(editing ? 'Chỉnh sửa' : 'Thêm liên hệ');
  await change('Tên liên hệ', 'Submitted');
  await click(editing ? 'Lưu' : 'Tạo');
  await waitFor(() => expect(finish).toBeDefined());
  for (const label of ['Tên liên hệ', 'Email', 'Điện thoại']) {
    expect(screen.getByLabelText(label).matches(':disabled')).toBe(true);
  }
  await act(async () => finish!(response('Submitted', '"v2"', editing ? 200 : 201)));
  await screen.findByRole('heading', { name: 'Submitted' });
});
it('keeps ambiguous create blocked when a filter supersedes its reconciliation read', async () => {
  history.replaceState(null, '', '/app/contacts');
  let reads = 0, writes = 0;
  let finishRead: ((response: Response) => void) | undefined;
  const page = (displayName: string) => Response.json({ items: [{ ...record, displayName }], nextCursor: null });
  vi.stubGlobal('fetch', (req: Request) => {
    if (req.method === 'POST') {
      writes++;
      return new Promise<Response>((_, reject) => req.signal.addEventListener('abort', () => reject(req.signal.reason)));
    }
    if (++reads === 2) return new Promise<Response>(resolve => { finishRead = resolve; });
    return Promise.resolve(page(reads === 1 ? 'Initial contact' : 'Filtered result'));
  });
  renderContactRoute();
  await screen.findByRole('link', { name: 'Initial contact' });
  await click('Thêm liên hệ'); await change('Tên liên hệ', 'No email');
  vi.useFakeTimers();
  try {
    fireEvent.click(screen.getByRole('button', { name: 'Tạo' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(10001); });
  } finally { vi.useRealTimers(); }
  await click('Tải lại dữ liệu');
  await waitFor(() => expect(finishRead).toBeDefined());
  await change('Tìm kiếm', 'filter'); await click('Lọc');
  await screen.findByRole('link', { name: 'Filtered result' });
  await act(async () => finishRead!(page('Canceled response')));
  expect(within(screen.getByRole('table')).getAllByRole('link').map(link => link.textContent)).toEqual(['Filtered result']);
  const submit = screen.getByRole('button', { name: 'Tạo' });
  expect(submit.matches(':disabled')).toBe(true);
  expect(screen.getByRole('alert').textContent).toContain('WEB_CRM_READ_CANCELLED');
  await click('Tải lại dữ liệu');
  await waitFor(() => expect(submit.matches(':disabled')).toBe(false));
  expect(reads).toBe(4); expect(writes).toBe(1);
});
it('reconciles ambiguous create with a new read even while the initial list read is pending', async () => {
  history.replaceState(null, '', '/app/contacts');
  const reads: Array<(response: Response) => void> = [];
  let writes = 0;
  const page = () => Response.json({ items: [], nextCursor: null });
  vi.stubGlobal('fetch', (req: Request) => {
    if (req.method === 'POST') { writes++; return Promise.resolve(new Response(null, { status: 503 })); }
    return new Promise<Response>(resolve => reads.push(resolve));
  });
  renderContactRoute();
  await waitFor(() => expect(reads).toHaveLength(1));
  await click('Thêm liên hệ'); await change('Tên liên hệ', 'Pending'); await click('Tạo');
  await screen.findByRole('alert');
  await click('Tải lại dữ liệu');
  await waitFor(() => expect(reads).toHaveLength(2));
  expect(screen.getByRole('button', { name: 'Tạo' }).matches(':disabled')).toBe(true);
  await act(async () => reads[1]!(page()));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Tạo' }).matches(':disabled')).toBe(false));
  await act(async () => reads[0]!(page()));
  expect(writes).toBe(1);
});
it('preserves partial patch and explicit null, and does not send a no-op', async () => {
  const { calls } = setup(req => req.method === 'PATCH' ? response('Original', '"v2"') :
    new URL(req.url).pathname.endsWith(id) ? response() : Response.json({ items: [], nextCursor: null }));
  await click('Chỉnh sửa');
  expect(screen.getByRole('button', { name: 'Lưu' }).matches(':disabled')).toBe(true);
  await change('Email', ''); await click('Lưu');
  await screen.findByRole('heading', { name: 'Original' });
  const writes = calls.filter(req => req.method === 'PATCH');
  expect(writes).toHaveLength(1);
  expect(await writes[0]!.clone().json()).toEqual({ email: null });
});
it('validates display name length by Unicode scalars', async () => {
  const { calls } = setup(req => req.method === 'POST' ? response('Saved', '"v1"', 201) :
    new URL(req.url).pathname.endsWith(id) ? response('Saved') : Response.json({ items: [], nextCursor: null }), false);
  await click('Thêm liên hệ');
  await change('Tên liên hệ', '😀'.repeat(201));
  await click('Tạo');
  expect(calls.filter(req => req.method === 'POST')).toHaveLength(0);
  expect(await screen.findByRole('alert')).toBeTruthy();
  await change('Tên liên hệ', '😀'.repeat(200));
  await click('Tạo');
  await screen.findByRole('heading', { name: 'Saved' });
  expect(calls.filter(req => req.method === 'POST')).toHaveLength(1);
});
it('keeps the reviewed version and original diff when the record prop refreshes', async () => {
  const patch = vi.fn().mockResolvedValue({ data: record, etag: '"v3"' });
  const client = { patchContact: patch } as unknown as CrmClient;
  const props = { client, readOnly: false, onSaved: vi.fn(), onCancel: vi.fn(), onForbidden: vi.fn() };
  const view = render(<ContactForm {...props} record={{ data: record, etag: '"v1"', status: 200 }} />);
  await change('Tên liên hệ', 'Draft');
  view.rerender(<ContactForm {...props} record={{ data: { ...record, displayName: 'Server refresh', email: 'new@example.test' }, etag: '"v2"', status: 200 }} />);
  await click('Lưu');
  await waitFor(() => expect(patch).toHaveBeenCalledTimes(1));
  expect(patch).toHaveBeenCalledWith(id, { displayName: 'Draft' }, '"v1"');
});
it('submits only once for simultaneous submit events', async () => {
  history.replaceState(null, '', '/app/contacts');
  let writes = 0;
  vi.stubGlobal('fetch', (req: Request) => {
    if (req.method === 'POST') { writes++; return new Promise<Response>(() => {}); }
    return Promise.resolve(Response.json({ items: [], nextCursor: null }));
  });
  renderContactRoute();
  await click('Thêm liên hệ'); await change('Tên liên hệ', 'Draft');
  const form = screen.getByRole('button', { name: 'Tạo' }).closest('form')!;
  fireEvent.submit(form); fireEvent.submit(form);
  await waitFor(() => expect(writes).toBe(1));
  expect(screen.getByLabelText('Tên liên hệ').matches(':disabled')).toBe(true);
});
it('retains a created draft until a missing ETag is read and explicitly reviewed', async () => {
  const saved = { data: { ...record, displayName: 'Created' }, etag: null, status: 201 };
  const fresh = { ...saved, etag: '"v1"', status: 200 };
  const client = { createContact: vi.fn().mockResolvedValue(saved), getContact: vi.fn().mockResolvedValue(fresh) } as unknown as CrmClient;
  const onSaved = vi.fn();
  render(<ContactForm client={client} readOnly={false} onSaved={onSaved} onCancel={vi.fn()} onForbidden={vi.fn()} />);
  await change('Tên liên hệ', 'Draft'); await click('Tạo');
  await screen.findByRole('alert');
  expect(onSaved).not.toHaveBeenCalled();
  expect((screen.getByLabelText('Tên liên hệ') as HTMLInputElement).value).toBe('Draft');
  expect(screen.getByRole('button', { name: 'Tạo' }).matches(':disabled')).toBe(true);
  await click('Tải lại dữ liệu');
  await screen.findByText('Created', { selector: 'dd' });
  expect(client.getContact).toHaveBeenCalledWith(id);
  expect(onSaved).not.toHaveBeenCalled();
  expect(client.createContact).toHaveBeenCalledTimes(1);
  await click('Xác nhận dữ liệu');
  expect(onSaved).toHaveBeenCalledWith(fresh);
  expect(client.createContact).toHaveBeenCalledTimes(1);
});
it('rejects a successful write response for a different contact and reconciles the selected ID', async () => {
  const other = '10000000-0000-4000-8000-000000000002';
  const expected = { data: record, etag: '"v2"', status: 200 };
  const client = { patchContact: vi.fn().mockResolvedValue({ data: { ...record, id: other }, etag: '"v2"', status: 200 }), getContact: vi.fn().mockResolvedValue(expected) } as unknown as CrmClient;
  const onSaved = vi.fn(); const refreshAttributes = vi.fn().mockResolvedValue(undefined);
  render(<ContactForm client={client} record={{ data: record, etag: '"v1"', status: 200 }} readOnly={false} refreshAttributes={refreshAttributes} onSaved={onSaved} onCancel={vi.fn()} onForbidden={vi.fn()} />);
  await change('Tên liên hệ', 'Draft'); await click('Lưu'); await screen.findByRole('alert');
  expect(onSaved).not.toHaveBeenCalled();
  await click('Tải lại dữ liệu'); await screen.findByText('Dữ liệu hiện tại');
  expect(client.getContact).toHaveBeenCalledWith(id);
  expect(refreshAttributes).toHaveBeenCalledOnce();
  expect(onSaved).not.toHaveBeenCalled();
  await click('Xác nhận dữ liệu'); expect(onSaved).toHaveBeenCalledWith(expected);
  expect(client.patchContact).toHaveBeenCalledOnce();
});
it('distinguishes same-name candidates and uses only the explicitly chosen ID and version', async () => {
  const other = '10000000-0000-4000-8000-000000000002';
  let posts = 0;
  const { calls } = setup(req => {
    if (req.method === 'POST') return ++posts === 1 ? new Response(null, { status: 428 }) : response('Saved', '"v3"');
    const path = new URL(req.url).pathname;
    if (path.endsWith(other)) return Response.json({ ...record, id: other }, { headers: { ETag: '"v2"' } });
    if (path.endsWith(id)) return response();
    return Response.json({ items: [record, { ...record, id: other }], nextCursor: null });
  }, false);
  await screen.findAllByRole('link', { name: 'Original' });
  await click('Thêm liên hệ'); await change('Tên liên hệ', 'Draft'); await change('Email', record.email); await click('Tạo');
  await click(`Chọn Original — ${other}`); await screen.findByText('Dữ liệu hiện tại'); await click('Cập nhật Original');
  await waitFor(() => expect(posts).toBe(2));
  const selected = calls.filter(req => req.method === 'POST')[1]!;
  expect(selected.headers.get('X-Contact-Id')).toBe(other);
  expect(selected.headers.get('If-Match')).toBe('"v2"');
});
it('keeps a missing-ETag PATCH locked when a sibling refresh fails', async () => {
  const snapshot = { data: record, etag: '"v1"', status: 200 };
  const client = { patchContact: vi.fn().mockResolvedValue({ ...snapshot, etag: null }), getContact: vi.fn().mockResolvedValue(snapshot) } as unknown as CrmClient;
  const refreshAttributes = vi.fn().mockRejectedValue(new Error('failed'));
  const onSaved = vi.fn();
  render(<ContactForm client={client} record={snapshot} readOnly={false} refreshAttributes={refreshAttributes} onSaved={onSaved} onCancel={vi.fn()} onForbidden={vi.fn()} />);
  await change('Tên liên hệ', 'Draft'); await click('Lưu'); await screen.findByRole('alert');
  await click('Tải lại dữ liệu'); await waitFor(() => expect(refreshAttributes).toHaveBeenCalledOnce());
  expect(screen.getByRole('button', { name: 'Lưu' }).matches(':disabled')).toBe(true);
  expect(screen.queryByRole('button', { name: 'Xác nhận dữ liệu' })).toBeNull();
  expect((screen.getByLabelText('Tên liên hệ') as HTMLInputElement).value).toBe('Draft');
  expect(onSaved).not.toHaveBeenCalled(); expect(client.patchContact).toHaveBeenCalledOnce();
});
it('reconciles the selected ID when a conditional POST returns a different ID', async () => {
  const other = '10000000-0000-4000-8000-000000000002';
  const snapshot = { data: record, etag: '"v1"', status: 200 };
  const client = { createContact: vi.fn().mockRejectedValueOnce(new (await import('./crmClient')).CrmError('CONFLICT', 428)).mockResolvedValueOnce({ data: { ...record, id: other }, etag: '"v2"', status: 200 }), getContact: vi.fn().mockResolvedValue(snapshot) } as unknown as CrmClient;
  const onSaved = vi.fn();
  render(<ContactForm client={client} choices={[record]} readOnly={false} onSaved={onSaved} onCancel={vi.fn()} onForbidden={vi.fn()} />);
  await change('Tên liên hệ', 'Draft'); await click('Tạo'); await screen.findByRole('alert');
  await click('Chọn Original'); await screen.findByText('Dữ liệu hiện tại'); await click('Cập nhật Original');
  await screen.findByRole('alert');
  expect(onSaved).not.toHaveBeenCalled();
  await click('Tải lại dữ liệu'); await screen.findByRole('button', { name: 'Xác nhận dữ liệu' });
  expect(client.getContact).toHaveBeenCalledTimes(2);
  expect(client.getContact).toHaveBeenLastCalledWith(id);
  expect(client.createContact).toHaveBeenCalledTimes(2);
});
