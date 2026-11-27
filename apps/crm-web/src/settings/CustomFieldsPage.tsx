import { useLayoutEffect, useState, type FormEvent } from 'react';
import { useInfiniteQuery, type InfiniteData } from '@tanstack/react-query';
import { useAuth, useAuthClient } from '../auth/AuthProvider';
import { useTenant } from '../tenant/TenantProvider';
import { ContactQueryScope, useContactQueryScope } from '../contacts/queryScope';
import { CrmError, createCrmClient, type CrmClient } from '../contacts/crmClient';
import { mutationMessage, safeError } from '../contacts/ContactForm';
import type { Definition } from '../contacts/CustomFieldSettings';

function SettingsSession({ identity, tenantId }: { identity: string; tenantId: string }) {
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
  return client ? <Definitions client={client} /> : <p role="status">Đang tải…</p>;
}

// An infinite-query refetch shares one signal across its pages; track only that sequence.
const cursorsByFetch = new WeakMap<AbortSignal, Set<string>>();

function Definitions({ client }: { client: CrmClient }) {
  const scope = useContactQueryScope();
  const [key, setKey] = useState(''), [name, setName] = useState('');
  const [type, setType] = useState<Definition['dataType']>('string');
  const [required, setRequired] = useState(false), [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<CrmError | null>(null);
  const [blocked, setBlocked] = useState(false), [forbidden, setForbidden] = useState(false);
  const query = useInfiniteQuery({
    queryKey: [scope.scopeId, 'settings-fields'], initialPageParam: undefined as string | undefined,
    queryFn: async ({ pageParam, signal }) => {
      const result = await client.listFields(pageParam ? { cursor: pageParam } : {}, signal);
      if (signal.aborted || !scope.isCurrent()) throw new CrmError('WEB_CRM_READ_CANCELLED');
      let used = cursorsByFetch.get(signal);
      if (!used) {
        const previous = scope.client.getQueryData<InfiniteData<Awaited<ReturnType<CrmClient['listFields']>>, string | undefined>>([scope.scopeId, 'settings-fields']);
        used = new Set(pageParam ? previous?.pageParams.filter((cursor): cursor is string => cursor !== undefined) : []);
        cursorsByFetch.set(signal, used);
      }
      if (pageParam) used.add(pageParam);
      if (result.data.nextCursor && used.has(result.data.nextCursor)) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
      return result;
    },
    getNextPageParam: last => last.data.nextCursor ?? undefined,
  });
  async function save(event: FormEvent) {
    event.preventDefault();
    if (busy || blocked || forbidden) return;
    setBusy(true); setFailure(null);
    try {
      await client.createField({ key, displayName: name, dataType: type, required });
      if (!scope.isCurrent()) return;
      setKey(''); setName(''); setType('string'); setRequired(false);
      await query.refetch();
    } catch (error) {
      if (!scope.isCurrent()) return;
      const result = safeError(error); setFailure(result);
      if (result.status === 403) setForbidden(true);
      if (!result.status || result.status >= 500) setBlocked(true);
    } finally { if (scope.isCurrent()) setBusy(false); }
  }
  async function review() {
    try {
      const result = await query.refetch({ throwOnError: true });
      if (scope.isCurrent() && result.isSuccess) { setBlocked(false); setFailure(null); }
    } catch (error) { if (scope.isCurrent()) setFailure(safeError(error)); }
  }
  return <section className="contact-content"><h2>Định nghĩa trường tùy chỉnh</h2>
    {query.isPending && <p role="status">Đang tải…</p>}
    {query.isError && <><p role="alert">{safeError(query.error).message}</p><button disabled={query.isFetching} onClick={() => void query.refetch()}>Thử lại</button></>}
    {query.data && <><ul>{query.data.pages.flatMap((page, index) => page.data.items.map(field => <li key={`${index}:${field.id}`}>{field.displayName}</li>))}</ul>
      {query.hasNextPage && <button disabled={query.isFetching} onClick={() => void query.fetchNextPage()}>Tải thêm</button>}</>}
    <form className="contact-form" onSubmit={event => void save(event)}><fieldset disabled={busy || blocked || forbidden}><legend>Tạo định nghĩa</legend>
      <label>Khóa trường<input required pattern="[a-z][a-z0-9_]{0,63}" value={key} onChange={e => setKey(e.target.value)} /></label>
      <label>Tên trường<input required maxLength={200} value={name} onChange={e => setName(e.target.value)} /></label>
      <label>Kiểu dữ liệu<select value={type} onChange={e => setType(e.target.value as Definition['dataType'])}>
        <option value="string">Văn bản</option><option value="number">Số</option><option value="boolean">Đúng/sai</option><option value="date">Ngày</option></select></label>
      <label>Bắt buộc<input type="checkbox" checked={required} onChange={e => setRequired(e.target.checked)} /></label>
      <button type="submit">Tạo trường</button></fieldset></form>
    {failure && <p role="alert">{mutationMessage(failure)}</p>}
    {blocked && <button disabled={busy || query.isFetching} onClick={() => void review()}>Tải lại dữ liệu</button>}
  </section>;
}

export function CustomFieldsPage() {
  const { identity, selectedTenantId } = useTenant();
  const { state } = useAuth();
  const [session, setSession] = useState({ state, generation: 0 });
  if (session.state !== state) setSession({ state, generation: session.generation + 1 });
  if (!identity || !selectedTenantId) return <p>Chọn tenant để xem định nghĩa trường.</p>;
  return <ContactQueryScope identity={identity} tenantId={selectedTenantId} generation={session.generation}>
    <SettingsSession key={JSON.stringify([identity, selectedTenantId, session.generation])} identity={identity} tenantId={selectedTenantId} />
  </ContactQueryScope>;
}
