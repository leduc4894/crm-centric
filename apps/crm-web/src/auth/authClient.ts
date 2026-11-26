import * as oauth from 'oauth4webapi';
import type { OidcConfig } from './oidcConfig.ts';

export type AuthState = { status: 'anonymous' } | { status: 'authenticated'; issuer: string; subject: string } | { status: 'error'; reasonToken: string; correlationId: string };
type StorageLike = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;
export interface AuthEnvironment {
  storage: StorageLike;
  fetch: typeof fetch;
  navigate(url: string): void;
  cleanUrl(url: string): void;
  logout(url: string, fields: Record<string, string>): Promise<void>;
}
const transactionKey = 'crm.oidc.transaction';
const lifetimeMs = 5 * 60 * 1000;
const defaults = (): AuthEnvironment => ({
  storage: window.sessionStorage,
  fetch: globalThis.fetch.bind(globalThis),
  navigate: (url) => window.location.assign(url),
  cleanUrl: (url) => window.history.replaceState(null, '', url),
  logout: async (url, fields) => {
    const form = document.createElement('form'); form.method = 'post'; form.action = url;
    form.referrerPolicy = 'no-referrer';
    for (const [name, value] of Object.entries(fields)) { const input = document.createElement('input'); input.type = 'hidden'; input.name = name; input.value = value; form.append(input); }
    document.body.append(form); form.submit(); form.remove();
  }
});
interface Transaction { state: string; nonce: string; verifier: string; created: number }

