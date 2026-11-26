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
it('retries a tenant-list failure when the user requests it', async () => {
  const client = { getSnapshot: () => ({ status: 'authenticated', issuer: 'https://issuer/', subject: 'a' }), subscribe: () => () => undefined, getAccessToken: () => 'synthetic', login: async () => undefined, logout: async () => undefined } as unknown as AuthClient;
  let attempt = 0;
  vi.stubGlobal('fetch', vi.fn(async () => ++attempt === 1 ? new Response('', { status: 503 }) : new Response(JSON.stringify({ items: [], nextCursor: null }))));
  render(<AuthProvider client={client}><TenantProvider><TenantPicker /></TenantProvider></AuthProvider>);
  expect(await screen.findByRole('alert')).toBeTruthy();
  screen.getByRole('button', { name: /retry/i }).click();
  expect(await screen.findByText(/no tenants/i)).toBeTruthy();
});
it('retries a failed next page without discarding earlier tenants', async () => {
  const client = { getSnapshot: () => ({ status: 'authenticated', issuer: 'https://issuer/', subject: 'a' }), subscribe: () => () => undefined, getAccessToken: () => 'synthetic', login: async () => undefined, logout: async () => undefined } as unknown as AuthClient;
  let attempt = 0;
  vi.stubGlobal('fetch', vi.fn(async () => {
    attempt++;
    if (attempt === 1) return new Response(JSON.stringify({ items: [{ id: '11111111-1111-4111-8111-111111111111', name: 'Tenant A', status: 'active' }], nextCursor: 'page-2' }));
    if (attempt === 2) return new Response('', { status: 503 });
    return new Response(JSON.stringify({ items: [{ id: '22222222-2222-4222-8222-222222222222', name: 'Tenant B', status: 'active' }], nextCursor: null }));
  }));
  render(<AuthProvider client={client}><TenantProvider><TenantPicker /></TenantProvider></AuthProvider>);
  await screen.findByRole('button', { name: 'Tenant A' });
  screen.getByRole('button', { name: 'Load more tenants' }).click();
  await screen.findByRole('alert'); screen.getByRole('button', { name: /retry/i }).click();
  expect(await screen.findByRole('button', { name: 'Tenant B' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Tenant A' })).toBeTruthy();
});
