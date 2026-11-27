// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import { AuthProvider, useAuth } from './AuthProvider.tsx';
import { AuthClient } from './authClient.ts';

function View() { const auth = useAuth(); return <p>{auth.state.status}</p>; }
afterEach(cleanup);
it('exposes an anonymous session without exposing tokens to views', () => {
  const client = new AuthClient({ issuer: 'https://id.example', clientId: 'web', callbackUrl: 'https://app.example/auth/callback', logoutUrl: 'https://app.example/', allowLocalHttp: false });
  render(<AuthProvider client={client}><View /></AuthProvider>);
  expect(screen.getByText('anonymous')).toBeTruthy();
});
it('completes a captured callback only once under StrictMode', async () => {
  const { StrictMode } = await import('react');
  const { vi } = await import('vitest');
  const client = new AuthClient({ issuer: 'https://id.example', clientId: 'web', callbackUrl: 'https://app.example/auth/callback', logoutUrl: 'https://app.example/', allowLocalHttp: false });
  const complete = vi.spyOn(client, 'completeCallback').mockResolvedValue();
  render(<StrictMode><AuthProvider client={client} callbackUrl="https://app.example/auth/callback?code=synthetic&state=synthetic"><View /></AuthProvider></StrictMode>);
  await waitFor(() => expect(complete).toHaveBeenCalledTimes(1));
  expect(complete).toHaveBeenCalledWith("https://app.example/auth/callback?code=synthetic&state=synthetic");
});