export class AuthClient {
  private state: AuthState = { status: 'anonymous' };
  private accessToken: string | null = null;
  private idToken: string | null = null;
  private expires = 0;
  private expiryTimer: ReturnType<typeof setTimeout> | undefined;
  private busy = false;
  private generation = 0;
  private listeners = new Set<(state: AuthState) => void>();
  private env: AuthEnvironment;
  constructor(private config: OidcConfig, environment?: AuthEnvironment) { this.env = environment ?? defaults(); }
  getSnapshot = (): AuthState => this.state;
  subscribe = (listener: (state: AuthState) => void): (() => void) => { this.listeners.add(listener); return () => this.listeners.delete(listener); };
  private setState(state: AuthState) { this.state = state; for (const listener of this.listeners) listener(state); }
  private clear() { if (this.expiryTimer !== undefined) clearTimeout(this.expiryTimer); this.expiryTimer = undefined; this.accessToken = null; this.idToken = null; this.expires = 0; this.setState({ status: 'anonymous' }); }
  private scheduleExpiry() { this.expiryTimer = setTimeout(() => { if (Date.now() >= this.expires) this.clear(); else this.scheduleExpiry(); }, Math.min(Math.max(0, this.expires - Date.now()), 2147483647)); }
  private fail(reasonToken: string): Error { const correlationId = crypto.randomUUID(); this.setState({ status: 'error', reasonToken, correlationId }); return new Error(`${reasonToken} ${correlationId}`); }
  invalidate(): void { this.generation++; this.clear(); this.setState({ status: 'error', reasonToken: 'WEB_TENANT_UNAUTHORIZED', correlationId: crypto.randomUUID() }); }
  getAccessToken(): string | null { if (this.accessToken && Date.now() >= this.expires) this.clear(); return this.accessToken; }
  private options() { return { [oauth.customFetch]: (input: string, init: unknown) => this.env.fetch(input, init as RequestInit), [oauth.allowInsecureRequests]: this.config.allowLocalHttp, signal: AbortSignal.timeout(10000) }; }
  private async metadata() {
    const as = await oauth.processDiscoveryResponse(new URL(this.config.issuer), await oauth.discoveryRequest(new URL(this.config.issuer), this.options()));
    for (const endpoint of [as.authorization_endpoint, as.token_endpoint, as.jwks_uri, as.end_session_endpoint]) {
      if (endpoint === undefined) continue;
      const url = new URL(endpoint);
      if (url.username || url.password || url.hash || (url.protocol !== 'https:' && !(this.config.allowLocalHttp && url.protocol === 'http:' && url.hostname === 'localhost'))) throw new Error();
    }
    return as;
  }
  async login(): Promise<void> {
    const generation = ++this.generation;
    try {
      this.clear(); this.env.storage.removeItem(transactionKey);
      const as = await this.metadata();
      if (generation !== this.generation || !as.authorization_endpoint) throw new Error();
      const tx: Transaction = { state: oauth.generateRandomState(), nonce: oauth.generateRandomNonce(), verifier: oauth.generateRandomCodeVerifier(), created: Date.now() };
      const challenge = await oauth.calculatePKCECodeChallenge(tx.verifier);
      if (generation !== this.generation) throw new Error();
      const url = new URL(as.authorization_endpoint);
      url.search = new URLSearchParams({ client_id: this.config.clientId, redirect_uri: this.config.callbackUrl, response_type: 'code', scope: 'openid', state: tx.state, nonce: tx.nonce, code_challenge: challenge, code_challenge_method: 'S256' }).toString();
      this.env.storage.setItem(transactionKey, JSON.stringify(tx)); this.env.navigate(url.href);
    } catch { if (generation !== this.generation) throw new Error(`OIDC_LOGIN_FAILED ${crypto.randomUUID()}`); try { this.env.storage.removeItem(transactionKey); } catch { /* storage is unavailable; the session remains unauthenticated */ } throw this.fail('OIDC_LOGIN_FAILED'); }
  }
  async completeCallback(callbackUrl: string): Promise<void> {
    let raw: string | null;
    try { this.env.cleanUrl(this.config.callbackUrl); raw = this.env.storage.getItem(transactionKey); this.env.storage.removeItem(transactionKey); }
    catch { this.clear(); throw this.fail('OIDC_CALLBACK_FAILED'); }
    if (this.busy) throw this.fail('OIDC_CALLBACK_FAILED');
    this.busy = true;
    const generation = ++this.generation;
    try {
      this.clear(); const url = new URL(callbackUrl); const expected = new URL(this.config.callbackUrl);
      if (url.origin !== expected.origin || url.pathname !== expected.pathname || url.hash || url.username || url.password || url.searchParams.getAll('state').length !== 1 || url.searchParams.getAll('code').length !== 1 || ['access_token', 'id_token', 'refresh_token'].some((key) => url.searchParams.has(key)) || !raw) throw new Error();
      const tx = JSON.parse(raw) as Transaction;
      if (typeof tx.state !== 'string' || typeof tx.nonce !== 'string' || typeof tx.verifier !== 'string' || typeof tx.created !== 'number' || Date.now() - tx.created > lifetimeMs || tx.created > Date.now()) throw new Error();
      const as = await this.metadata(); const client = { client_id: this.config.clientId };
      const params = oauth.validateAuthResponse(as, client, url.searchParams, tx.state);
      const response = await oauth.authorizationCodeGrantRequest(as, client, oauth.None(), params, this.config.callbackUrl, tx.verifier, this.options());
      const tokens = await oauth.processAuthorizationCodeResponse(as, client, response, { expectedNonce: tx.nonce, requireIdToken: true });
      await oauth.validateApplicationLevelSignature(as, response, this.options());
      const claims = oauth.getValidatedIdTokenClaims(tokens);
      if (generation !== this.generation || !claims || !tokens.id_token || !tokens.access_token || !tokens.expires_in || tokens.expires_in <= 0) throw new Error();
      this.accessToken = tokens.access_token; this.idToken = tokens.id_token;
      this.expires = Math.min(Date.now() + tokens.expires_in * 1000, claims.exp * 1000);
      this.setState({ status: 'authenticated', issuer: claims.iss, subject: claims.sub }); this.scheduleExpiry();
    } catch { if (generation !== this.generation) throw new Error(`OIDC_CALLBACK_FAILED ${crypto.randomUUID()}`); this.clear(); throw this.fail('OIDC_CALLBACK_FAILED'); }
    finally { this.busy = false; }
  }
  async logout(): Promise<void> {
    const generation = ++this.generation;
    const idToken = this.idToken; this.clear();
    try {
      this.env.storage.removeItem(transactionKey);
      const as = await this.metadata(); if (generation !== this.generation || !as.end_session_endpoint || !idToken) throw new Error();
      await this.env.logout(as.end_session_endpoint, { id_token_hint: idToken, post_logout_redirect_uri: this.config.logoutUrl, client_id: this.config.clientId });
    } catch { if (generation !== this.generation) throw new Error(`OIDC_LOGOUT_FAILED ${crypto.randomUUID()}`); throw this.fail('OIDC_LOGOUT_FAILED'); }
  }
}
