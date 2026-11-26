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

function fixture(allowLocalHttp = false) {
  const storage = new Map<string, string>();
  let location = '';
  let cleaned = '';
  let nonce = '';
  let state = '';
  let claimOverrides: Record<string, unknown> = {};
  let badSignature = false;
  let endpointOverrides: Record<string, string> = {};
  let challenge = '';
  let sawInvalidVerifier = false;
  const fetcher: typeof fetch = async (input, init) => {
    const url = String(input);
    if (url.endsWith('/.well-known/openid-configuration')) return Response.json({ issuer: config.issuer, authorization_endpoint: `${config.issuer}/authorize`, token_endpoint: `${config.issuer}/token`, jwks_uri: `${config.issuer}/keys`, end_session_endpoint: `${config.issuer}/logout`, id_token_signing_alg_values_supported: ['RS256'], ...endpointOverrides });
    if (url.endsWith('/keys')) return Response.json({ keys: [jwk] });
    if (url.endsWith('/token')) {
      const verifier = new URLSearchParams(String(init?.body)).get('code_verifier');
      const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier ?? ''));
      if (Buffer.from(digest).toString('base64url') !== challenge) { sawInvalidVerifier = true; return Response.json({ error: 'invalid_grant' }, { status: 400 }); }
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
  const client = new AuthClient({ ...config, allowLocalHttp }, env);
  async function login() { await client.login(); const url = new URL(location); nonce = url.searchParams.get('nonce')!; state = url.searchParams.get('state')!; challenge = url.searchParams.get('code_challenge')!; return url; }
  return { client, env, storage, login, callback: () => `${config.callbackUrl}?code=code&state=${encodeURIComponent(state)}`, clean: () => cleaned, claims: (c: Record<string, unknown>) => { claimOverrides = c; }, badSignature: () => { badSignature = true; }, endpoints: (value: Record<string, string>) => { endpointOverrides = value; }, invalidVerifier: () => sawInvalidVerifier };
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
  it('rejects a verifier different from the authorization challenge', async () => {
    const f = fixture(); await f.login(); const [key, raw] = [...f.storage.entries()][0]!;
    const tx = JSON.parse(raw) as { verifier: string }; tx.verifier = 'wrong-verifier'; f.storage.set(key, JSON.stringify(tx));
    await expect(f.client.completeCallback(f.callback())).rejects.toThrow(/OIDC_CALLBACK_FAILED/); expect(f.invalidVerifier()).toBe(true);
  });
  it('does not navigate or restore a transaction when logout interrupts login', async () => {
    const f = fixture(); const original = f.env.fetch; let release!: () => void;
    const barrier = new Promise<void>((resolve) => { release = resolve; }); let first = true;
    f.env.fetch = async (input, init) => { if (first) { first = false; await barrier; } return original(input, init); };
    const login = f.client.login(); await expect(f.client.logout()).rejects.toThrow(/OIDC_LOGOUT_FAILED/);
    release(); await expect(login).rejects.toThrow(/OIDC_LOGIN_FAILED/); expect(f.storage.size).toBe(0);
  });
  it('does not navigate if logout interrupts PKCE challenge generation', async () => {
    const f = fixture(); const original = crypto.subtle.digest.bind(crypto.subtle);
    let release!: () => void; let entered!: () => void;
    const barrier = new Promise<void>((resolve) => { release = resolve; });
    const started = new Promise<void>((resolve) => { entered = resolve; });
    const digest = vi.spyOn(crypto.subtle, 'digest').mockImplementation(async (...args) => { entered(); await barrier; return original(...args); });
    try {
      const login = f.client.login(); await started; await expect(f.client.logout()).rejects.toThrow(/OIDC_LOGOUT_FAILED/);
      release(); await expect(login).rejects.toThrow(/OIDC_LOGIN_FAILED/); expect(f.storage.size).toBe(0);
    } finally { digest.mockRestore(); }
  });
  it('keeps the newer login transaction when an earlier login resumes', async () => {
    const f = fixture(); const original = f.env.fetch; let release!: () => void;
    const barrier = new Promise<void>((resolve) => { release = resolve; }); let first = true;
    f.env.fetch = async (input, init) => { if (first) { first = false; await barrier; } return original(input, init); };
    const older = f.client.login(); await f.login(); const current = [...f.storage.values()][0];
    release(); await expect(older).rejects.toThrow(/OIDC_LOGIN_FAILED/); expect([...f.storage.values()][0]).toBe(current);
    await f.client.completeCallback(f.callback()); expect(f.client.getAccessToken()).toBe('access-sentinel');
  });
  it.each(['authorization_endpoint', 'token_endpoint', 'jwks_uri', 'end_session_endpoint'])('rejects unsafe HTTP metadata %s', async (key) => {
    const f = fixture(true); f.endpoints({ [key]: 'http://attacker.example/endpoint' });
    await expect(f.client.login()).rejects.toThrow(/OIDC_LOGIN_FAILED/); expect(f.storage.size).toBe(0);
  });
  it('clears the local session if logout fails', async () => {
    const f = fixture(); await f.login(); await f.client.completeCallback(f.callback());
    f.env.logout = async () => { throw new Error('sensitive'); };
    await expect(f.client.logout()).rejects.toThrow(/OIDC_LOGOUT_FAILED/); expect(f.client.getAccessToken()).toBeNull();
  });
});
