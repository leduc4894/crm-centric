export interface Tenant { readonly id: string; readonly name: string; readonly status: 'active' | 'suspended' }
export interface TenantPage { readonly items: Tenant[]; readonly nextCursor: string | null }

export class TenantError extends Error {
  readonly correlationId: string;
  constructor(readonly reasonToken: string, readonly status?: number) {
    const correlationId = crypto.randomUUID();
    super(`${reasonToken} ${correlationId}`); this.correlationId = correlationId; this.name = 'TenantError';
  }
}
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const validCursor = (value: unknown): value is string => typeof value === 'string' && value.length > 0 && value.length <= 2048 && ![...value].some((c) => c.charCodeAt(0) < 32 || c.charCodeAt(0) === 127);
function validPage(value: unknown): value is TenantPage {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const page = value as Record<string, unknown>;
  if (Object.keys(page).some((key) => key !== 'items' && key !== 'nextCursor') || !Array.isArray(page.items) || !(page.nextCursor === null || validCursor(page.nextCursor))) return false;
  return page.items.every((v: unknown) => {
    if (!v || typeof v !== 'object' || Array.isArray(v)) return false;
    const item = v as Record<string, unknown>;
    return Object.keys(item).every((key) => ['id', 'name', 'status'].includes(key)) && typeof item.id === 'string' && uuid.test(item.id) && typeof item.name === 'string' && [...item.name].length >= 1 && [...item.name].length <= 200 && (item.status === 'active' || item.status === 'suspended');
  });
}
export async function listTenants(accessToken: string, cursor?: string, signal?: AbortSignal): Promise<TenantPage> {
  if (!accessToken || (cursor !== undefined && !validCursor(cursor))) throw new TenantError('WEB_TENANT_CONTRACT_INVALID');
  const controller = new AbortController();
  let timedOut = false;
  const abort = () => controller.abort();
  if (signal?.aborted) throw signal.reason ?? new DOMException('Aborted', 'AbortError');
  signal?.addEventListener('abort', abort, { once: true });
  const timer = setTimeout(() => { timedOut = true; controller.abort(); }, 10_000);
  try {
    const url = cursor === undefined ? '/api/v1/tenants' : `/api/v1/tenants?${new URLSearchParams({ cursor })}`;
    const response = await fetch(url, { method: 'GET', headers: { Authorization: `Bearer ${accessToken}`, Accept: 'application/json' }, credentials: 'omit', redirect: 'error', signal: controller.signal });
    if (response.status === 401) throw new TenantError('WEB_TENANT_UNAUTHORIZED', 401);
    if (!response.ok) throw new TenantError('WEB_TENANT_API_ERROR', response.status);
    let page: unknown;
    try { page = await response.json(); } catch { throw new TenantError('WEB_TENANT_CONTRACT_INVALID'); }
    if (!validPage(page)) throw new TenantError('WEB_TENANT_CONTRACT_INVALID');
    return page;
  } catch (error) {
    if (timedOut) throw new TenantError('WEB_TENANT_TIMEOUT');
    if (signal?.aborted) throw signal.reason ?? new DOMException('Aborted', 'AbortError');
    if (error instanceof TenantError) throw error;
    throw new TenantError('WEB_TENANT_NETWORK_ERROR');
  } finally { clearTimeout(timer); signal?.removeEventListener('abort', abort); }
}
