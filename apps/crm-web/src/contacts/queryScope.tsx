import { createContext, useContext, useLayoutEffect, useState, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

type Scope = { client: QueryClient; scopeId: string; isCurrent(): boolean };
const Context = createContext<Scope | null>(null);
export function useContactQueryScope(): Scope {
  const scope = useContext(Context);
  if (!scope) throw new Error(`WEB_CRM_QUERY_SCOPE_MISSING ${crypto.randomUUID()}`);
  return scope;
}
function Instance({ children }: { children: ReactNode }) {
  const [scope] = useState(() => {
    let active = true;
    const client = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false, refetchOnReconnect: false }, mutations: { retry: false } } });
    return { client, scopeId: crypto.randomUUID(), isCurrent: () => active, open: () => { active = true; }, close: () => { active = false; void client.cancelQueries(); client.clear(); } };
  });
  useLayoutEffect(() => { scope.open(); return () => scope.close(); }, [scope]);
  return <Context.Provider value={scope}><QueryClientProvider client={scope.client}>{children}</QueryClientProvider></Context.Provider>;
}
export function ContactQueryScope({ identity, tenantId, generation, children }: { identity: string; tenantId: string; generation: number; children: ReactNode }) {
  return <Instance key={JSON.stringify([identity, tenantId, generation])}>{children}</Instance>;
}
