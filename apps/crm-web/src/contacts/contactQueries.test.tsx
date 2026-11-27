// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { ContactList } from './ContactList';
import { contactsOptions, fieldValueOptions } from './contactQueries';
import { exactNumber } from './losslessJson';
import { ContactQueryScope, useContactQueryScope } from './queryScope';
import { queryKeys } from './queryKeys';
import type { CrmClient } from './crmClient';

const record = { id: '10000000-0000-4000-8000-000000000001', displayName: 'Cached record', version: 1, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' };
afterEach(cleanup);
it('stores the server page in the scoped infinite query cache and forwards its signal', async () => {
  let scope: ReturnType<typeof useContactQueryScope> | undefined;
  function Probe() { scope = useContactQueryScope(); return null; }
  const listContacts = vi.fn(async (...args: [unknown, AbortSignal]) => { expect(args[1]).toBeInstanceOf(AbortSignal); return { data: { items: [record], nextCursor: null }, etag: '"v1"' }; });
  const client = { listContacts } as unknown as CrmClient;
  render(<ContactQueryScope identity="principal" tenantId="tenant" generation={0}><Probe /><ContactList client={client} /></ContactQueryScope>);
  await screen.findByText('Cached record');
  await waitFor(() => expect(scope!.client.getQueryData(queryKeys.contacts(scope!.scopeId, {}))).toMatchObject({ pages: [{ data: { items: [record] }, etag: '"v1"' }] }));
  expect(listContacts.mock.calls[0]![1]).toBeInstanceOf(AbortSignal);
});
it('reports cancellation when an aborted field read rejects late', async () => {
  let scope: ReturnType<typeof useContactQueryScope> | undefined;
  function Probe() { scope = useContactQueryScope(); return null; }
  render(<ContactQueryScope identity="principal" tenantId="tenant" generation={0}><Probe /></ContactQueryScope>);
  const controller = new AbortController();
  const client = { getField: async () => { controller.abort(); throw new Error('transport error'); } } as unknown as CrmClient;
  const options = fieldValueOptions(scope!, client, record.id, record.id);
  const queryFn = options.queryFn;
  if (typeof queryFn !== 'function') throw new Error('missing query function');
  await expect(queryFn({ signal: controller.signal, queryKey: options.queryKey, meta: undefined, client: scope!.client })).rejects.toMatchObject({ reasonToken: 'WEB_CRM_READ_CANCELLED' });
});
it('preserves the lossless field value and ETag in the query cache', async () => {
  let scope: ReturnType<typeof useContactQueryScope> | undefined;
  function Probe() { scope = useContactQueryScope(); return null; }
  render(<ContactQueryScope identity="principal" tenantId="tenant" generation={0}><Probe /></ContactQueryScope>);
  const value = exactNumber('123456789012345678901234567890123456789');
  const getField = vi.fn(async () => ({ data: value, etag: '"v42"' }));
  const client = { getField } as unknown as CrmClient;
  const options = fieldValueOptions(scope!, client, record.id, record.id);
  const result = await scope!.client.fetchQuery(options);
  expect(result).toEqual({ data: value, etag: '"v42"' });
  expect(scope!.client.getQueryData(options.queryKey)).toEqual(result);
  expect(getField.mock.calls).toHaveLength(1);
});
it.each([
  { label: 'detects a cycle in the new cursor sequence', cursors: ['X', 'Y', 'X'], failure: true },
  { label: 'accepts a cursor used only by the obsolete sequence', cursors: ['X', 'A', null], failure: false },
])('$label during a multi-page refetch', async ({ cursors, failure }) => {
  let scope: ReturnType<typeof useContactQueryScope> | undefined;
  function Probe() { scope = useContactQueryScope(); return null; }
  render(<ContactQueryScope identity="principal" tenantId="tenant" generation={0}><Probe /></ContactQueryScope>);
  let count = 0;
  const listContacts = vi.fn(async () => ({ data: { items: [], nextCursor: cursors[count++] ?? null }, etag: null }));
  const client = { listContacts } as unknown as CrmClient;
  const options = contactsOptions(scope!, client, {});
  const oldPage = (nextCursor: string | null) => ({ data: { items: [], nextCursor }, etag: null, status: 200 });
  scope!.client.setQueryData(options.queryKey, { pages: [oldPage('A'), oldPage('B'), oldPage(null)], pageParams: [undefined, 'A', 'B'] });
  const result = scope!.client.fetchInfiniteQuery({ ...options, staleTime: 0 });
  if (failure) await expect(result).rejects.toMatchObject({ reasonToken: 'WEB_CRM_CONTRACT_INVALID' });
  else await expect(result).resolves.toMatchObject({ pageParams: [undefined, 'X', 'A'] });
});
