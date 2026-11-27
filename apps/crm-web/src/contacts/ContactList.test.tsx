// @vitest-environment jsdom
import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { renderContactRoute } from './testRouter';
const id = '10000000-0000-4000-8000-000000000001';
vi.mock('../tenant/TenantProvider', () => ({ useTenant: () => ({ identity: '["i","s"]', selectedTenantId: id }) }));
vi.mock('../auth/AuthProvider', () => ({ useAuthClient: () => auth, useAuth: () => ({ state: authState }) }));
const authState = { status: 'authenticated', issuer: 'i', subject: 's' };
const auth = { getAccessToken: () => 'test', getSnapshot: () => ({ status: 'authenticated', issuer: 'i', subject: 's' }) };
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
it('retries a failed read, applies literal filters, resets paging and rejects repeated cursors', async () => {
  history.replaceState(null, '', '/app/contacts');
  const queries: URLSearchParams[] = [];
  const pending: Array<(response: Response) => void> = [];
  vi.stubGlobal('fetch', (req: Request) => {
    queries.push(new URL(req.url).searchParams);
    return new Promise<Response>(resolve => pending.push(resolve));
  });
  const respond = async (index: number, response: Response) => {
    await waitFor(() => expect(pending[index]).toBeDefined());
    await act(async () => pending[index]!(response));
  };
  renderContactRoute();
  expect(await screen.findByRole('status')).toBeTruthy();
  await respond(0, new Response(null, { status: 503 }));
  fireEvent.click(await screen.findByText('Thử lại'));
  await respond(1, Response.json({ items: [], nextCursor: 'opaque' }));
  fireEvent.click(await screen.findByText('Tải thêm'));
  await respond(2, Response.json({ items: [], nextCursor: 'opaque' }));
  expect(queries[2]!.get('cursor')).toBe('opaque');
  expect((await screen.findByRole('alert')).textContent).toContain('WEB_CRM_CONTRACT_INVALID');
  const search = screen.getByLabelText('Tìm kiếm');
  fireEvent.change(search, { target: { value: '%_+' } });
  fireEvent.change(screen.getByLabelText('ID thẻ'), { target: { value: id } });
  fireEvent.change(screen.getByLabelText('Cập nhật từ'), { target: { value: '2026-01-01T12:00' } });
  fireEvent.click(screen.getByText('Lọc'));
  await waitFor(() => expect(queries).toHaveLength(4));
  expect(Object.fromEntries(queries[3]!)).toEqual({ q: '%_+', tagId: id, updatedSince: new Date('2026-01-01T12:00').toISOString() });
  fireEvent.change(search, { target: { value: 'new' } });
  fireEvent.click(screen.getByText('Lọc'));
  await respond(4, Response.json({ items: [], nextCursor: null }));
  await respond(3, new Response(null, { status: 500 }));
  expect(screen.getByText('Chưa có liên hệ.')).toBeTruthy();
  expect(screen.queryByRole('alert')).toBeNull();
});
it('does not reuse old pages when returning to a previous filter', async () => {
  history.replaceState(null, '', '/app/contacts');
  const pending: Array<(response: Response) => void> = [];
  vi.stubGlobal('fetch', () => new Promise<Response>(resolve => pending.push(resolve)));
  const page = (name: string, nextCursor: string | null) => Response.json({ items: [{ id, displayName: name, version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' }], nextCursor });
  renderContactRoute();
  await waitFor(() => expect(pending).toHaveLength(1));
  await act(async () => pending[0]!(page('Old page', 'cursor')));
  await screen.findByText('Old page');
  const search = screen.getByLabelText('Tìm kiếm');
  fireEvent.change(search, { target: { value: 'other' } }); fireEvent.click(screen.getByText('Lọc'));
  await waitFor(() => expect(pending).toHaveLength(2));
  await act(async () => pending[1]!(page('Other page', null)));
  await screen.findByText('Other page');
  fireEvent.change(search, { target: { value: '' } }); fireEvent.click(screen.getByText('Lọc'));
  expect(screen.queryByText('Old page')).toBeNull();
  await waitFor(() => expect(pending).toHaveLength(3));
  await act(async () => pending[2]!(page('Fresh page', null)));
  await screen.findByText('Fresh page');
  expect(screen.queryByText('Tải thêm')).toBeNull();
});
it('table_preserves_server_pages_and_filter_race', async () => {
  history.replaceState(null, '', '/app/contacts');
  const pending: Array<(response: Response) => void> = [];
  const queries: URLSearchParams[] = [];
  vi.stubGlobal('fetch', (req: Request) => {
    queries.push(new URL(req.url).searchParams);
    return new Promise<Response>(resolve => pending.push(resolve));
  });
  const respond = async (index: number, response: Response) => {
    await waitFor(() => expect(pending[index]).toBeDefined());
    await act(async () => pending[index]!(response));
  };
  const row = (suffix: number, displayName: string) => ({
    id: id.slice(0, -1) + suffix, displayName, version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01',
  });
  const page = (items: ReturnType<typeof row>[], nextCursor: string | null) => Response.json({ items, nextCursor });
  const names = () => within(screen.getByRole('table')).getAllByRole('link').map(link => link.textContent);
  renderContactRoute();
  await waitFor(() => expect(pending).toHaveLength(1));
  fireEvent.change(screen.getByLabelText('Tìm kiếm'), { target: { value: '%_+' } });
  fireEvent.change(screen.getByLabelText('ID thẻ'), { target: { value: id } });
  fireEvent.change(screen.getByLabelText('Cập nhật từ'), { target: { value: '2026-01-01T12:00' } });
  fireEvent.click(screen.getByText('Lọc'));
  await respond(1, page([row(1, 'Zulu'), row(2, 'Beta')], 'cursor-A'));
  await screen.findByRole('table');
  await respond(0, page([row(4, 'Old filter')], null));
  expect(names()).toEqual(['Zulu', 'Beta']);
  fireEvent.click(screen.getByText('Tải thêm'));
  await respond(2, new Response(null, { status: 503 }));
  fireEvent.click(await screen.findByText('Thử lại'));
  await respond(3, page([row(2, 'New Beta'), row(3, 'Alpha')], 'cursor-B'));
  await screen.findByRole('link', { name: 'New Beta' });
  expect(names()).toEqual(['Zulu', 'New Beta', 'Alpha']);
  expect(screen.queryByText(/tổng số|trang \d+|\d+ liên hệ/i)).toBeNull();
  fireEvent.click(screen.getByText('Tải thêm'));
  await respond(4, page([row(4, 'Invalid page')], 'cursor-A'));
  expect((await screen.findByRole('alert')).textContent).toContain('WEB_CRM_CONTRACT_INVALID');
  expect(names()).toEqual(['Zulu', 'New Beta', 'Alpha']);
  fireEvent.click(screen.getByText('Thử lại'));
  await waitFor(() => expect(pending).toHaveLength(6));
  fireEvent.change(screen.getByLabelText('Tìm kiếm'), { target: { value: 'fresh' } });
  fireEvent.click(screen.getByText('Lọc'));
  expect(screen.queryByRole('table')).toBeNull();
  await respond(6, page([row(4, 'Fresh result')], null));
  await screen.findByRole('table');
  await respond(5, page([row(3, 'Stale next page')], null));
  expect(names()).toEqual(['Fresh result']);
  expect(screen.queryByText('Tải thêm')).toBeNull();
  const filters = { q: '%_+', tagId: id, updatedSince: new Date('2026-01-01T12:00').toISOString() };
  expect(queries.map(params => Object.fromEntries(params))).toEqual([
    {}, filters, { ...filters, cursor: 'cursor-A' }, { ...filters, cursor: 'cursor-A' }, { ...filters, cursor: 'cursor-B' },
    { ...filters, cursor: 'cursor-B' }, { ...filters, q: 'fresh' },
  ]);
});
