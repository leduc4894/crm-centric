import { useAuth } from '../auth/AuthProvider.tsx';
import { useTenant } from './TenantProvider.tsx';
export function TenantPicker() {
  const auth = useAuth(); const tenant = useTenant();
  if (auth.state.status === 'error') return <section aria-label="Account"><p role="alert">{auth.state.reasonToken} ({auth.state.correlationId})</p><button onClick={() => void auth.login().catch(() => undefined)}>Sign in</button></section>;
  if (auth.state.status === 'anonymous') return <section aria-label="Account"><button onClick={() => void auth.login().catch(() => undefined)}>Sign in</button></section>;
  return <section aria-label="Account"><button onClick={() => void auth.logout().catch(() => undefined)}>Sign out</button><h2>Choose a tenant</h2>
    {tenant.status === 'loading' && <p role="status">Loading tenants…</p>}
    {tenant.status === 'error' && <><p role="alert">{tenant.reasonToken} ({tenant.correlationId})</p><button onClick={() => void tenant.retry()}>Retry</button></>}
    {tenant.status === 'ready' && tenant.items.length === 0 && <p>No tenants available.</p>}
    <ul>{tenant.items.map((item) => <li key={item.id}><button disabled={item.status !== 'active'} aria-pressed={item.id === tenant.selectedTenantId} onClick={() => tenant.selectTenant(item.id)}>{item.name}{item.status === 'suspended' ? ' (suspended)' : ''}</button></li>)}</ul>
    {tenant.nextCursor && <button disabled={tenant.status !== 'ready'} onClick={() => void tenant.loadMore()}>Load more tenants</button>}
  </section>;
}
