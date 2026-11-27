import { useMemo, useRef, useState, type FormEvent } from 'react';
import { useInfiniteQuery } from '@tanstack/react-query';
import { ContactForm, safeError } from './ContactForm';
import { openContacts } from './ContactShell';
import { ContactTable } from './ContactTable';
import { contactsOptions, scopedRecordClient } from './contactQueries';
import { useContactQueryScope } from './queryScope';
import { contactIdPattern, CrmError, type ContactQuery, type CrmClient } from './crmClient';

export function ContactList({ client }: { client: CrmClient }) {
  const [query, setQuery] = useState<ContactQuery>({});
  const [creating, setCreating] = useState(false);
  const [readOnly, setReadOnly] = useState(false);
  const scope = useContactQueryScope();
  const recordClient = useMemo(() => scopedRecordClient(scope, client), [scope, client]);
  const generation = useRef(0);
  const options = contactsOptions(scope, client, query);
  const pages = useInfiniteQuery(options);
  const items = [...new Map((pages.data?.pages.flatMap(page => page.data.items) ?? []).map(item => [item.id, item])).values()];
  const next = pages.data?.pages.at(-1)?.data.nextCursor;
  const failure = pages.error ? safeError(pages.error) : null;
  const loading = pages.isFetching;
  async function refreshList() {
    const current = generation.current;
    try {
      await scope.client.cancelQueries({ queryKey: options.queryKey, exact: true });
      if (current !== generation.current || !scope.isCurrent()) throw new CrmError('WEB_CRM_READ_CANCELLED');
      const result = await pages.refetch({ cancelRefetch: true, throwOnError: true });
      if (current !== generation.current || !scope.isCurrent() || result.isError) throw new CrmError('WEB_CRM_READ_CANCELLED');
    } catch (error) {
      if (current !== generation.current || !scope.isCurrent()) throw new CrmError('WEB_CRM_READ_CANCELLED');
      throw error;
    }
  }
  function filter(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const q = String(form.get('q')), tagId = String(form.get('tagId')), date = String(form.get('date'));
    const filters = { ...(q && { q }), ...(tagId && { tagId }), ...(date && { updatedSince: new Date(date).toISOString() }) };
    generation.current++;
    void scope.client.cancelQueries({ queryKey: options.queryKey, exact: true });
    if (JSON.stringify(filters) === JSON.stringify(query)) void scope.client.resetQueries({ queryKey: options.queryKey, exact: true });
    else {
      scope.client.removeQueries({ queryKey: contactsOptions(scope, client, filters).queryKey, exact: true });
      setQuery(filters);
    }
  }
  return <>
    <header className="contact-title"><h2>Liên hệ</h2>{!readOnly && !creating && <button onClick={() => setCreating(true)}>Thêm liên hệ</button>}</header>
    {creating && <section className="contact-card"><h3>Thêm liên hệ</h3><ContactForm client={recordClient} readOnly={readOnly} choices={items} refreshList={refreshList}
      onSaved={result => openContacts(result.data.id)} onCancel={() => setCreating(false)} onForbidden={() => setReadOnly(true)} /></section>}
    <div className="contact-card">
      <form className="contact-filters" onSubmit={filter}>
        <label>Tìm kiếm<input name="q" type="search" /></label>
        <label>ID thẻ<input name="tagId" pattern={contactIdPattern.source.replaceAll('a-f', 'a-fA-F')} /></label>
        <label>Cập nhật từ<input name="date" type="datetime-local" /></label><button>Lọc</button>
      </form>
      {failure && <p role="alert">{failure.message} <button onClick={() => void (pages.isFetchNextPageError ? pages.fetchNextPage() : pages.refetch())}>Thử lại</button></p>}
      {items.length > 0 && <ContactTable items={items} />}
      {loading ? <p role="status">Đang tải…</p> : !failure && items.length === 0 && <p>Chưa có liên hệ.</p>}
      {next && !failure && <button disabled={loading} onClick={() => void pages.fetchNextPage()}>Tải thêm</button>}
    </div></>;
}
