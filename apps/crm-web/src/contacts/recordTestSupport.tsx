import { render, fireEvent, screen } from '@testing-library/react';
import { vi } from 'vitest';
import { createMemoryHistory, createRootRoute, createRoute, createRouter, RouterProvider } from '@tanstack/react-router';
import { createCrmClient } from './crmClient';
import { ContactDetail } from './ContactDetail';
import { ContactQueryScope, useContactQueryScope } from './queryScope';

export const id = '10000000-0000-4000-8000-000000000001', itemId = '10000000-0000-4000-8000-000000000002';
export const data = { id, displayName: 'Demo', version: 1, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
export const page = (items: unknown[] = [], nextCursor: string | null = null) => Response.json({ items, nextCursor });
export const record = (version = 1) => Response.json({ ...data, version }, { headers: { ETag: '"v' + version + '"' } });
export const field = { id: itemId, key: 'amount', displayName: 'Amount', dataType: 'number', required: false, version: 1 };
export const entry = { id: itemId, contactId: id, occurredAt: '2026-01-01', source: 'remote.module', type: 'remote.event', inboxId: id, summary: '<script>alert(1)</script>' };
export function setup(handler: (req: Request, path: string) => Response | Promise<Response | undefined> | undefined, inspect?: (scope: ReturnType<typeof useContactQueryScope>) => void) {
  const calls: Request[] = [];
  vi.stubGlobal('fetch', async (req: Request) => {
    calls.push(req);
    const path = new URL(req.url).pathname;
    return await handler(req, path) ?? (path.endsWith('/contacts/' + id) ? record() : page());
  });
  const client = createCrmClient({ origin: location.origin, tenantId: id, token: 'fixture', signal: new AbortController().signal, invalidate() { } });
  function Fixture() { const scope = useContactQueryScope(); inspect?.(scope); return <ContactDetail client={client} id={id} />; }
  const root = createRootRoute({ component: () => <ContactQueryScope identity="fixture" tenantId={id} generation={1}><Fixture /></ContactQueryScope> });
  const home = createRoute({ getParentRoute: () => root, path: '/' });
  const list = createRoute({ getParentRoute: () => root, path: '/app/contacts' });
  const router = createRouter({ routeTree: root.addChildren([home, list]), history: createMemoryHistory({ initialEntries: ['/'] }) });
  window.scrollTo = () => {};
  render(<RouterProvider router={router} />);
  return calls;
}
export const click = async (name: string) => fireEvent.click(await screen.findByRole('button', { name }));
export const change = async (label: string, value: string) => fireEvent.change(await screen.findByLabelText(label), { target: { value } });
