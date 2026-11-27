import { useLayoutEffect, useState, useSyncExternalStore, type MouseEvent } from 'react';
import { useAuthClient } from '../auth/AuthProvider';
import { useTenant } from '../tenant/TenantProvider';
import { contactIdPattern, createCrmClient, CrmError } from './crmClient';
import type { components } from '@crm-centric/crm-client';

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
type Contact = components['schemas']['Contact'];
type View = { kind: 'loading' } | { kind: 'error'; message: string } | { kind: 'list'; items: Contact[] } | { kind: 'record'; contact: Contact };
function ContactRoute({ identity, tenantId, recordId }: { identity: string; tenantId: string; recordId: string | null }) {
  const auth = useAuthClient();
  const [view, setView] = useState<View>({ kind: 'loading' });
  useLayoutEffect(() => {
    const token = auth?.getAccessToken();
    if (!auth || !token) return;
    const controller = new AbortController();
    const current = () => {
      const state = auth.getSnapshot();
      return !controller.signal.aborted && state.status === 'authenticated' &&
        JSON.stringify([state.issuer, state.subject]) === identity && auth.getAccessToken() === token;
    };
    const client = createCrmClient({ origin: location.origin, tenantId, token, signal: controller.signal, current, invalidate: () => auth.invalidate() });
    async function load() {
      try {
        const result: View = recordId
          ? { kind: 'record', contact: (await client.getContact(recordId)).data }
          : { kind: 'list', items: (await client.listContacts()).data.items };
        if (current()) setView(result);
      } catch (error) {
        if (current()) setView({ kind: 'error', message: error instanceof CrmError ? error.message : new CrmError('WEB_CRM_NETWORK_ERROR').message });
      }
    }
    void load();
    return () => controller.abort();
  }, [auth, identity, tenantId, recordId]);
  return <section className="contact-content">
    {recordId && <a href={listPath} onClick={navigate}>Liên hệ</a>}
    <h2>{view.kind === 'record' ? view.contact.displayName : recordId ? 'Hồ sơ liên hệ' : 'Liên hệ'}</h2>
    {view.kind === 'loading' && <p role="status">Đang tải…</p>}
    {view.kind === 'error' && <p role="alert">{view.message}</p>}
    {view.kind === 'list' && <div className="contact-card">{view.items.length === 0 ? <p>Chưa có liên hệ.</p> :
      <ul>{view.items.map(item => <li key={item.id}><a href={listPath + '/' + item.id} onClick={navigate}>{item.displayName}</a></li>)}</ul>}</div>}
    {view.kind === 'record' && <section className="contact-card"><h3>Thông tin liên hệ</h3>
      <dl><dt>Email</dt><dd>{view.contact.email ?? '—'}</dd><dt>Điện thoại</dt><dd>{view.contact.phone ?? '—'}</dd></dl></section>}
  </section>;
}
export function ContactShell() {
  const { identity, selectedTenantId } = useTenant();
  const path = useSyncExternalStore(subscribe, () => location.pathname);
  const recordId = path.startsWith(listPath + '/') ? path.slice(listPath.length + 1) : null;
  if (path !== listPath && !(recordId && contactIdPattern.test(recordId))) return null;
  if (!identity || !selectedTenantId) return <p>Chọn tenant để xem liên hệ.</p>;
  return <ContactRoute key={JSON.stringify([identity, selectedTenantId, path])} identity={identity} tenantId={selectedTenantId} recordId={recordId} />;
}
