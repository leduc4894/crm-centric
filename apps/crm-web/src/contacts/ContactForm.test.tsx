// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { ContactShell } from './ContactShell';
const id = '10000000-0000-4000-8000-000000000001';
let tenant: string | null = id;
const auth = { getAccessToken: () => 'test', getSnapshot: () => ({ status: 'authenticated', issuer: 'i', subject: 's' }) };
vi.mock('../tenant/TenantProvider', () => ({ useTenant: () => ({ identity: '["i","s"]', selectedTenantId: tenant }) }));
vi.mock('../auth/AuthProvider', () => ({ useAuthClient: () => auth }));
const record = { id, displayName: 'Original', email: 'demo@example.test', version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
function setup(handler: (req: Request) => Response, detail = true) {
  history.replaceState(null, '', '/app/contacts' + (detail ? '/' + id : ''));
  const calls: Request[] = [];
  vi.stubGlobal('fetch', (req: Request) => { calls.push(req); return Promise.resolve(handler(req)); });
  const view = render(<ContactShell />);
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
    return response();
  });
  await click('Chỉnh sửa'); await change('Tên liên hệ', 'Private draft'); await click('Lưu');
  const alert = await screen.findByRole('alert');
  expect(alert.textContent).not.toContain('private@example.test');
  expect(alert.textContent).toContain(status ? 'Chỉ đọc' : 'Chưa xác định');
  expect(calls.filter(req => req.method === 'PATCH')).toHaveLength(1);
  tenant = null; view.rerender(<ContactShell />);
  await waitFor(() => expect(screen.queryByDisplayValue('Private draft')).toBeNull());
});
it('blocks stale upsert after email reuse, preserves the draft, and invalidates a selection whose reload fails', async () => {
  let posts = 0, gets = 0;
  const other = '10000000-0000-4000-8000-000000000002';
  const { calls } = setup(req => {
    if (req.method === 'POST') return new Response(null, { status: ++posts === 1 ? 428 : 412 });
    if (new URL(req.url).pathname.endsWith(other)) return new Response(null, { status: 404 });
    if (new URL(req.url).pathname.endsWith(id)) return response(++gets === 1 ? 'Original' : 'Latest', gets === 1 ? '"v1"' : '"v2"');
    return Response.json({ items: [record, { ...record, id: other, displayName: 'Other' }], nextCursor: null });
  }, false);
  await click('Thêm liên hệ'); await change('Tên liên hệ', 'Draft'); await change('Email', record.email); await click('Tạo');
  await click('Chọn Original'); await click('Cập nhật Original');
  await click('Tải lại dữ liệu'); await screen.findByText('Latest');
  expect((screen.getByLabelText('Tên liên hệ') as HTMLInputElement).value).toBe('Draft');
  await click('Chọn Other'); await screen.findByRole('alert');
  expect(screen.queryByRole('button', { name: 'Cập nhật Latest' })).toBeNull();
  expect(calls.filter(req => req.method === 'POST')).toHaveLength(2);
});
it('shows uncertain outcome after a real client deadline and waits for an explicit read', async () => {
  let gets = 0, writes = 0;
  history.replaceState(null, '', '/app/contacts/' + id);
  vi.stubGlobal('fetch', (req: Request) => {
    if (req.method === 'GET') { gets++; return Promise.resolve(response()); }
    writes++;
    return new Promise<Response>((_, reject) => req.signal.addEventListener('abort', () => reject(req.signal.reason)));
  });
  render(<ContactShell />);
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
