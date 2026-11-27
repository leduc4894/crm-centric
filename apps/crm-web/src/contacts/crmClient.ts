import { createApiClient, type components, type operations } from '@crm-centric/crm-client';
import { readField, writeField, type FieldValue } from './losslessJson';

type Schema = components['schemas'];
type PageQuery = NonNullable<operations['listTimeline']['parameters']['query']>;
export type ContactQuery = NonNullable<operations['listContacts']['parameters']['query']>;
export const contactIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const strongTag = /^"v[1-9]\d*"$/;
const knownReasons = new Set(['REQUEST_INVALID', 'NOT_FOUND', 'VERSION_CONFLICT', 'PRECONDITION_REQUIRED',
  'PRECONDITION_INVALID', 'FIELD_VALUE_UNREPRESENTABLE', 'CRM_STORE_UNAVAILABLE', 'TENANT_FORBIDDEN',
  'TENANT_ACCESS_DENIED', 'TENANT_CONTEXT_INVALID', 'CONTACT_EMAIL_CONFLICT', 'AUTHENTICATION_REQUIRED',
  'CRM_CURSOR_UNAVAILABLE', 'CURSOR_INVALID', 'FIELD_KEY_CONFLICT', 'TAG_NAME_CONFLICT', 'INSUFFICIENT_SCOPE', 'INTERNAL_ERROR']);
export class CrmError extends Error {
  constructor(readonly reasonToken: string, readonly status = 0, readonly correlationId: string = crypto.randomUUID()) {
    super(reasonToken + ' (' + correlationId + ')');
  }
}
const invalid = () => new CrmError('WEB_CRM_CONTRACT_INVALID');
type Guard<T> = (v: unknown) => v is T;
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const id = (v: unknown) => typeof v === 'string' && contactIdPattern.test(v);
const date = (v: unknown) => typeof v === 'string' && Number.isFinite(Date.parse(v));
const version = (v: unknown) => typeof v === 'number' && Number.isSafeInteger(v) && v > 0;
const contact: Guard<Schema['Contact']> = (v): v is Schema['Contact'] => object(v) && id(v.id) &&
  typeof v.displayName === 'string' && (v.email == null || typeof v.email === 'string') &&
  (v.phone == null || typeof v.phone === 'string') && version(v.version) && date(v.createdAt) && date(v.updatedAt);
const tag: Guard<Schema['Tag']> = (v): v is Schema['Tag'] => object(v) && id(v.id) && typeof v.name === 'string';
const field: Guard<Schema['FieldDefinition']> = (v): v is Schema['FieldDefinition'] => object(v) && id(v.id) &&
  typeof v.key === 'string' && typeof v.displayName === 'string' && typeof v.required === 'boolean' &&
  ['string', 'boolean', 'number', 'date'].some(type => type === v.dataType) && version(v.version);
const entry: Guard<Schema['TimelineEntry']> = (v): v is Schema['TimelineEntry'] => object(v) && id(v.id) &&
  id(v.contactId) && (v.inboxId === null || id(v.inboxId)) && date(v.occurredAt) &&
  typeof v.summary === 'string' && typeof v.source === 'string' && typeof v.type === 'string';
