import { useLayoutEffect, useRef, useState } from 'react';
import { useInfiniteQuery, type InfiniteData } from '@tanstack/react-query';
import { useAuth, useAuthClient } from '../auth/AuthProvider';
import { useTenant } from '../tenant/TenantProvider';
import { ContactQueryScope, useContactQueryScope } from '../contacts/queryScope';
import { CrmError, createCrmClient, type CrmClient } from '../contacts/crmClient';
import { mutationMessage, safeError } from '../contacts/ContactForm';
import { DefinitionForm } from '../contacts/DefinitionForm';

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
  const [busy, setBusy] = useState(false);
  const writing = useRef(false);
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
  async function save(value: Parameters<CrmClient['createField']>[0]): Promise<boolean> {
    if (busy || writing.current || blocked || forbidden) return false;
    writing.current = true;
    setBusy(true); setFailure(null);
    let written = false;
    try {
      await client.createField(value);
      if (!scope.isCurrent()) return false;
      written = true;
      await scope.client.cancelQueries({ queryKey: [scope.scopeId, 'settings-fields'], exact: true });
      if (!scope.isCurrent()) return false;
      await query.refetch({ throwOnError: true });
      return scope.isCurrent();
    } catch (error) {
      if (!scope.isCurrent()) return false;
      const result = safeError(error); setFailure(result);
      if (written || !result.status || result.status >= 500) setBlocked(true);
      else if (result.status === 403) setForbidden(true);
      return false;
    } finally { writing.current = false; if (scope.isCurrent()) setBusy(false); }
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
    <DefinitionForm disabled={busy || blocked || forbidden} save={save} />
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
