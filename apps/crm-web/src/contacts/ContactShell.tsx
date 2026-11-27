import { useLayoutEffect, useState } from 'react';
import { useAuth, useAuthClient } from '../auth/AuthProvider';
import { ContactQueryScope } from './queryScope';
import { useTenant } from '../tenant/TenantProvider';
import { createCrmClient, type CrmClient } from './crmClient';
import { ContactDetail } from './ContactDetail';
import { ContactList } from './ContactList';

function ContactRoute({ identity, tenantId, recordId }: { identity: string; tenantId: string; recordId: string | null }) {
  const auth = useAuthClient();
  const [client, setClient] = useState<CrmClient | null>(null);
  useLayoutEffect(() => {
    const token = auth?.getAccessToken();
    if (!auth || !token) return;
    const controller = new AbortController();
    const current = () => {
      const state = auth.getSnapshot();
      return !controller.signal.aborted && state.status === 'authenticated' &&
        JSON.stringify([state.issuer, state.subject]) === identity && auth.getAccessToken() === token;
    };
    queueMicrotask(() => {
      if (current()) setClient(createCrmClient({ origin: location.origin, tenantId, token, signal: controller.signal, current, invalidate: () => auth.invalidate() }));
    });
    return () => controller.abort();
  }, [auth, identity, tenantId]);
  return <section className="contact-content">{!client ? <p role="status">Đang tải…</p> : (recordId
    ? <ContactDetail client={client} id={recordId} />
    : <ContactList client={client} />)}</section>;
}
export function ContactShell({ recordId = null }: { recordId?: string | null }) {
  const { identity, selectedTenantId } = useTenant();
  const { state } = useAuth();
  const [session, setSession] = useState({ state, generation: 0 });
  if (session.state !== state) setSession({ state, generation: session.generation + 1 });
  if (!identity || !selectedTenantId) return <p>Chọn tenant để xem liên hệ.</p>;
  return <ContactQueryScope identity={identity} tenantId={selectedTenantId} generation={session.generation}><ContactRoute key={JSON.stringify([identity, selectedTenantId, recordId, session.generation])} identity={identity} tenantId={selectedTenantId} recordId={recordId} /></ContactQueryScope>;
}