function page<T>(item: Guard<T>) {
  return (v: unknown): v is { items: T[]; nextCursor: string | null } => object(v) && Array.isArray(v.items) &&
    v.items.every(item) && (v.nextCursor === null || (typeof v.nextCursor === 'string' && v.nextCursor.length > 0));
}
function json<T>(guard: Guard<T>) {
  return (raw: string): T => {
    const value: unknown = JSON.parse(raw);
    if (!guard(value)) throw invalid();
    return value;
  };
}
interface Scope { origin: string; tenantId: string; token: string; signal: AbortSignal; invalidate(): void; current?(): boolean }
type Options = { signal: AbortSignal; credentials: 'omit'; redirect: 'error'; cache: 'no-store'; parseAs: 'text'; headers: Record<string, string> };
interface WireResponse { response: Response; data?: unknown; error?: unknown }
export function createCrmClient(scope: Scope) {
  const sdk = createApiClient(scope.origin);
  const header = { 'X-Tenant-Id': scope.tenantId };
  const checkScope = () => {
    if (scope.signal.aborted || scope.current?.() === false) throw new DOMException('Aborted', 'AbortError');
  };
  async function run<T>(successStatuses: readonly number[], operation: (options: Options) => Promise<WireResponse>, decode: (raw: string) => T, signal?: AbortSignal) {
    checkScope();
    if (!scope.token || !id(scope.tenantId)) throw invalid();
    const deadline = new AbortController();
    const timeout = setTimeout(() => deadline.abort(), 10_000);
    const combined = AbortSignal.any([scope.signal, deadline.signal, ...(signal ? [signal] : [])]);
    try {
      combined.throwIfAborted();
      const { response, data, error } = await operation({
        signal: combined, credentials: 'omit', redirect: 'error', cache: 'no-store',
        parseAs: 'text', headers: { Authorization: 'Bearer ' + scope.token, Accept: 'application/json' }
      });
      checkScope();
      combined.throwIfAborted();
      if (!response.ok) {
        if (response.status === 401) scope.invalidate();
        const problem = object(error) ? error : {};
        const reason = typeof problem.reasonToken === 'string' && knownReasons.has(problem.reasonToken) ? problem.reasonToken : 'WEB_CRM_API_ERROR';
        const correlation = problem.correlationId ?? response.headers.get('x-correlation-id');
        throw new CrmError(reason, response.status, typeof correlation === 'string' && (id(correlation) || /^[0-9a-f]{32}$/i.test(correlation)) ? correlation : undefined);
      }
      if (!successStatuses.includes(response.status)) throw invalid();
      const etag = response.headers.get('etag');
      try { return { data: decode(typeof data === 'string' ? data : ''), etag: etag && strongTag.test(etag) ? etag : null, status: response.status }; }
      catch { throw invalid(); }
    } catch (error) {
      if (error instanceof CrmError) throw error;
      checkScope(); signal?.throwIfAborted();
      if (deadline.signal.aborted) throw new CrmError('WEB_CRM_TIMEOUT');
      throw new CrmError('WEB_CRM_NETWORK_ERROR');
    } finally { clearTimeout(timeout); }
  }
  const match = (etag: string) => {
    if (!strongTag.test(etag)) throw invalid();
    return { ...header, 'If-Match': etag };
  };
  function path<T extends Record<string, string>>(ids: T): T {
    if (!Object.values(ids).every(id)) throw invalid();
    return ids;
  }
  const api = {
    listContacts(query: ContactQuery = {}, signal?: AbortSignal) {
      return run([200], o => sdk.GET('/api/v1/contacts', { ...o, params: { header, query } }), json(page(contact)), signal);
    },
    getContact(id: string, signal?: AbortSignal) {
      return run([200], o => sdk.GET('/api/v1/contacts/{id}', { ...o, params: { header, path: path({ id }) } }), json(contact), signal);
    },
    createContact(body: Schema['CreateContact'], expected?: { id: string; etag: string }, signal?: AbortSignal) {
      return run([200, 201], o => sdk.POST('/api/v1/contacts', { ...o, params: { header: expected ? { ...match(expected.etag), 'X-Contact-Id': expected.id } : header }, body }), json(contact), signal);
    },
    patchContact(id: string, body: Schema['UpdateContact'], etag: string, signal?: AbortSignal) {
      return run([200], o => sdk.PATCH('/api/v1/contacts/{id}', { ...o, params: { header: match(etag), path: path({ id }) }, body }), json(contact), signal);
    },
    deleteContact(id: string, etag: string, signal?: AbortSignal) {
      return run([204], o => sdk.DELETE('/api/v1/contacts/{id}', { ...o, params: { header: match(etag), path: path({ id }) } }), raw => { if (raw) throw invalid(); }, signal);
    },
    listTags(query: PageQuery = {}, signal?: AbortSignal) {
      return run([200], o => sdk.GET('/api/v1/tags', { ...o, params: { header, query } }), json(page(tag)), signal);
    },
    createTag(body: Schema['CreateTag'], signal?: AbortSignal) {
      return run([201], o => sdk.POST('/api/v1/tags', { ...o, params: { header }, body }), json(tag), signal);
    },
    listFields(query: PageQuery = {}, signal?: AbortSignal) {
      return run([200], o => sdk.GET('/api/v1/custom-fields', { ...o, params: { header, query } }), json(page(field)), signal);
    },
    createField(body: Schema['CreateField'], signal?: AbortSignal) {
      return run([201], o => sdk.POST('/api/v1/custom-fields', { ...o, params: { header }, body }), json(field), signal);
    },
    listContactTags(id: string, query: PageQuery = {}, signal?: AbortSignal) {
      return run([200], o => sdk.GET('/api/v1/contacts/{id}/tags', { ...o, params: { header, path: path({ id }), query } }), json(page(tag)), signal);
    },
    addTag(id: string, tagId: string, etag: string, signal?: AbortSignal) {
      return run([200], o => sdk.PUT('/api/v1/contacts/{id}/tags/{tagId}', { ...o, params: { header: match(etag), path: path({ id, tagId }) } }), json(contact), signal);
    },
    removeTag(id: string, tagId: string, etag: string, signal?: AbortSignal) {
      return run([200], o => sdk.DELETE('/api/v1/contacts/{id}/tags/{tagId}', { ...o, params: { header: match(etag), path: path({ id, tagId }) } }), json(contact), signal);
    },
    getField(id: string, fieldId: string, signal?: AbortSignal) {
      return run([200], o => sdk.GET('/api/v1/contacts/{id}/fields/{fieldId}', { ...o, params: { header, path: path({ id, fieldId }) } }), readField, signal);
    },
    setField(id: string, fieldId: string, value: FieldValue, etag: string, signal?: AbortSignal) {
      // The generated body type uses number. Serialize the original numeric token
      // directly instead of passing it through the SDK's JSON.stringify.
      return run([200], o => {
        let body: string;
        try { body = writeField(value); } catch { throw invalid(); }
        return sdk.PUT('/api/v1/contacts/{id}/fields/{fieldId}', { ...o, params: { header: match(etag), path: path({ id, fieldId }) }, body: { value: null }, bodySerializer: () => body });
      }, json(contact), signal);
    },
    listTimeline(id: string, query: PageQuery = {}, signal?: AbortSignal) {
      return run([200], o => sdk.GET('/api/v1/contacts/{id}/timeline', { ...o, params: { header, path: path({ id }), query } }), json(page(entry)), signal);
    },
    createNote(id: string, body: Schema['CreateNote'], etag: string, signal?: AbortSignal) {
      return run([201], o => sdk.POST('/api/v1/contacts/{id}/notes', { ...o, params: { header: match(etag), path: path({ id }) }, body }), json(entry), signal);
    },
  };
  return api;
}
export type CrmClient = ReturnType<typeof createCrmClient>;
