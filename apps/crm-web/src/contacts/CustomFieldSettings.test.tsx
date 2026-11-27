// @vitest-environment jsdom
import { cleanup, screen } from '@testing-library/react';
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
