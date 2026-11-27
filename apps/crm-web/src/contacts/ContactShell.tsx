import { useLayoutEffect, useState, useSyncExternalStore, type MouseEvent } from 'react';
import { useAuthClient } from '../auth/AuthProvider';
import { useTenant } from '../tenant/TenantProvider';
import { contactIdPattern, createCrmClient, type CrmClient } from './crmClient';
import { ContactDetail } from './ContactDetail';
import { ContactList } from './ContactList';

const listPath = '/app/contacts';
function subscribe(change: () => void) {
  window.addEventListener('popstate', change);
  return () => window.removeEventListener('popstate', change);
}
export function navigate(event: MouseEvent<HTMLAnchorElement>) {
  if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
  event.preventDefault();
  history.pushState(null, '', event.currentTarget.pathname);
  window.dispatchEvent(new PopStateEvent('popstate'));
}
export function openContacts(id?: string) {
  history.pushState(null, '', id ? listPath + '/' + id : listPath);
  window.dispatchEvent(new PopStateEvent('popstate'));
}
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
export function ContactShell() {
  const { identity, selectedTenantId } = useTenant();
  const path = useSyncExternalStore(subscribe, () => location.pathname);
  const recordId = path.startsWith(listPath + '/') ? path.slice(listPath.length + 1) : null;
  if (path !== listPath && !(recordId && contactIdPattern.test(recordId))) return null;
  if (!identity || !selectedTenantId) return <p>Chọn tenant để xem liên hệ.</p>;
  return <ContactRoute key={JSON.stringify([identity, selectedTenantId, path])} identity={identity} tenantId={selectedTenantId} recordId={recordId} />;
}
