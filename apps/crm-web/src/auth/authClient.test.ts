import { beforeAll, describe, expect, it, vi } from 'vitest';
import { AuthClient } from './authClient.ts';
import type { OidcConfig } from './oidcConfig.ts';

const config: OidcConfig = { issuer: 'https://id.example/realm', clientId: 'web', callbackUrl: 'https://app.example/auth/callback', logoutUrl: 'https://app.example/', allowLocalHttp: false };
let keys: CryptoKeyPair;
let jwk: JsonWebKey & { kid: string; alg: string; use: string };
const enc = (value: string) => Buffer.from(value).toString('base64url');
beforeAll(async () => {
  keys = await crypto.subtle.generateKey({ name: 'RSASSA-PKCS1-v1_5', modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256' }, true, ['sign', 'verify']);
  jwk = { ...(await crypto.subtle.exportKey('jwk', keys.publicKey)), kid: 'test', alg: 'RS256', use: 'sig' };
});

function fixture() {
  const storage = new Map<string, string>();
  let location = '';
  let cleaned = '';
  let nonce = '';
  let state = '';
  let claimOverrides: Record<string, unknown> = {};
  let badSignature = false;
  const fetcher: typeof fetch = async (input, init) => {
    const url = String(input);
    if (url.endsWith('/.well-known/openid-configuration')) return Response.json({ issuer: config.issuer, authorization_endpoint: `${config.issuer}/authorize`, token_endpoint: `${config.issuer}/token`, jwks_uri: `${config.issuer}/keys`, end_session_endpoint: `${config.issuer}/logout`, id_token_signing_alg_values_supported: ['RS256'] });
    if (url.endsWith('/keys')) return Response.json({ keys: [jwk] });
    if (url.endsWith('/token')) {
      expect(String(init?.body)).toContain('code_verifier=');
      const now = Math.floor(Date.now() / 1000);
      const payload = { iss: config.issuer, aud: config.clientId, sub: 'subject', iat: now, exp: now + 300, nonce, ...claimOverrides };
      const data = `${enc(JSON.stringify({ alg: 'RS256', kid: 'test' }))}.${enc(JSON.stringify(payload))}`;
      const signature = await crypto.subtle.sign('RSASSA-PKCS1-v1_5', keys.privateKey, new TextEncoder().encode(data));
      const jwt = `${data}.${badSignature ? enc('bad') : Buffer.from(signature).toString('base64url')}`;
      return Response.json({ access_token: 'access-sentinel', token_type: 'Bearer', expires_in: 300, id_token: jwt });
    }
    throw new Error('unexpected URL');
  };
  const env = {
    storage: { getItem: (k: string) => storage.get(k) ?? null, setItem: (k: string, v: string) => { storage.set(k, v); }, removeItem: (k: string) => { storage.delete(k); } },
    navigate: (url: string) => { location = url; }, cleanUrl: (url: string) => { cleaned = url; },
    logout: async () => {}, fetch: fetcher
  };
  const client = new AuthClient(config, env);
  async function login() { await client.login(); const url = new URL(location); nonce = url.searchParams.get('nonce')!; state = url.searchParams.get('state')!; return url; }
  return { client, env, storage, login, callback: () => `${config.callbackUrl}?code=code&state=${encodeURIComponent(state)}`, clean: () => cleaned, claims: (c: Record<string, unknown>) => { claimOverrides = c; }, badSignature: () => { badSignature = true; } };
}

describe('OIDC browser session', () => {
  it('starts without a session; uses S256, single-use state and memory only', async () => {
    const f = fixture(); expect(f.client.getAccessToken()).toBeNull();
    const url = await f.login(); expect(url.searchParams.get('code_challenge_method')).toBe('S256');
    expect(url.searchParams.get('nonce')).toBeTruthy();
    const transaction = JSON.parse([...f.storage.values()][0]!) as { verifier: string };
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(transaction.verifier));
    expect(url.searchParams.get('code_challenge')).toBe(Buffer.from(digest).toString('base64url'));
    await f.client.completeCallback(f.callback());
    expect(f.clean()).toBe(config.callbackUrl); expect(f.client.getAccessToken()).toBe('access-sentinel');
    expect(JSON.stringify([...f.storage])).not.toContain('access-sentinel');
    expect(new AuthClient(config, f.env).getAccessToken()).toBeNull();
    await expect(f.client.completeCallback(f.callback())).rejects.toThrow(/OIDC_CALLBACK_FAILED/);
  });
  it.each(['missing', 'wrong', 'duplicate', 'redirect', 'token'])('rejects %s callback', async (kind) => {
    const f = fixture(); await f.login(); const url = new URL(f.callback());
    if (kind === 'missing') url.searchParams.delete('state');
    if (kind === 'wrong') url.searchParams.set('state', 'wrong');
    if (kind === 'duplicate') url.searchParams.append('state', 'wrong');
    if (kind === 'redirect') url.pathname = '/wrong';
    if (kind === 'token') url.searchParams.set('access_token', 'sentinel');
    await expect(f.client.completeCallback(url.href)).rejects.toThrow(/OIDC_CALLBACK_FAILED/);
    expect(f.client.getAccessToken()).toBeNull(); expect(f.clean()).toBe(config.callbackUrl);
  });
  it.each([{ iss: 'https://wrong.example' }, { aud: 'wrong' }, { exp: 1 }, { nonce: 'wrong' }])('rejects invalid signed claims', async (claims) => {
    const f = fixture(); await f.login(); f.claims(claims);
    await expect(f.client.completeCallback(f.callback())).rejects.toThrow(/OIDC_CALLBACK_FAILED/); expect(f.client.getAccessToken()).toBeNull();
  });
  it('notifies subscribers when the session expires without an API call', async () => {
    const f = fixture(); await f.login(); vi.useFakeTimers();
    try { await f.client.completeCallback(f.callback()); vi.advanceTimersByTime(301_000); expect(f.client.getSnapshot().status).toBe('anonymous'); } finally { vi.useRealTimers(); }
  });
  it('expires its in-memory session', async () => {
    const f = fixture(); await f.login(); await f.client.completeCallback(f.callback());
    vi.useFakeTimers(); try { vi.advanceTimersByTime(301_000); expect(f.client.getAccessToken()).toBeNull(); expect(f.client.getSnapshot().status).toBe('anonymous'); } finally { vi.useRealTimers(); }
  });
  it('rejects expired transaction state', async () => {
    const f = fixture(); await f.login(); vi.useFakeTimers();
    try { vi.advanceTimersByTime(301_000); await expect(f.client.completeCallback(f.callback())).rejects.toThrow(/OIDC_CALLBACK_FAILED/); } finally { vi.useRealTimers(); }
  });
  it('rejects a reentrant callback without creating a second session', async () => {
    const f = fixture(); await f.login(); const original = f.env.fetch;
    let release!: () => void; const barrier = new Promise<void>((resolve) => { release = resolve; });
    f.env.fetch = async (input, init) => { if (String(input).endsWith('/token')) await barrier; return original(input, init); };
    const first = f.client.completeCallback(f.callback()); await new Promise((resolve) => setTimeout(resolve, 20));
    await expect(f.client.completeCallback(f.callback())).rejects.toThrow(/OIDC_CALLBACK_FAILED/);
    release(); await first; expect(f.client.getAccessToken()).toBe('access-sentinel');
  });
  it('fails closed with a safe reason if transaction storage is unavailable', async () => {
    const f = fixture(); f.env.storage.getItem = () => { throw new Error('raw storage detail'); };
    await expect(f.client.completeCallback(f.callback())).rejects.toThrow(/^OIDC_CALLBACK_FAILED [a-f0-9-]+$/);
    expect(f.client.getAccessToken()).toBeNull();
  });
  it('surfaces discovery failures without leaking raw error data', async () => {
    const f = fixture(); f.env.fetch = async () => { throw new Error('secret-raw-provider'); };
    await expect(f.client.login()).rejects.toThrow(/^OIDC_LOGIN_FAILED [a-f0-9-]+$/);
    expect(JSON.stringify(f.client.getSnapshot())).not.toContain('secret-raw-provider');
  });
  it('rejects a bad signature', async () => { const f = fixture(); await f.login(); f.badSignature(); await expect(f.client.completeCallback(f.callback())).rejects.toThrow(/OIDC_CALLBACK_FAILED/); });
  it('does not restore a session after logout while a callback is pending', async () => {
    const f = fixture(); await f.login(); const original = f.env.fetch;
    let release!: () => void; const barrier = new Promise<void>((resolve) => { release = resolve; });
    f.env.fetch = async (input, init) => { if (String(input).endsWith('/token')) await barrier; return original(input, init); };
    const callback = f.client.completeCallback(f.callback());
    await new Promise((resolve) => setTimeout(resolve, 20));
    await expect(f.client.logout()).rejects.toThrow(/OIDC_LOGOUT_FAILED/);
    release(); await expect(callback).rejects.toThrow(/OIDC_CALLBACK_FAILED/);
    expect(f.client.getAccessToken()).toBeNull();
  });
  it('clears the local session if logout fails', async () => {
    const f = fixture(); await f.login(); await f.client.completeCallback(f.callback());
    f.env.logout = async () => { throw new Error('sensitive'); };
    await expect(f.client.logout()).rejects.toThrow(/OIDC_LOGOUT_FAILED/); expect(f.client.getAccessToken()).toBeNull();
  });
});
