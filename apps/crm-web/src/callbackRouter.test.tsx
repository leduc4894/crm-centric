// @vitest-environment jsdom
import { readFileSync } from 'node:fs';
import { StrictMode } from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { RouterProvider } from '@tanstack/react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { AuthProvider } from './auth/AuthProvider';
import { AuthClient } from './auth/authClient';
import { createAppRouter } from './router';

vi.mock('./tenant/TenantProvider', () => ({ TenantProvider: ({ children }: { children: React.ReactNode }) => <>{children}</> }));
vi.mock('./tenant/TenantPicker', () => ({ TenantPicker: () => null }));
vi.mock('./health/HealthStatus', () => ({ HealthStatus: () => null }));
vi.mock('./contacts/ContactShell', () => ({ ContactShell: () => null }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); delete window.__crmOidcCallback; });

it('cleans the real callback prelude before routing and delivers the original exactly once under StrictMode', async () => {
  const original = `${location.origin}/auth/callback?code=synthetic-code-sentinel&state=synthetic-state-sentinel`;
  history.replaceState(null, '', original);
  const push = vi.spyOn(history, 'pushState');
  const replace = vi.spyOn(history, 'replaceState');
  const logs = [vi.spyOn(console, 'log'), vi.spyOn(console, 'warn'), vi.spyOn(console, 'error')];
  vi.spyOn(window, 'scrollTo').mockImplementation(() => {});
  const html = new DOMParser().parseFromString(readFileSync('index.html', 'utf8'), 'text/html');
  expect(html.querySelector('meta[name="referrer"]')?.getAttribute('content')).toBe('no-referrer');
  const prelude = html.querySelector('head script:not([src])')?.textContent;
  expect(prelude).toBeTruthy();
  window.eval(prelude!);
  expect(location.pathname + location.search + location.hash).toBe('/auth/callback');
  const captured = window.__crmOidcCallback;
  delete window.__crmOidcCallback;
  const client = new AuthClient({ issuer: 'https://id.example', clientId: 'web', callbackUrl: `${location.origin}/auth/callback`, logoutUrl: `${location.origin}/`, allowLocalHttp: false });
  const complete = vi.spyOn(client, 'completeCallback').mockResolvedValue();
  const router = createAppRouter();
  render(<StrictMode><AuthProvider client={client} callbackUrl={captured}><RouterProvider router={router} /></AuthProvider></StrictMode>);
  await waitFor(() => expect(complete).toHaveBeenCalledTimes(1));
  expect(complete).toHaveBeenCalledWith(original);
  await screen.findByRole('link', { name: 'Platform' });
  expect(location.pathname + location.search + location.hash).toBe('/auth/callback');
  const exposed = JSON.stringify({ history: [...push.mock.calls, ...replace.mock.calls], state: history.state, links: Array.from(document.querySelectorAll('a'), a => a.href), logs: logs.flatMap(log => log.mock.calls) });
  expect(exposed).not.toContain('synthetic-code-sentinel');
  expect(exposed).not.toContain('synthetic-state-sentinel');
});
