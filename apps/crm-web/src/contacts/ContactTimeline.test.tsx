// @vitest-environment jsdom
import { act, cleanup, fireEvent, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { setup, entry, page, record, click, change, id } from './recordTestSupport';
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
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
    if (path.endsWith(id)) return record(writes ? 7 : 1);
  });
  await screen.findByText('Chưa có hoạt động.');
  await change('Ghi chú', '🙂'.repeat(1001)); expect((screen.getByRole('button', { name: 'Thêm ghi chú' }) as HTMLButtonElement).disabled).toBe(true);
  await change('Ghi chú', '🙂'.repeat(1000)); await click('Thêm ghi chú');
  await waitFor(() => expect((screen.getByLabelText('Ghi chú') as HTMLTextAreaElement).value).toBe(''));
  await change('Ghi chú', 'B'); await click('Thêm ghi chú');
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
  setup((_req, path) => path.endsWith('/notes') ? Response.json(entry, { status: 201, headers: { ETag: '"v2"' } }) : undefined);
  await screen.findByText('Chưa có hoạt động.'); await change('Ghi chú', 'Done');
  const submit = screen.getByRole('button', { name: 'Thêm ghi chú' });
  submit.focus(); fireEvent.click(submit);
  await waitFor(() => expect(document.activeElement).toBe(screen.getByLabelText('Ghi chú')));
});
