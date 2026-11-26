import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { AuthClient, type AuthState } from './authClient.ts';
import { parseOidcConfig } from './oidcConfig.ts';

interface AuthContextValue { state: AuthState; login(): Promise<void>; logout(): Promise<void> }
const Context = createContext<AuthContextValue | null>(null);
const ClientContext = createContext<AuthClient | null>(null);
export function useAuthClient(): AuthClient | null { return useContext(ClientContext); }
export function useAuth(): AuthContextValue {
  const value = useContext(Context); if (!value) throw new Error('OIDC_CONTEXT_MISSING'); return value;
}
export function AuthProvider({ client, children, callbackUrl }: { client: AuthClient; children: ReactNode; callbackUrl?: string | undefined }) {
  const started = useRef(false);
  const [state, setState] = useState<AuthState>(() => client.getSnapshot());
  useEffect(() => client.subscribe(setState), [client]);
  useEffect(() => {
    if (callbackUrl && !started.current) { started.current = true; void client.completeCallback(callbackUrl).catch(() => undefined); }
  }, [client, callbackUrl]);
  return <ClientContext.Provider value={client}><Context.Provider value={{ state, login: () => client.login(), logout: () => client.logout() }}>{children}</Context.Provider></ClientContext.Provider>;
}
export function ConfiguredAuthProvider({ children, callbackUrl }: { children: ReactNode; callbackUrl?: string | undefined }) {
  const [result] = useState(() => {
    try { return { client: new AuthClient(parseOidcConfig(import.meta.env, window.location.origin)) }; }
    catch { return { error: { status: 'error', reasonToken: 'OIDC_CONFIG_INVALID', correlationId: crypto.randomUUID() } as AuthState }; }
  });
  if ('client' in result && result.client) return <AuthProvider client={result.client} callbackUrl={callbackUrl}>{children}</AuthProvider>;
  return <Context.Provider value={{ state: result.error!, login: async () => { throw new Error(`OIDC_CONFIG_INVALID ${crypto.randomUUID()}`); }, logout: async () => { throw new Error(`OIDC_CONFIG_INVALID ${crypto.randomUUID()}`); } }}>{children}</Context.Provider>;
}
