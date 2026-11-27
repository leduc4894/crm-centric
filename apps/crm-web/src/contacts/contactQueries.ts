import { infiniteQueryOptions, queryOptions, type InfiniteData } from '@tanstack/react-query';
import { CrmError, type ContactQuery, type CrmClient } from './crmClient';
import { queryKeys } from './queryKeys';
import { useContactQueryScope } from './queryScope';

type Scope = ReturnType<typeof useContactQueryScope>;
type Page = Awaited<ReturnType<CrmClient['listContacts']>>;
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
      const previous = scope.client.getQueryData<InfiniteData<Page, string | undefined>>(queryKey);
      const index = previous?.pageParams.indexOf(pageParam) ?? -1;
      const used = index < 0 ? previous?.pageParams : previous?.pageParams.slice(0, index + 1);
      if (result.data.nextCursor && (result.data.nextCursor === pageParam || (pageParam && used?.includes(result.data.nextCursor)))) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
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
