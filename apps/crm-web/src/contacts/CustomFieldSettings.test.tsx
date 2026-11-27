// @vitest-environment jsdom
import { act, cleanup, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { setup, field, click, change, page } from './recordTestSupport';
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
it.each([201, 403])('lets the server authorize field definitions (%i)', async status => {
  let created = false;
  const calls = setup((req, path) => {
    if (!path.endsWith('/custom-fields')) return;
    if (req.method === 'GET') return page(created ? [field] : []);
    created = status === 201; return Response.json(field, { status });
  });
  await click('Định nghĩa trường'); await change('Khóa trường', 'amount'); await change('Tên trường', 'Amount'); await change('Kiểu dữ liệu', 'number');
  await click('Tạo trường');
  if (created || status === 201) await screen.findByText('Amount', { selector: 'li' });
  else expect((await screen.findByRole('alert')).textContent).toContain('Chỉ đọc');
  expect(await calls.find(r => r.method === 'POST')!.clone().json()).toEqual({ key: 'amount', displayName: 'Amount', dataType: 'number', required: false });
});

it('retains a definition draft if refresh fails after creation', async () => {
  let created = false;
  const calls = setup((req, path) => {
    if (!path.endsWith('/custom-fields')) return;
    if (req.method === 'POST') { created = true; return Response.json(field, { status: 201 }); }
    return created ? new Response(null, { status: 503 }) : page([]);
  });
  await click('Định nghĩa trường'); await change('Khóa trường', 'amount'); await change('Tên trường', 'Amount'); await click('Tạo trường');
  await screen.findByRole('button', { name: 'Đối chiếu dữ liệu' });
  expect((screen.getByLabelText('Tên trường') as HTMLInputElement).value).toBe('Amount');
  expect(calls.filter(r => r.method === 'POST')).toHaveLength(1);
});

it('keeps a definition draft across closing and reopening the panel, including a failed pending create', async () => {
  let reject!: (cause: Error) => void;
  const calls = setup((req, path) => {
    if (!path.endsWith('/custom-fields')) return;
    return req.method === 'POST' ? new Promise<Response>((_resolve, fail) => { reject = fail; }) : page([]);
  });
  await click('Định nghĩa trường'); await change('Khóa trường', 'amount'); await change('Tên trường', 'Keep this');
  await click('Định nghĩa trường'); await click('Định nghĩa trường');
  expect((screen.getByLabelText('Tên trường') as HTMLInputElement).value).toBe('Keep this');
  await click('Tạo trường'); await waitFor(() => expect(reject).toBeDefined());
  await click('Định nghĩa trường');
  await act(async () => reject(new Error('offline')));
  await click('Định nghĩa trường');
  expect((screen.getByLabelText('Tên trường') as HTMLInputElement).value).toBe('Keep this');
  expect(calls.filter(r => r.method === 'POST')).toHaveLength(1);
});
