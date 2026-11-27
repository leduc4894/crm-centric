import { useCallback, useState } from 'react';
import type { Snapshot } from './ContactForm';
import { CustomFieldSettings, type Definition } from './CustomFieldSettings';
import { CrmError, type CrmClient } from './crmClient';
import { exactNumber, type FieldValue } from './losslessJson';
import { collectPages, usePanelRead, type RecordWrite, type RegisterPanelRead } from './recordPanels';

interface Props { client: CrmClient; id: string; disabled: boolean; run: RecordWrite; register: RegisterPanelRead; onRecord(record: Snapshot): void }
function FieldEditor({ definition, client, id, disabled, run, register, onRecord }: Props & { definition: Definition }) {
  const [draft, setDraft] = useState<string>();
  const load = useCallback(async (signal: AbortSignal) => {
    try { return (await client.getField(id, definition.id, signal)).data; }
    catch (cause) {
      if (cause instanceof CrmError && cause.status === 404 && cause.reasonToken === 'NOT_FOUND') return null;
      throw cause;
    }
  }, [client, definition.id, id]);
  const { data, read, error, loading } = usePanelRead(load, register);
  const current = typeof data === 'object' && data !== null ? data.numberToken : String(data ?? '');
  const value = draft ?? current;
  const inactive = disabled || loading || !!error;
  let scalar: FieldValue = value || null;
  let valid = !!value || !definition.required;
  if (value && definition.dataType === 'number') {
    try { scalar = exactNumber(value); } catch { valid = false; }
  } else if (value && definition.dataType === 'boolean') scalar = value === 'true';
  return <form className="contact-form" onSubmit={event => {
    event.preventDefault();
    if (!valid || inactive || draft === undefined) return;
    void run(async etag => {
      onRecord(await client.setField(id, definition.id, scalar, etag));
      setDraft(undefined);
    }, read);
  }}><label>{definition.displayName}{definition.dataType === 'boolean' ?
    <select value={value} required={definition.required} disabled={inactive} onChange={e => setDraft(e.target.value)}>
      <option value="">—</option><option value="true">Đúng</option><option value="false">Sai</option></select> :
    <input type={definition.dataType === 'date' ? 'date' : 'text'} required={definition.required} disabled={inactive} value={value} onChange={e => setDraft(e.target.value)} />}</label>
    {draft !== undefined && draft !== current && <small>Giá trị hiện tại: {current || '—'}</small>}
    <button type="submit" disabled={inactive || !valid || draft === undefined || draft === current}>Lưu {definition.displayName}</button>
    {error && <p role="alert">{error.message} <button type="button" onClick={() => void read().catch(() => { })}>Tải lại giá trị</button></p>}
  </form>;
}
export function ContactAttributes(props: Props) {
  const { client, id, run, disabled, onRecord } = props;
  const [name, setName] = useState(''), [selected, setSelected] = useState('');
  const loadTags = useCallback(async (signal: AbortSignal) => {
    const [all, assigned] = await Promise.all([
      collectPages(cursor => client.listTags(cursor ? { cursor } : {}, signal)),
      collectPages(cursor => client.listContactTags(id, cursor ? { cursor } : {}, signal))
    ]);
    return { all, assigned };
  }, [client, id]);
  const tags = usePanelRead(loadTags, props.register);
  const loadFields = useCallback((signal: AbortSignal) => collectPages(cursor => client.listFields(cursor ? { cursor } : {}, signal)), [client]);
  const fields = usePanelRead(loadFields);
  const locked = disabled || tags.loading || !!tags.error;
  const options = tags.data?.all.filter(tag => !tags.data?.assigned.some(item => item.id === tag.id)) ?? [];
  return <><section className="contact-card"><h3>Thẻ</h3>
    {tags.error && <p role="alert">{tags.error.message} <button onClick={() => void tags.read().catch(() => { })}>Tải lại thẻ</button></p>}
    {tags.data && !tags.error && <><ul className="tag-list">{tags.data.assigned.map(tag => <li key={tag.id}>{tag.name}
      <button aria-label={'Gỡ ' + tag.name} disabled={locked} onClick={() => void run(async etag => onRecord(await client.removeTag(id, tag.id, etag)), tags.read)}>×</button></li>)}</ul>
      {!tags.data.assigned.length && <p>Chưa gắn thẻ.</p>}
      <form className="contact-form" onSubmit={e => {
        e.preventDefault();
        if (!locked && options.some(tag => tag.id === selected)) void run(async etag => { onRecord(await client.addTag(id, selected, etag)); setSelected(''); }, tags.read);
      }}><label>Thẻ có sẵn<select disabled={locked} value={selected} onChange={e => setSelected(e.target.value)}><option value="">Chọn thẻ</option>
        {options.map(tag => <option key={tag.id} value={tag.id}>{tag.name}</option>)}</select></label><button disabled={locked || !selected}>Gắn thẻ</button></form>
      <form className="contact-form" onSubmit={e => {
        e.preventDefault();
        if (!locked && name.trim()) void run(async () => { await client.createTag({ name }); setName(''); }, tags.read);
      }}><label>Tên thẻ mới<input disabled={locked} required maxLength={64} value={name} onChange={e => setName(e.target.value)} /></label><button disabled={locked || !name.trim()}>Tạo thẻ</button></form></>}
  </section><section className="contact-card"><h3>Trường tùy chỉnh</h3>
      {fields.error && <p role="alert">{fields.error.message} <button onClick={() => void fields.read().catch(() => { })}>Tải lại trường</button></p>}
      {fields.data && !fields.error && <>{fields.data.map(definition => <FieldEditor key={definition.id} {...props} definition={definition} />)}
        {!fields.data.length && <p>Chưa có trường tùy chỉnh.</p>}
        <CustomFieldSettings client={client} fields={fields.data} disabled={disabled || fields.loading} run={run} refresh={fields.read} /></>}
    </section></>;
}
