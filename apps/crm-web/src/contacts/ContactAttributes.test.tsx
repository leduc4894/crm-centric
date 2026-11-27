// @vitest-environment jsdom
import { cleanup, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { setup, field, itemId, page, record, click, change } from './recordTestSupport';
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
it('creates a tag, attaches and removes it with the latest contact version', async () => {
  let created = false, attached = false;
  const tag = { id: itemId, name: 'VIP' };
  const calls = setup((req, path) => {
    if (path.endsWith('/v1/tags')) {
      if (req.method === 'POST') { created = true; return Response.json(tag, { status: 201 }); }
      return page(created ? [tag] : []);
    }
    if (path.endsWith('/tags/' + itemId)) { attached = req.method === 'PUT'; return record(attached ? 2 : 3); }
    if (path.endsWith('/tags')) return page(attached ? [tag] : []);
  });
  await change('Tên thẻ mới', 'VIP'); await click('Tạo thẻ');
  await screen.findByRole('option', { name: 'VIP' }); await change('Thẻ có sẵn', itemId); await click('Gắn thẻ');
  await click('Gỡ VIP'); await waitFor(() => expect(screen.queryByRole('button', { name: 'Gỡ VIP' })).toBeNull());
  expect(calls.filter(r => ['PUT', 'DELETE'].includes(r.method)).map(r => r.headers.get('If-Match'))).toEqual(['"v1"', '"v2"']);
});
it.each([['number', '9999999999999999999999999999.01'], ['string', 'xin chào'], ['boolean', 'false'], ['date', '2026-11-27']])('writes the exact %s scalar and supports an optional null', async (type, value) => {
  let raw = 'null';
  const calls = setup(async (req, path) => {
    if (path.endsWith('/custom-fields')) return page([{ ...field, dataType: type }]);
    if (path.endsWith('/fields/' + itemId)) {
      if (req.method === 'GET') return new Response('{"value":' + raw + '}');
      raw = (await req.clone().text()).slice(9, -1); return record(2);
    }
  });
  await change('Amount', value); await click('Lưu Amount');
  await waitFor(() => expect(raw).toEqual(['number', 'boolean'].includes(type) ? value : JSON.stringify(value)));
  await waitFor(() => expect((screen.getByLabelText('Amount') as HTMLInputElement).disabled).toBe(false));
  await change('Amount', ''); await click('Lưu Amount'); await waitFor(() => expect(raw).toBe('null'));
  expect(calls.filter(r => r.method === 'PUT')).toHaveLength(2);
});
it('shows the newly read field beside a conflicting draft and blocks a required null', async () => {
  let writes = 0;
  setup((req, path) => {
    if (path.endsWith('/custom-fields')) return page([{ ...field, required: true }]);
    if (path.endsWith('/fields/' + itemId)) {
      if (req.method === 'PUT') { writes++; return new Response(null, { status: 412 }); }
      return new Response(writes ? '{"value":99887766554433221100123}' : '{"value":1}');
    }
  });
  await screen.findByDisplayValue('1');
  await change('Amount', ''); expect(screen.getByRole('button', { name: 'Lưu Amount' }).matches(':disabled')).toBe(true);
  await change('Amount', '500'); await click('Lưu Amount'); await click('Đối chiếu dữ liệu');
  await screen.findByText('Giá trị hiện tại: 99887766554433221100123');
  expect(screen.getByDisplayValue('500')).toBeTruthy(); expect(writes).toBe(1);
});
it.each([403, 404])('handles forbidden and missing tag writes without replay (%i)', async status => {
  const calls = setup((req, path) => {
    if (path.endsWith('/v1/tags')) return page([{ id: itemId, name: 'VIP' }]);
    if (req.method === 'PUT') return Response.json({ reasonToken: status === 404 ? 'NOT_FOUND' : 'TENANT_FORBIDDEN' }, { status });
  });
  await change('Thẻ có sẵn', itemId); await click('Gắn thẻ');
  await screen.findByRole('alert'); expect(screen.getByRole('button', { name: 'Gắn thẻ' }).matches(':disabled')).toBe(true);
  if (status === 404) await click('Đối chiếu dữ liệu');
  expect(calls.filter(r => r.method === 'PUT')).toHaveLength(1);
});
it('preserves the server ETag on an idempotent tag attachment', async () => {
  const tag = { id: itemId, name: 'VIP' };
  let attached = false;
  const calls = setup((req, path) => {
    if (path.endsWith('/v1/tags')) return page([tag]);
    if (path.endsWith('/tags')) return page(attached ? [tag] : []);
    if (path.endsWith('/tags/' + itemId)) {
      attached = req.method === 'PUT';
      return record(1);
    }
  });
  await change('Thẻ có sẵn', itemId); await click('Gắn thẻ'); await click('Gỡ VIP');
  await waitFor(() => expect(calls.filter(r => r.method === 'DELETE')).toHaveLength(1));
  expect(calls.filter(r => ['PUT','DELETE'].includes(r.method)).map(r => r.headers.get('If-Match'))).toEqual(['"v1"','"v1"']);
});
