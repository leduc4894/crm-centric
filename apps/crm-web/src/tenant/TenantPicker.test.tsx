// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { TenantPicker } from './TenantPicker';
import { AuthProvider } from '../auth/AuthProvider';
import { TenantProvider } from './TenantProvider';
import type { AuthClient } from '../auth/authClient';
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
it('shows no tenants when an authenticated account has an empty page', async () => {
  const client = { getSnapshot: () => ({ status: 'authenticated', issuer: 'https://issuer/', subject: 'a' }), subscribe: () => () => undefined, getAccessToken: () => 'synthetic', login: async () => undefined, logout: async () => undefined } as unknown as AuthClient;
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ items: [], nextCursor: null }))));
  render(<AuthProvider client={client}><TenantProvider><TenantPicker /></TenantProvider></AuthProvider>);
  expect(await screen.findByText(/no tenants/i)).toBeTruthy();
});
