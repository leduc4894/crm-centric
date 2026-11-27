// @vitest-environment jsdom
import { act, cleanup, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { setup, data, id, field, itemId, page, record, click, change } from './recordTestSupport';
import { queryKeys } from './queryKeys';
import type { useContactQueryScope } from './queryScope';
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.useRealTimers(); });
it('stores contact and attribute reads in the scoped query cache', async () => {
  let scope: ReturnType<typeof useContactQueryScope> | undefined;
  setup((_req, path) => path.endsWith('/custom-fields') ? page([field]) : path.endsWith('/fields/' + itemId) ? Response.json({ value: 1 }) : undefined, value => { scope = value; });
  await screen.findByLabelText('Amount');
  await waitFor(() => {
    expect(scope?.client.getQueryData(queryKeys.contact(scope.scopeId, id))).toMatchObject({ data: { id }, etag: '"v1"' });
    expect(scope?.client.getQueryData(queryKeys.fieldValue(scope.scopeId, id, itemId))).toBeDefined();
  });
});
it('keeps the reviewed version when the query cache receives a newer contact during editing', async () => {
  let scope: ReturnType<typeof useContactQueryScope> | undefined;
  const calls = setup((req, path) => path.endsWith('/contacts/' + id) && req.method === 'PATCH' ? record(2) : undefined, value => { scope = value; });
  await click('Chỉnh sửa');
  await change('Tên liên hệ', 'Draft');
  scope!.client.setQueryData(queryKeys.contact(scope!.scopeId, id), { data: { ...data, displayName: 'Elsewhere', version: 2 }, etag: '"v2"', status: 200 });
  await click('Lưu');
  await waitFor(() => expect(calls.find(req => req.method === 'PATCH')?.headers.get('If-Match')).toBe('"v1"'));
});
it('collects all tag and field pages through scoped queries', async () => {
  const calls = setup((req, path) => {
    const cursor = new URL(req.url).searchParams.get('cursor');
    if (path.endsWith('/v1/tags')) return cursor ? page([{ id: itemId, name: 'VIP' }]) : page([], 'second-tag-page');
    if (path.endsWith('/custom-fields')) return cursor ? page([field]) : page([], 'second-field-page');
    if (path.endsWith('/fields/' + itemId)) return Response.json({ value: 1 });
  });
  await screen.findByRole('option', { name: 'VIP' });
  await screen.findByDisplayValue('1');
  expect(calls.filter(req => new URL(req.url).searchParams.has('cursor'))).toHaveLength(2);
});
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
it.each(['field', 'delete'])('reviews a changed record and sibling values before an unversioned %s action', async action => {
  let reads = 0;
  const calls = setup((req, path) => {
    if (path.endsWith('/contacts/' + id)) {
      if (req.method === 'DELETE') return new Response(null, { status: 204 });
      if (++reads === 1) return Response.json(data);
      return Response.json({ ...data, displayName: 'Updated', version: 2 }, { headers: { ETag: '"v2"' } });
    }
    if (path.endsWith('/custom-fields')) return page([field]);
    if (path.endsWith('/fields/' + itemId)) return req.method === 'GET' ? Response.json({ value: reads === 1 ? 1 : 2 }) : record(3);
  });
  await screen.findByDisplayValue('1');
  await change('Amount', '500');
  async function submit() {
    if (action === 'field') await click('Lưu Amount');
    else { await click('Xóa liên hệ'); await click('Xác nhận xóa'); }
  }
  const mutations = () => calls.filter(req => ['PUT', 'DELETE'].includes(req.method));
  await submit();
  await click('Đối chiếu dữ liệu');
  await screen.findByRole('heading', { name: 'Updated' });
  await screen.findByText('Giá trị hiện tại: 2');
  expect(screen.getByDisplayValue('500')).toBeTruthy();
  expect(mutations()).toHaveLength(0);
  await submit();
  await waitFor(() => expect(mutations().map(req => req.headers.get('If-Match'))).toEqual(['"v2"']));
});
it('does not send an unchanged field when its form is submitted directly', async () => {
  const calls = setup((req, path) => {
    if (path.endsWith('/custom-fields')) return page([field]);
    if (path.endsWith('/fields/' + itemId)) return req.method === 'GET' ? new Response('{"value":1}') : record(2);
  });
  await screen.findByDisplayValue('1');
  await change('Amount', '2');
  await change('Amount', '1');
  const button = screen.getByRole('button', { name: 'Lưu Amount' });
  expect(button.matches(':disabled')).toBe(true);
  const { fireEvent } = await import('@testing-library/react');
  fireEvent.submit(button.closest('form')!);
  await waitFor(() => expect(calls.filter(req => req.method === 'PUT')).toHaveLength(0));
});
it.each(['1e131072', '1e-16384', '123456789012345678901234567890123', '1.00E+0'])('preserves numeric token %s through the field form and wire', async token => {
  let raw = '0';
  setup(async (req, path) => {
    if (path.endsWith('/custom-fields')) return page([field]);
    if (path.endsWith('/fields/' + itemId)) {
      if (req.method === 'GET') return new Response('{"value":' + raw + '}');
      raw = (await req.clone().text()).slice(9, -1); return record(2);
    }
  });
  await screen.findByDisplayValue('0');
  await change('Amount', token); await click('Lưu Amount');
  await waitFor(() => expect(raw).toBe(token));
  await screen.findByDisplayValue(token);
});
it.each([403, 404, 500])('retains a tag draft and write lock after 412 when sibling refresh returns %i', async status => {
  let writes = 0;
  let reads = 0;
  const tag = { id: itemId, name: 'VIP' };
  setup((req, path) => {
    if (path.endsWith('/v1/tags')) return page([tag]);
    if (path.endsWith('/custom-fields')) return page([field]);
    if (path.endsWith('/fields/' + itemId)) return ++reads > 1 ? new Response(null, { status }) : new Response('{"value":1}');
    if (path.endsWith('/tags/' + itemId) && req.method === 'PUT') { writes++; return new Response(null, { status: 412 }); }
  });
  await change('Thẻ có sẵn', itemId); await click('Gắn thẻ');
  await click('Đối chiếu dữ liệu');
  await screen.findByText(/WEB_CRM/);
  expect((screen.getByLabelText('Thẻ có sẵn') as HTMLSelectElement).value).toBe(itemId);
  expect(screen.getByRole('button', { name: 'Gắn thẻ' }).matches(':disabled')).toBe(true);
  expect(writes).toBe(1);
});
it('prevents duplicate tag creation while a request is pending', async () => {
  let release!: (response: Response) => void;
  const pending = new Promise<Response>(resolve => { release = resolve; });
  const calls = setup((req, path) => path.endsWith('/v1/tags') && req.method === 'POST' ? pending : undefined);
  await change('Tên thẻ mới', 'VIP');
  const button = screen.getByRole('button', { name: 'Tạo thẻ' });
  const { fireEvent } = await import('@testing-library/react');
  fireEvent.submit(button.closest('form')!); fireEvent.submit(button.closest('form')!);
  await waitFor(() => expect(calls.filter(req => req.method === 'POST')).toHaveLength(1));
  release(Response.json({ id: itemId, name: 'VIP' }, { status: 201 }));
  await waitFor(() => expect((screen.getByLabelText('Tên thẻ mới') as HTMLInputElement).value).toBe(''));
});
it('retains field draft if refresh fails after a successful write', async () => {
  let reads = 0;
  setup((req, path) => {
    if (path.endsWith('/custom-fields')) return page([field]);
    if (path.endsWith('/fields/' + itemId)) {
      if (req.method === 'PUT') return record(2);
      return ++reads === 2 ? new Response(null, { status: 500 }) : new Response('{"value":1}');
    }
  });
  await screen.findByDisplayValue('1');
  await change('Amount', '123'); await click('Lưu Amount');
  await click('Tải lại giá trị');
  await screen.findByDisplayValue('123');
});
it('serializes simultaneous submissions from different attribute forms', async () => {
  let release!: (response: Response) => void;
  const pending = new Promise<Response>(resolve => { release = resolve; });
  const calls = setup((req, path) => {
    if (path.endsWith('/custom-fields')) return page([field]);
    if (path.endsWith('/fields/' + itemId)) return req.method === 'PUT' ? record(2) : new Response('{"value":1}');
    if (path.endsWith('/v1/tags') && req.method === 'POST') return pending;
  });
  await screen.findByDisplayValue('1');
  await change('Tên thẻ mới', 'VIP'); await change('Amount', '2');
  const { fireEvent } = await import('@testing-library/react');
  fireEvent.submit(screen.getByRole('button', { name: 'Tạo thẻ' }).closest('form')!);
  fireEvent.submit(screen.getByRole('button', { name: 'Lưu Amount' }).closest('form')!);
  await waitFor(() => expect(calls.filter(req => ['POST', 'PUT'].includes(req.method))).toHaveLength(1));
  release(Response.json({ id: itemId, name: 'VIP' }, { status: 201 }));
});
it.each([403, 404, 409, 500])('keeps write locked and draft after successful field write followed by refresh %i', async status => {
  let reads = 0, writes = 0;
  setup((req, path) => {
    if (path.endsWith('/custom-fields')) return page([field]);
    if (path.endsWith('/fields/' + itemId)) {
      if (req.method === 'PUT') { writes++; return record(2); }
      return ++reads === 1 ? new Response('{"value":1}') : new Response(null, { status });
    }
  });
  await screen.findByDisplayValue('1');
  await change('Amount', '123'); await click('Lưu Amount');
  await screen.findByRole('button', { name: 'Đối chiếu dữ liệu' });
  expect(screen.getByRole('button', { name: 'Lưu Amount' }).matches(':disabled')).toBe(true);
  expect((screen.getByLabelText('Amount') as HTMLInputElement).value).toBe('123');
  expect(writes).toBe(1);
});
it.each(['missing', 'invalid'])('keeps field draft and requires review on a %s ETag write response', async mode => {
  let writes = 0;
  setup((req, path) => {
    if (path.endsWith('/custom-fields')) return page([field]);
    if (path.endsWith('/fields/' + itemId)) {
      if (req.method === 'PUT') { writes++; return Response.json({ ...data, version: 2 }, mode === 'invalid' ? { headers: { ETag: 'W/"v2"' } } : undefined); }
      return new Response('{"value":1}');
    }
  });
  await screen.findByDisplayValue('1'); await change('Amount', '123'); await click('Lưu Amount');
  await screen.findByRole('button', { name: 'Đối chiếu dữ liệu' });
  expect((screen.getByLabelText('Amount') as HTMLInputElement).value).toBe('123');
  expect(screen.getByRole('button', { name: 'Lưu Amount' }).matches(':disabled')).toBe(true);
  expect(writes).toBe(1);
});
it('keeps tag selection and requires review after an attachment returns no ETag', async () => {
  let writes = 0;
  setup((req, path) => {
    if (path.endsWith('/v1/tags')) return page([{ id: itemId, name: 'VIP' }]);
    if (path.endsWith('/tags/' + itemId) && req.method === 'PUT') { writes++; return Response.json({ ...data, version: 2 }); }
  });
  await change('Thẻ có sẵn', itemId); await click('Gắn thẻ');
  await screen.findByRole('button', { name: 'Đối chiếu dữ liệu' });
  expect((screen.getByLabelText('Thẻ có sẵn') as HTMLSelectElement).value).toBe(itemId);
  expect(screen.getByRole('button', { name: 'Gắn thẻ' }).matches(':disabled')).toBe(true);
  expect(writes).toBe(1);
});
it('keeps a tag draft after a literal timeout without retrying', async () => {
  const calls = setup((req, path) => {
    if (path.endsWith('/v1/tags') && req.method === 'POST') return new Promise((_resolve, reject) => req.signal.addEventListener('abort', () => reject(req.signal.reason)));
  });
  await change('Tên thẻ mới', 'Timeout draft');
  vi.useFakeTimers();
  const { fireEvent } = await import('@testing-library/react');
  fireEvent.click(screen.getByRole('button', { name: 'Tạo thẻ' }));
  await act(async () => { await vi.advanceTimersByTimeAsync(10000); });
  vi.useRealTimers();
  await screen.findByRole('button', { name: 'Đối chiếu dữ liệu' });
  expect((screen.getByLabelText('Tên thẻ mới') as HTMLInputElement).value).toBe('Timeout draft');
  expect(calls.filter(req => req.method === 'POST')).toHaveLength(1);
});
it.each(['null', '""'])('leaves existing optional string %s unchanged until edited', async raw => {
  const calls = setup((req, path) => {
    if (path.endsWith('/custom-fields')) return page([{ ...field, dataType: 'string' }]);
    if (path.endsWith('/fields/' + itemId)) return new Response('{"value":' + raw + '}');
  });
  const input = await screen.findByLabelText('Amount') as HTMLInputElement;
  await waitFor(() => expect(input.value).toBe(''));
  expect(screen.getByRole('button', { name: 'Lưu Amount' }).matches(':disabled')).toBe(true);
  expect(calls.filter(req => req.method === 'PUT')).toHaveLength(0);
});
