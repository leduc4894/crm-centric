import { infiniteQueryOptions, queryOptions, type InfiniteData } from '@tanstack/react-query';
import { CrmError, type ContactQuery, type CrmClient } from './crmClient';
import { queryKeys } from './queryKeys';
import { useContactQueryScope } from './queryScope';

type Scope = ReturnType<typeof useContactQueryScope>;
type Page = Awaited<ReturnType<CrmClient['listContacts']>>;
// One signal represents one infinite-query fetch, including all pages of a refetch.
const cursorsByFetch = new WeakMap<AbortSignal, Set<string>>();
async function read<T>(scope: Scope, signal: AbortSignal, fetch: () => Promise<T>): Promise<T> {
  try {
    const result = await fetch();
    if (signal.aborted || !scope.isCurrent()) throw new CrmError('WEB_CRM_READ_CANCELLED');
    return result;
  } catch (error) {
    if (signal.aborted || !scope.isCurrent()) throw new CrmError('WEB_CRM_READ_CANCELLED');
    throw error;
  }
}
export function contactsOptions(scope: Scope, client: CrmClient, filters: ContactQuery) {
  const queryKey = queryKeys.contacts(scope.scopeId, filters);
  return infiniteQueryOptions({
    queryKey, initialPageParam: undefined as string | undefined,
    queryFn: async ({ signal, pageParam }) => {
      const result = await read(scope, signal, () => client.listContacts(pageParam ? { ...filters, cursor: pageParam } : filters, signal));
      let used = cursorsByFetch.get(signal);
      if (!used) {
        const previous = scope.client.getQueryData<InfiniteData<Page, string | undefined>>(queryKey);
        used = new Set(pageParam ? previous?.pageParams.filter((cursor): cursor is string => cursor !== undefined) : []);
        cursorsByFetch.set(signal, used);
      }
      if (pageParam) used.add(pageParam);
      if (result.data.nextCursor && used.has(result.data.nextCursor)) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
      return result;
    },
    getNextPageParam: last => last.data.nextCursor ?? undefined,
  });
}
export function contactOptions(scope: Scope, client: CrmClient, id: string) {
  return queryOptions({ queryKey: queryKeys.contact(scope.scopeId, id), queryFn: ({ signal }) => read(scope, signal, () => client.getContact(id, signal)) });
}
export function tagsOptions(scope: Scope, client: CrmClient) {
  return queryOptions({ queryKey: queryKeys.tags(scope.scopeId), queryFn: ({ signal }) => read(scope, signal, () => client.listTags({}, signal)) });
}
export function fieldsOptions(scope: Scope, client: CrmClient) {
  return queryOptions({ queryKey: queryKeys.fields(scope.scopeId), queryFn: ({ signal }) => read(scope, signal, () => client.listFields({}, signal)) });
}
export function contactTagsOptions(scope: Scope, client: CrmClient, id: string) {
  return queryOptions({ queryKey: queryKeys.contactTags(scope.scopeId, id), queryFn: ({ signal }) => read(scope, signal, () => client.listContactTags(id, {}, signal)) });
}
export function fieldValueOptions(scope: Scope, client: CrmClient, id: string, fieldId: string) {
  return queryOptions({ queryKey: queryKeys.fieldValue(scope.scopeId, id, fieldId), queryFn: ({ signal }) => read(scope, signal, () => client.getField(id, fieldId, signal)) });
}
export function timelineOptions(scope: Scope, client: CrmClient, id: string) {
  return queryOptions({ queryKey: queryKeys.timeline(scope.scopeId, id), queryFn: ({ signal }) => read(scope, signal, () => client.listTimeline(id, {}, signal)) });
}

// Record panels keep their reviewed data in local state. Query holds transport
// results only; a cache refetch cannot silently adopt a new record version.
export function scopedRecordClient(scope: Scope, client: CrmClient): CrmClient {
  async function fetch<T>(key: readonly unknown[], signal: AbortSignal | undefined, operation: (signal: AbortSignal) => Promise<T>): Promise<T> {
    await scope.client.cancelQueries({ queryKey: key, exact: true });
    if (signal?.aborted || !scope.isCurrent()) throw new CrmError('WEB_CRM_READ_CANCELLED');
    const result = await scope.client.fetchQuery({ queryKey: key, retry: false, staleTime: 0, queryFn: ({ signal: querySignal }) => {
      const combined = signal ? AbortSignal.any([signal, querySignal]) : querySignal;
      return read(scope, combined, () => operation(combined));
    } });
    if (signal?.aborted || !scope.isCurrent()) throw new CrmError('WEB_CRM_READ_CANCELLED');
    return result;
  }
  async function write<T>(operation: () => Promise<T>): Promise<T> {
    if (!scope.isCurrent()) throw new CrmError('WEB_CRM_READ_CANCELLED');
    const result = await operation();
    if (!scope.isCurrent()) throw new CrmError('WEB_CRM_READ_CANCELLED');
    return result;
  }
  const page = (key: readonly unknown[], cursor?: string) => cursor ? [...key, cursor] : key;
  return {
    ...client,
    getContact: (id, signal) => fetch(queryKeys.contact(scope.scopeId, id), signal, s => client.getContact(id, s)),
    listTags: (query = {}, signal) => fetch(page(queryKeys.tags(scope.scopeId), query.cursor), signal, s => client.listTags(query, s)),
    listFields: (query = {}, signal) => fetch(page(queryKeys.fields(scope.scopeId), query.cursor), signal, s => client.listFields(query, s)),
    listContactTags: (id, query = {}, signal) => fetch(page(queryKeys.contactTags(scope.scopeId, id), query.cursor), signal, s => client.listContactTags(id, query, s)),
    getField: (id, fieldId, signal) => fetch(queryKeys.fieldValue(scope.scopeId, id, fieldId), signal, s => client.getField(id, fieldId, s)),
    listTimeline: (id, query = {}, signal) => fetch(page(queryKeys.timeline(scope.scopeId, id), query.cursor), signal, s => client.listTimeline(id, query, s)),
    createContact: (...args) => write(() => client.createContact(...args)),
    patchContact: (...args) => write(() => client.patchContact(...args)),
    deleteContact: (...args) => write(() => client.deleteContact(...args)),
    createTag: (...args) => write(() => client.createTag(...args)),
    createField: (...args) => write(() => client.createField(...args)),
    addTag: (...args) => write(() => client.addTag(...args)),
    removeTag: (...args) => write(() => client.removeTag(...args)),
    setField: (...args) => write(() => client.setField(...args)),
    createNote: (...args) => write(() => client.createNote(...args)),
  };
}
