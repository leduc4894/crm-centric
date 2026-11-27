// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryHistory, RouterProvider } from '@tanstack/react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { createAppRouter } from './router';

vi.mock('./tenant/TenantProvider', () => ({ TenantProvider: ({ children }: { children: React.ReactNode }) => <>{children}</> }));
vi.mock('./tenant/TenantPicker', () => ({ TenantPicker: () => null }));
vi.mock('./health/HealthStatus', () => ({ HealthStatus: () => null }));
vi.mock('./contacts/ContactShell', () => ({ ContactShell: ({ recordId }: { recordId?: string }) => <p>{recordId ? `Record ${recordId}` : 'Contact list'}</p> }));
afterEach(cleanup);
const uuid = 'ABCDEF01-0000-4000-8000-000000000001';
async function mount(path: string) {
  const history = createMemoryHistory({ initialEntries: [path] });
  const router = createAppRouter(history);
  render(<RouterProvider router={router} />);
  return { history, router };
}
it('renders platform and contact deep links including uppercase UUID', async () => {
  await mount('/');
  expect(await screen.findByRole('link', { name: 'Platform' })).toBeTruthy();
  cleanup();
  await mount('/app/contacts');
  expect(await screen.findByText('Contact list')).toBeTruthy();
  cleanup();
  await mount('/app/contacts/' + uuid);
  expect(await screen.findByText(`Record ${uuid}`)).toBeTruthy();
});
it.each(['/app/contacts/invalid', '/app/contacts/a%2Fb', '/app/contacts/ABCDEF01-0000-4000-8000-000000000001/extra', '/unknown', '/api/contacts'])('renders a safe not found page for %s', async path => {
  await mount(path);
  expect(await screen.findByText('Không tìm thấy trang.')).toBeTruthy();
  expect(screen.queryByText('Contact list')).toBeNull();
});

it('navigates with browser history and leaves modified link clicks to the browser', async () => {
  const { history } = await mount('/');
  const link = await screen.findByRole('link', { name: 'Liên hệ' });
  for (const event of [new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true }), new MouseEvent('click', { bubbles: true, cancelable: true, metaKey: true }), new MouseEvent('click', { bubbles: true, cancelable: true, button: 1 })]) {
    const stop = (e: Event) => { expect(e.defaultPrevented).toBe(false); e.preventDefault(); };
    document.addEventListener('click', stop, { once: true });
    fireEvent(link, event);
    expect(history.location.pathname).toBe('/');
  }
  fireEvent.click(link);
  expect(await screen.findByText('Contact list')).toBeTruthy();
  act(() => history.back());
  await waitFor(() => expect(history.location.pathname).toBe('/'));
  act(() => history.forward());
  expect(await screen.findByText('Contact list')).toBeTruthy();
});
it('keeps the already-clean callback path clean without redirecting', async () => {
  const { history } = await mount('/auth/callback');
  await screen.findByRole('link', { name: 'Platform' });
  expect(history.location.href).toBe('/auth/callback');
  expect(screen.queryByText('Contact list')).toBeNull();
  expect(Array.from(document.querySelectorAll('a')).every(link => !link.href.includes('code=') && !link.href.includes('state='))).toBe(true);
});
