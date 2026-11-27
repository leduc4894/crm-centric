// @vitest-environment jsdom
import { useEffect } from 'react';
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { useQuery, useMutation } from '@tanstack/react-query';
import { afterEach, expect, it, vi } from 'vitest';
import { ContactQueryScope, useContactQueryScope } from './queryScope';
import { queryKeys } from './queryKeys';
import { createCrmClient } from './crmClient';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const pending: Array<{ resolve: (value: string) => void; signal: AbortSignal }> = [];
function Read() {
  const scope = useContactQueryScope();
  const result = useQuery({ queryKey: queryKeys.contacts(scope.scopeId, {}), queryFn: ({ signal }) => new Promise<string>(resolve => pending.push({ resolve, signal })) });
  return <p>{result.data ?? 'loading'}</p>;
}
it('isolates tenant, identity and generation and ignores a late read across A to B to A', async () => {
  pending.length = 0;
  const view = render(<ContactQueryScope identity="a" tenantId="one" generation={1}><Read /></ContactQueryScope>);
  await waitFor(() => expect(pending).toHaveLength(1));
  view.rerender(<ContactQueryScope identity="a" tenantId="two" generation={1}><Read /></ContactQueryScope>);
  expect(pending[0]!.signal.aborted).toBe(true);
  await waitFor(() => expect(pending).toHaveLength(2));
  view.rerender(<ContactQueryScope identity="a" tenantId="one" generation={2}><Read /></ContactQueryScope>);
  await waitFor(() => expect(pending).toHaveLength(3));
  await act(async () => pending[2]!.resolve('new session'));
  await screen.findByText('new session');
  await act(async () => { pending[0]!.resolve('old session'); pending[1]!.resolve('other tenant'); });
  expect(screen.queryByText('old session')).toBeNull();
  expect(screen.queryByText('other tenant')).toBeNull();
  expect(screen.getByText('new session')).toBeTruthy();
});
it('disables automatic query and mutation retries', async () => {
  const read = vi.fn(async () => { throw new Error('failed'); });
  const write = vi.fn(async () => { throw new Error('failed'); });
  function Failure() {
    const { scopeId } = useContactQueryScope();
    const query = useQuery({ queryKey: queryKeys.tags(scopeId), queryFn: read });
    const mutation = useMutation({ mutationFn: write });
    return <><span>{query.status}</span><button onClick={() => mutation.mutate()}>write</button><span>{mutation.status}</span></>;
  }
  render(<ContactQueryScope identity="a" tenantId="one" generation={1}><Failure /></ContactQueryScope>);
  await screen.findByText('error');
  act(() => screen.getByRole('button').click());
  await waitFor(() => expect(screen.getAllByText('error')).toHaveLength(2));
  expect(read).toHaveBeenCalledTimes(1); expect(write).toHaveBeenCalledTimes(1);
});

it('keeps the scope current after React strict effect replay', async () => {
  const { StrictMode } = await import('react');
  function Status() { const scope = useContactQueryScope(); return <button onClick={event => { event.currentTarget.textContent = scope.isCurrent() ? 'current' : 'closed'; }}>check</button>; }
  render(<StrictMode><ContactQueryScope identity="a" tenantId="one" generation={1}><Status /></ContactQueryScope></StrictMode>);
  act(() => screen.getByRole('button').click());
  expect(screen.getByRole('button').textContent).toBe('current');
});

it('does not reuse cache after logout and login with the same identity', async () => {
  pending.length = 0;
  const view = render(<ContactQueryScope identity="a" tenantId="one" generation={1}><Read /></ContactQueryScope>);
  await waitFor(() => expect(pending).toHaveLength(1));
  await act(async () => pending[0]!.resolve('private old data'));
  await screen.findByText('private old data');
  view.rerender(<p>signed out</p>);
  view.rerender(<ContactQueryScope identity="a" tenantId="one" generation={2}><Read /></ContactQueryScope>);
  expect(screen.queryByText('private old data')).toBeNull();
  await waitFor(() => expect(pending).toHaveLength(2));
});

it('keeps an old mutation callback out of the new session cache', async () => {
  let finish: ((value: string) => void) | undefined;
  let oldCurrent: (() => boolean) | undefined;
  function Mutator() {
    const scope = useContactQueryScope(); useEffect(() => { oldCurrent = scope.isCurrent; }, [scope]);
    const mutation = useMutation({ mutationFn: () => new Promise<string>(resolve => { finish = resolve; }), onSuccess: value => { if (scope.isCurrent()) scope.client.setQueryData(queryKeys.tags(scope.scopeId), value); } });
    return <button onClick={() => mutation.mutate()}>write</button>;
  }
  function Cached() { const scope = useContactQueryScope(); const query = useQuery({ queryKey: queryKeys.tags(scope.scopeId), queryFn: async () => 'new session' }); return <p>{query.data}</p>; }
  const view = render(<ContactQueryScope identity="a" tenantId="one" generation={1}><Mutator /></ContactQueryScope>);
  act(() => screen.getByRole('button').click());
  await waitFor(() => expect(finish).toBeDefined());
  view.rerender(<ContactQueryScope identity="a" tenantId="one" generation={2}><Cached /></ContactQueryScope>);
  await screen.findByText('new session');
  expect(oldCurrent!()).toBe(false);
  await act(async () => finish!('old mutation'));
  expect(screen.queryByText('old mutation')).toBeNull();
});

it('passes query cancellation through the CRM transport without putting the token in query metadata', async () => {
  let signal: AbortSignal | undefined;
  vi.stubGlobal('fetch', (request: Request) => { signal = request.signal; return new Promise<Response>(() => {}); });
  const transport = createCrmClient({ origin: location.origin, tenantId: '10000000-0000-4000-8000-000000000002', token: 'synthetic-secret', signal: new AbortController().signal, invalidate: () => {} });
  let keys = '';
  function ClientRead() {
    const scope = useContactQueryScope();
    useQuery({ queryKey: queryKeys.contacts(scope.scopeId, {}), queryFn: ({ signal: querySignal }) => transport.listContacts({}, querySignal) });
    useEffect(() => { keys = JSON.stringify(scope.client.getQueryCache().getAll().map(query => [query.queryKey, query.meta])); }, [scope]);
    return null;
  }
  const view = render(<ContactQueryScope identity="a" tenantId="one" generation={1}><ClientRead /></ContactQueryScope>);
  await waitFor(() => expect(signal).toBeDefined());
  expect(keys).not.toContain('synthetic-secret');
  view.unmount();
  expect(signal!.aborted).toBe(true);
});
