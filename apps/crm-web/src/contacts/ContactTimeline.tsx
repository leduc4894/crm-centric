import { useCallback, useLayoutEffect, useRef } from 'react';
import { useForm, useStore } from '@tanstack/react-form';
import { CrmError, type CrmClient } from './crmClient';
import { usePanelRead, type RecordWrite } from './recordPanels';

interface Props { client: CrmClient; id: string; disabled: boolean; run: RecordWrite; onVersion(etag: string | null): Promise<void> }
export function ContactTimeline({ client, id, disabled, run, onVersion }: Props) {

  const input = useRef<HTMLTextAreaElement>(null);
  const restoreFocus = useRef(false);
  const load = useCallback(async (signal: AbortSignal, cursor?: string) => {
    const { data } = await client.listTimeline(id, cursor ? { cursor } : {}, signal);
    if (data.items.some(entry => entry.contactId.toLowerCase() !== id.toLowerCase())) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
    return data;
  }, [client, id]);
  const { data, error, loading, read } = usePanelRead(load);
  const seen = useRef(new Set<string>());
  const refresh = async () => { await read(); seen.current.clear(); };
  const more = async () => {
    const cursor = data?.nextCursor;
    if (!cursor) return;
    await read(async signal => {
      const page = await load(signal, cursor);
      if (page.nextCursor && (page.nextCursor === cursor || seen.current.has(page.nextCursor))) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
      seen.current.add(cursor);
      return { ...page, items: [...data.items, ...page.items] };
    });
  };
  const inFlight = useRef(false);
  const form = useForm({ defaultValues: { draft: '' }, onSubmit: async ({ value }) => {
    const length = Array.from(value.draft).length;
    if (inFlight.current || disabled || loading || error || !length || length > 1000) return;
    inFlight.current = true;
    try {
      if (await run(async etag => {
        const saved = await client.createNote(id, { text: value.draft }, etag);
        if (saved.data.contactId.toLowerCase() !== id.toLowerCase()) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
        await onVersion(saved.etag);
      }, refresh, undefined, true)) { restoreFocus.current = true; form.reset(); }
    } finally { inFlight.current = false; }
  } });
  const draft = useStore(form.store, state => state.values.draft);
  const submitting = useStore(form.store, state => state.isSubmitting);
  const length = Array.from(draft).length;
  useLayoutEffect(() => {
    if (!disabled && !submitting && restoreFocus.current) {
      input.current?.focus();
      restoreFocus.current = false;
    }
  }, [disabled, submitting]);
  return <section className="contact-card activity-panel"><h3>Hoạt động</h3>
    <form className="contact-form" onSubmit={event => { event.preventDefault(); void form.handleSubmit(); }}>
      <form.Field name="draft">{field => <label>Ghi chú<textarea ref={input} rows={3} value={field.state.value} disabled={disabled || submitting} onBlur={field.handleBlur} onChange={e => field.handleChange(e.target.value)} /></label>}</form.Field>
      <div className="contact-actions"><button type="submit" disabled={disabled || submitting || loading || !!error || !length || length > 1000}>Thêm ghi chú</button><span>{length}/1000</span></div>
    </form>
    {error && <p role="alert">{error.message} <button onClick={() => void refresh().catch(() => { })}>Tải lại hoạt động</button></p>}
    {loading && <p role="status">Đang tải hoạt động…</p>}
    {!error && data && (data.items.length ? <ol className="activity-list">{data.items.map(item => <li key={item.id}>
      <time dateTime={item.occurredAt}>{new Date(item.occurredAt).toLocaleString('vi-VN')}</time>
      <p>{item.summary}</p><small>{item.source} · {item.type}{item.inboxId && <> · {item.inboxId}</>}</small>
    </li>)}</ol> : <p>Chưa có hoạt động.</p>)}
    {data?.nextCursor && !error && <button disabled={loading} onClick={() => void more().catch(() => { })}>Hoạt động trước</button>}
  </section>;
}
