import { createContext, useCallback, useContext, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { useAuth, useAuthClient } from '../auth/AuthProvider.tsx';
import { listTenants, TenantError, type Tenant } from './tenantClient.ts';

type Status = 'anonymous' | 'loading' | 'ready' | 'error';
interface Snapshot { identity: string | null; status: Status; items: Tenant[]; nextCursor: string | null; selectedTenantId: string | null; reasonToken: string | null; correlationId: string | null }
interface TenantContextValue extends Snapshot { selectTenant(id: string): void; loadMore(): Promise<void> }
const Context = createContext<TenantContextValue | null>(null);
const empty = (identity: string | null, status: Status): Snapshot => ({ identity, status, items: [], nextCursor: null, selectedTenantId: null, reasonToken: null, correlationId: null });
export function useTenant(): TenantContextValue { const value = useContext(Context); if (!value) throw new Error(`WEB_TENANT_CONTEXT_MISSING ${crypto.randomUUID()}`); return value; }
export function TenantProvider({ children }: { children: ReactNode }) {
  const auth = useAuth(); const client = useAuthClient();
  const identity = auth.state.status === 'authenticated' ? JSON.stringify([auth.state.issuer, auth.state.subject]) : null;
  const [snapshot, setSnapshot] = useState<Snapshot>(() => empty(null, 'anonymous'));
  const generation = useRef(0); const activeRequest = useRef<AbortController | null>(null); const currentIdentity = useRef(identity); const seenCursors = useRef(new Set<string>());
  useLayoutEffect(() => { currentIdentity.current = identity; }, [identity]);
  const request = useCallback(async (owner: string, cursor?: string) => {
    if (!client || activeRequest.current) return;
    const token = client.getAccessToken(); if (!token) return;
    const current = generation.current; const controller = new AbortController(); activeRequest.current = controller;
    setSnapshot((old) => ({ ...(old.identity === owner ? old : empty(owner, 'loading')), status: 'loading', reasonToken: null, correlationId: null }));
    try {
      if (cursor !== undefined) seenCursors.current.add(cursor);
      const result = await listTenants(token, cursor, controller.signal);
      if (result.nextCursor && seenCursors.current.has(result.nextCursor)) throw new TenantError('WEB_TENANT_CONTRACT_INVALID');
      if (generation.current !== current || currentIdentity.current !== owner || controller.signal.aborted) return;
      setSnapshot((old) => ({ ...old, identity: owner, status: 'ready', items: cursor === undefined ? result.items : [...old.items, ...result.items], nextCursor: result.nextCursor }));
    } catch (error) {
      if (generation.current !== current || currentIdentity.current !== owner || controller.signal.aborted) return;
      const failure = error instanceof TenantError ? error : new TenantError('WEB_TENANT_NETWORK_ERROR');
      if (failure.status === 401) { client.invalidate(); return; }
      setSnapshot((old) => ({ ...old, status: 'error', selectedTenantId: null, reasonToken: failure.reasonToken, correlationId: failure.correlationId }));
    } finally { if (activeRequest.current === controller) activeRequest.current = null; }
  }, [client]);
  useEffect(() => {
    const counter = generation;
    counter.current++; activeRequest.current?.abort(); activeRequest.current = null; seenCursors.current.clear();
    const started = counter.current;
    if (identity) queueMicrotask(() => { if (counter.current === started) void request(identity); });
    return () => { counter.current++; activeRequest.current?.abort(); activeRequest.current = null; };
  }, [identity, request]);
  const safe = snapshot.identity === identity ? snapshot : empty(identity, identity ? 'loading' : 'anonymous');
  const selectTenant = (id: string) => { setSnapshot((old) => old.identity === currentIdentity.current && old.items.some((item) => item.id === id && item.status === 'active') ? { ...old, selectedTenantId: id } : old); };
  const loadMore = async () => { if (identity && safe.nextCursor && safe.status === 'ready') await request(identity, safe.nextCursor); };
  return <Context.Provider value={{ ...safe, selectTenant, loadMore }}>{children}</Context.Provider>;
}
