// @vitest-environment jsdom
import { act, cleanup, fireEvent, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { setup, field, itemId, entry, page, record, click, change, id } from './recordTestSupport';
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
it('requires explicit successful review when a saved note has no ETag', async () => {
  let writes = 0, reads = 0, failSibling = true;
  const calls = setup((_req, path) => {
    if (path.endsWith('/custom-fields')) return page([field]);
    if (path.endsWith('/fields/' + itemId)) return writes && failSibling ? new Response(null, { status: 503 }) : Response.json({ value: 1 });
    if (path.endsWith('/notes')) { writes++; return Response.json(entry, { status: 201 }); }
    if (path.endsWith(id)) {
      reads++;
      if (reads === 2) return Response.json({ ...entry, id, displayName: 'Current', version: 2, createdAt: '2026-01-01', updatedAt: '2026-01-01' });
      return reads > 2 ? Response.json({ id, displayName: 'Current', version: 2, createdAt: '2026-01-01', updatedAt: '2026-01-01' }, { headers: { ETag: '"v2"' } }) : record();
    }
  });
  await screen.findByDisplayValue('1'); await change('Ghi chú', 'Saved'); await click('Thêm ghi chú');
  await screen.findByRole('button', { name: 'Đối chiếu dữ liệu' });
  expect(screen.getByRole('button', { name: 'Thêm ghi chú' }).matches(':disabled')).toBe(true);
  await click('Đối chiếu dữ liệu');
  await waitFor(() => expect(screen.getAllByRole('alert').some(alert => alert.textContent?.includes('WEB_CRM_CONTRACT_INVALID'))).toBe(true));
  expect(screen.getByRole('button', { name: 'Thêm ghi chú' }).matches(':disabled')).toBe(true);
  await click('Đối chiếu dữ liệu'); await screen.findByRole('button', { name: 'Tải lại giá trị' });
  expect(screen.getByRole('button', { name: 'Thêm ghi chú' }).matches(':disabled')).toBe(true);
  failSibling = false; await click('Đối chiếu dữ liệu');
  await screen.findByRole('heading', { name: 'Current' });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Chỉnh sửa' }).matches(':disabled')).toBe(false));
  expect(calls.filter(req => req.method === 'POST')).toHaveLength(1);
});
it('does not attach the note ETag to an old contact representation', async () => {
  let wrote = false;
  const calls = setup((req, path) => {
    if (path.endsWith('/notes')) { wrote = true; return Response.json(entry, { status: 201, headers: { ETag: '"v2"' } }); }
    if (path.endsWith(id) && req.method === 'GET') return wrote ? Response.json({ id, displayName: 'Changed elsewhere', version: 3, createdAt: '2026-01-01', updatedAt: '2026-01-01' }, { headers: { ETag: '"v3"' } }) : record();
  });
  await screen.findByText('Chưa có hoạt động.'); await change('Ghi chú', 'Saved'); await click('Thêm ghi chú');
  await screen.findByRole('button', { name: 'Đối chiếu dữ liệu' });
  expect(screen.getByRole('button', { name: 'Chỉnh sửa' }).matches(':disabled')).toBe(true);
  await click('Đối chiếu dữ liệu'); await screen.findByRole('heading', { name: 'Changed elsewhere' });
  expect(calls.filter(req => req.method === 'POST')).toHaveLength(1);
});
it('renders ordered provenance and untrusted summaries as text and pages forward', async () => {
  setup((req, path) => path.endsWith('/timeline') ? new URL(req.url).searchParams.has('cursor') ? page([{ ...entry, id, summary: 'Older' }]) : page([entry], 'opaque') : undefined);
  await screen.findByText(entry.summary); expect(document.querySelector('script')).toBeNull();
  await screen.findByText('remote.module', { exact: false }); await screen.findByText(id, { exact: false });
  await click('Hoạt động trước'); await screen.findByText('Older');
  expect(document.body.textContent!.indexOf(entry.summary)).toBeLessThan(document.body.textContent!.indexOf('Older'));
});
it.each([true, false])('validates Unicode note length and uses returned or refreshed ETag (%s)', async hasHeader => {
  let writes = 0;
  const calls = setup((_req, path) => {
    if (path.endsWith('/notes')) { writes++; return Response.json(entry, { status: 201, headers: hasHeader ? { ETag: '"v2"' } : {} }); }
    if (path.endsWith(id)) return record(writes ? (hasHeader ? 2 : 7) : 1);
  });
  await screen.findByText('Chưa có hoạt động.');
  await change('Ghi chú', '🙂'.repeat(1001)); expect((screen.getByRole('button', { name: 'Thêm ghi chú' }) as HTMLButtonElement).disabled).toBe(true);
  await change('Ghi chú', '🙂'.repeat(1000)); await click('Thêm ghi chú');
  await waitFor(() => expect((screen.getByLabelText('Ghi chú') as HTMLTextAreaElement).value).toBe(''));
  if (!hasHeader) await click('Đối chiếu dữ liệu');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Chỉnh sửa' }).matches(':disabled')).toBe(false));
  await change('Ghi chú', 'B');
  await click('Thêm ghi chú');
  await waitFor(() => expect(writes).toBe(2));
  expect(calls.filter(r => r.method === 'POST').map(r => r.headers.get('If-Match'))).toEqual(['"v1"', hasHeader ? '"v2"' : '"v7"']);
});
it.each([412, 0])('keeps draft after conflict or uncertain delivery and requires explicit reconciliation (%i)', async status => {
  const calls = setup((_req, path) => { if (path.endsWith('/notes')) { if (!status) throw Error(); return new Response(null, { status }); } });
  await change('Ghi chú', 'Draft'); await click('Thêm ghi chú');
  expect((await screen.findByRole('alert')).textContent).toContain(status ? 'đã thay đổi' : 'Chưa xác định');
  expect(screen.getByDisplayValue('Draft')).toBeTruthy();
  await click('Đối chiếu dữ liệu'); await waitFor(() => expect((screen.getByRole('button', { name: 'Thêm ghi chú' }) as HTMLButtonElement).disabled).toBe(false));
  expect(calls.filter(r => r.method === 'POST')).toHaveLength(1);
});
it('retries a failed timeline read and rejects a different contact in the page', async () => {
  let reads = 0;
  setup((_req, path) => {
    if (path.endsWith('/timeline')) return ++reads === 1 ? page([{ ...entry, contactId: entry.id }]) : page();
  });
  expect((await screen.findByRole('alert')).textContent).toContain('WEB_CRM_CONTRACT_INVALID');
  expect(screen.queryByText(entry.summary)).toBeNull();
  await click('Tải lại hoạt động'); await screen.findByText('Chưa có hoạt động.'); expect(reads).toBe(2);
});
it('removes the stale record when reconciliation confirms deletion', async () => {
  let removed = false;
  setup((_req, path) => {
    if (path.endsWith('/notes')) { removed = true; return Response.json({ reasonToken: 'NOT_FOUND' }, { status: 404 }); }
    if (removed && path.endsWith(id)) return Response.json({ reasonToken: 'NOT_FOUND' }, { status: 404 });
  });
  await change('Ghi chú', 'Draft'); await click('Thêm ghi chú'); await click('Đối chiếu dữ liệu');
  await waitFor(() => expect(screen.queryByRole('heading', { name: 'Demo' })).toBeNull());
  expect(screen.queryByDisplayValue('Draft')).toBeNull();
  expect((await screen.findByRole('alert')).textContent).toContain('NOT_FOUND');
});
it('holds a timed-out note for explicit review without replay', async () => {
  const calls = setup((req, path) => {
    if (path.endsWith('/notes')) return new Promise<Response>((_, reject) => req.signal.addEventListener('abort', () => reject(req.signal.reason)));
  });
  await screen.findByText('Chưa có hoạt động.');
  await change('Ghi chú', 'Pending');
  vi.useFakeTimers();
  try {
    fireEvent.click(screen.getByRole('button', { name: 'Thêm ghi chú' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(10001); });
    expect(screen.getByRole('alert').textContent).toContain('WEB_CRM_TIMEOUT');
    expect(screen.getByRole('button', { name: 'Thêm ghi chú' }).matches(':disabled')).toBe(true);
  } finally { vi.useRealTimers(); }
  await click('Đối chiếu dữ liệu');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Thêm ghi chú' }).matches(':disabled')).toBe(false));
  expect(calls.filter(r => r.method === 'POST')).toHaveLength(1);
});
it('returns keyboard focus to the note input after saving', async () => {
  let saved = false;
  setup((_req, path) => {
    if (path.endsWith('/notes')) { saved = true; return Response.json(entry, { status: 201, headers: { ETag: '"v2"' } }); }
    if (path.endsWith(id)) return record(saved ? 2 : 1);
  });
  await screen.findByText('Chưa có hoạt động.'); await change('Ghi chú', 'Done');
  const submit = screen.getByRole('button', { name: 'Thêm ghi chú' });
  submit.focus(); fireEvent.click(submit);
  await waitFor(() => expect(document.activeElement).toBe(screen.getByLabelText('Ghi chú')));
});
it('gates cross-panel writes on refresh of the conflicting field and preserves its draft', async () => {
  let conflict = false;
  let resolveField: ((response: Response) => void) | undefined;
  const calls = setup((req, path) => {
    if (path.endsWith('/custom-fields')) return page([field]);
    if (path.endsWith('/fields/' + itemId)) {
      if (req.method === 'PUT') return record(3);
      if (!conflict) return new Response('{"value":1}');
      return new Promise<Response>(resolve => { resolveField = resolve; });
    }
    if (path.endsWith('/notes')) { conflict = true; return new Response(null, { status: 412 }); }
    if (path.endsWith(id)) return record(conflict ? 2 : 1);
  });
  await screen.findByDisplayValue('1'); await change('Amount', '500');
  await change('Ghi chú', 'Note draft'); await click('Thêm ghi chú'); await click('Đối chiếu dữ liệu');
  await waitFor(() => expect(resolveField).toBeDefined());
  expect(screen.getByRole('button', { name: 'Lưu Amount' }).matches(':disabled')).toBe(true);
  await act(async () => resolveField!(new Response('{"value":22}')));
  await screen.findByText('Giá trị hiện tại: 22');
  expect(screen.getByDisplayValue('500')).toBeTruthy();
  expect(screen.getByDisplayValue('Note draft')).toBeTruthy();
  expect(calls.filter(r => r.method === 'PUT')).toHaveLength(0);
  await click('Lưu Amount');
  await waitFor(() => expect(calls.find(r => r.method === 'PUT')?.headers.get('If-Match')).toBe('"v2"'));
});
it('allows timeline pagination after the server denies a note write', async () => {
  const calls = setup((req, path) => {
    if (path.endsWith('/notes')) return new Response(null, { status: 403 });
    if (path.endsWith('/timeline')) return new URL(req.url).searchParams.has('cursor') ? page([{ ...entry, id, summary: 'Page two' }]) : page([entry], 'next');
  });
  await screen.findByText(entry.summary);
  await change('Ghi chú', 'Operator draft'); await click('Thêm ghi chú');
  await screen.findByRole('alert');
  expect(screen.getByRole('button', { name: 'Thêm ghi chú' }).matches(':disabled')).toBe(true);
  await click('Hoạt động trước'); await screen.findByText('Page two');
  expect(calls.filter(r => r.method === 'POST')).toHaveLength(1);
});
it('keeps writes locked when a sibling field read fails during reconciliation', async () => {
  let conflict = false, failRefresh = true;
  const calls = setup((_req, path) => {
    if (path.endsWith('/custom-fields')) return page([field]);
    if (path.endsWith('/fields/' + itemId)) {
      if (conflict && failRefresh) return new Response(null, { status: 503 });
      return new Response(conflict ? '{"value":3}' : '{"value":1}');
    }
    if (path.endsWith('/notes')) { conflict = true; return new Response(null, { status: 412 }); }
    if (path.endsWith(id)) return record(conflict ? 2 : 1);
  });
  await screen.findByDisplayValue('1'); await change('Amount', '9'); await change('Ghi chú', 'Draft');
  await click('Thêm ghi chú'); await click('Đối chiếu dữ liệu');
  await screen.findByRole('button', { name: 'Tải lại giá trị' });
  expect(screen.getByRole('button', { name: 'Thêm ghi chú' }).matches(':disabled')).toBe(true);
  expect(screen.getByRole('button', { name: 'Lưu Amount' }).matches(':disabled')).toBe(true);
  failRefresh = false; await click('Đối chiếu dữ liệu');
  await screen.findByText('Giá trị hiện tại: 3');
  expect(screen.getByDisplayValue('9')).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Lưu Amount' }).matches(':disabled')).toBe(false);
  expect(calls.filter(r => r.method !== 'GET')).toHaveLength(1);
});
