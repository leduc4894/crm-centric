import { useCallback, useEffect, useRef, useState } from 'react';
import { safeError } from './ContactForm';
import { CrmError } from './crmClient';

type PanelRead = () => Promise<void>;
export type RegisterPanelRead = (read: PanelRead) => () => void;
export type RecordWrite = (write: (etag: string) => Promise<void>, refresh: () => Promise<void>, onForbidden?: () => void) => Promise<void>;

// A replacement read supersedes both the initial read and any in-flight page.
export function usePanelRead<T>(load: (signal: AbortSignal) => Promise<T>, register?: RegisterPanelRead) {
  const [data, setData] = useState<T>();
  const [error, setError] = useState<CrmError>();
  const [loading, setLoading] = useState(true);
  const active = useRef<AbortController | null>(null);
  const read = useCallback(async (operation = load) => {
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    setLoading(true);
    try {
      const next = await operation(controller.signal);
      controller.signal.throwIfAborted();
      setData(next); setError(undefined);
    } catch (cause) {
      if (!controller.signal.aborted) setError(safeError(cause));
      throw cause;
    } finally { if (!controller.signal.aborted) setLoading(false); }
  }, [load]);
  useEffect(() => {
    const unregister = register?.(read);
    let mounted = true;
    queueMicrotask(() => { if (mounted) void read().catch(() => { /* read exposes the failure in the panel */ }); });
    return () => { mounted = false; unregister?.(); active.current?.abort(); };
  }, [read, register]);
  return { data, error, loading, read };
}

export async function collectPages<T>(get: (cursor?: string) => Promise<{ data: { items: T[]; nextCursor: string | null } }>) {
  const items: T[] = [];
  const seen = new Set<string>();
  let cursor: string | undefined;
  do {
    const page = (await get(cursor)).data;
    items.push(...page.items);
    cursor = page.nextCursor ?? undefined;
    if (cursor && seen.has(cursor)) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
    if (cursor) seen.add(cursor);
  } while (cursor);
  return items;
}
