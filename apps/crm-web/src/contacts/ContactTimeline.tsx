import { useCallback, useLayoutEffect, useRef, useState } from 'react';
import { CrmError, type CrmClient } from './crmClient';
import { usePanelRead, type RecordWrite } from './recordPanels';

interface Props { client: CrmClient; id: string; disabled: boolean; run: RecordWrite; onVersion(etag: string | null): Promise<void> }
export function ContactTimeline({ client, id, disabled, run, onVersion }: Props) {
  const [draft, setDraft] = useState('');
  const input = useRef<HTMLTextAreaElement>(null);
  const restoreFocus = useRef(false);
  useLayoutEffect(() => {
    if (!disabled && restoreFocus.current) {
      input.current?.focus();
      restoreFocus.current = false;
    }
  }, [disabled]);
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
  const length = Array.from(draft).length;
  return <section className="contact-card activity-panel"><h3>Hoạt động</h3>
    <form className="contact-form" onSubmit={event => {
      event.preventDefault();
      if (disabled || loading || error || !length || length > 1000) return;
      void run(async etag => {
        const saved = await client.createNote(id, { text: draft }, etag);
        if (saved.data.contactId.toLowerCase() !== id.toLowerCase()) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
        restoreFocus.current = true;
        setDraft('');
        await onVersion(saved.etag);
      }, refresh);
    }}>
      <label>Ghi chú<textarea ref={input} rows={3} value={draft} disabled={disabled} onChange={e => setDraft(e.target.value)} /></label>
      <div className="contact-actions"><button type="submit" disabled={disabled || loading || !!error || !length || length > 1000}>Thêm ghi chú</button><span>{length}/1000</span></div>
    </form>
    {error && <p role="alert">{error.message} <button onClick={() => void refresh().catch(() => { })}>Tải lại hoạt động</button></p>}
    {loading && <p role="status">Đang tải hoạt động…</p>}
    {!error && data && (data.items.length ? <ol className="activity-list">{data.items.map(item => <li key={item.id}>
      <time dateTime={item.occurredAt}>{new Date(item.occurredAt).toLocaleString('vi-VN')}</time>
      <p>{item.summary}</p><small>{item.source} · {item.type}{item.inboxId && <> · {item.inboxId}</>}</small>
    </li>)}</ol> : <p>Chưa có hoạt động.</p>)}
    {data?.nextCursor && !error && <button disabled={loading || disabled} onClick={() => void more().catch(() => { })}>Hoạt động trước</button>}
  </section>;
}
