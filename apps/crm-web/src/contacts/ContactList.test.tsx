// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { ContactShell } from './ContactShell';
const id = '10000000-0000-4000-8000-000000000001';
vi.mock('../tenant/TenantProvider', () => ({ useTenant: () => ({ identity: '["i","s"]', selectedTenantId: id }) }));
vi.mock('../auth/AuthProvider', () => ({ useAuthClient: () => auth }));
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
  render(<ContactShell />);
  expect(screen.getByRole('status')).toBeTruthy();
  await respond(0, new Response(null, { status: 503 }));
  fireEvent.click(await screen.findByText('Thử lại'));
  await respond(1, Response.json({ items: [], nextCursor: 'opaque' }));
  fireEvent.click(await screen.findByText('Tải thêm'));
  await respond(2, Response.json({ items: [], nextCursor: 'opaque' }));
  expect(queries[2]!.get('cursor')).toBe('opaque');
  expect(screen.getByRole('alert').textContent).toContain('WEB_CRM_CONTRACT_INVALID');
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
