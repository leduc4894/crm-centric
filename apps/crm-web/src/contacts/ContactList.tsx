import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { ContactForm, safeError, type Contact } from './ContactForm';
import { navigate, openContacts } from './ContactShell';
import { contactIdPattern, CrmError, type ContactQuery, type CrmClient } from './crmClient';

export function ContactList({ client }: { client: CrmClient }) {
  const [items, setItems] = useState<Contact[]>([]);
  const [next, setNext] = useState<string | null>(null);
  const [query, setQuery] = useState<ContactQuery>({});
  const [failure, setFailure] = useState<CrmError | null>(null);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [readOnly, setReadOnly] = useState(false);
  const active = useRef<AbortController | null>(null);
  const cursors = useRef(new Set<string>());
  const load = useCallback(async (filters: ContactQuery, cursor?: string) => {
    active.current?.abort();
    const request = new AbortController();
    active.current = request;
    setLoading(true); setFailure(null);
    if (!cursor) { setItems([]); setNext(null); cursors.current.clear(); }
    try {
      const { data } = await client.listContacts(cursor ? { ...filters, cursor } : filters, request.signal);
      request.signal.throwIfAborted();
      if (cursor) cursors.current.add(cursor);
      if (data.nextCursor && cursors.current.has(data.nextCursor)) {
        setNext(null); throw new CrmError('WEB_CRM_CONTRACT_INVALID');
      }
      setItems(old => cursor ? [...new Map([...old, ...data.items].map(item => [item.id, item])).values()] : data.items);
      setNext(data.nextCursor);
    } catch (error) {
      if (request.signal.aborted) return new CrmError('WEB_CRM_READ_CANCELLED');
      const failure = safeError(error);
      setFailure(failure);
      return failure;
    } finally { if (!request.signal.aborted) setLoading(false); }
  }, [client]);
  useEffect(() => {
    let mounted = true;
    queueMicrotask(() => { if (mounted) void load({}); });
    return () => { mounted = false; active.current?.abort(); };
  }, [load]);
  function filter(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const q = String(form.get('q')), tagId = String(form.get('tagId')), date = String(form.get('date'));
    const filters = { ...(q && { q }), ...(tagId && { tagId }), ...(date && { updatedSince: new Date(date).toISOString() }) };
    setQuery(filters); void load(filters);
  }
  return <>
    <header className="contact-title"><h2>Liên hệ</h2>{!readOnly && !creating && <button onClick={() => setCreating(true)}>Thêm liên hệ</button>}</header>
    {creating && <section className="contact-card"><h3>Thêm liên hệ</h3><ContactForm client={client} readOnly={readOnly} choices={items} refreshList={async () => { const failure = await load(query); if (failure) throw failure; }}
      onSaved={result => openContacts(result.data.id)} onCancel={() => setCreating(false)} onForbidden={() => setReadOnly(true)} /></section>}
    <div className="contact-card">
      <form className="contact-filters" onSubmit={filter}>
        <label>Tìm kiếm<input name="q" type="search" /></label>
        <label>ID thẻ<input name="tagId" pattern={contactIdPattern.source.replaceAll('a-f', 'a-fA-F')} /></label>
        <label>Cập nhật từ<input name="date" type="datetime-local" /></label><button>Lọc</button>
      </form>
      {failure && <p role="alert">{failure.message} <button onClick={() => void load(query, next ?? undefined)}>Thử lại</button></p>}
      {items.length > 0 && <table><thead><tr><th>Tên liên hệ</th><th>Email</th><th>Điện thoại</th></tr></thead><tbody>
        {items.map(item => <tr key={item.id}><td><a href={'/app/contacts/' + item.id} onClick={navigate}>{item.displayName}</a></td><td>{item.email ?? '—'}</td><td>{item.phone ?? '—'}</td></tr>)}
      </tbody></table>}
      {loading ? <p role="status">Đang tải…</p> : !failure && items.length === 0 && <p>Chưa có liên hệ.</p>}
      {next && !failure && <button disabled={loading} onClick={() => void load(query, next)}>Tải thêm</button>}
    </div></>;
}
