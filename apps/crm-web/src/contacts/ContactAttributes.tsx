import { useCallback, useRef } from 'react';
import { useForm, useStore } from '@tanstack/react-form';
import type { Snapshot } from './ContactForm';
import { CustomFieldSettings, type Definition } from './CustomFieldSettings';
import { CrmError, type CrmClient } from './crmClient';
import { exactNumber, type FieldValue } from './losslessJson';
import { collectPages, usePanelRead, type RecordWrite, type RegisterPanelRead } from './recordPanels';

interface Props { client: CrmClient; id: string; disabled: boolean; run: RecordWrite; register: RegisterPanelRead; onRecord(record: Snapshot): void }
function FieldEditor({ definition, client, id, disabled, run, register, onRecord }: Props & { definition: Definition }) {
  const load = useCallback(async (signal: AbortSignal) => {
    try { return (await client.getField(id, definition.id, signal)).data; }
    catch (cause) {
      if (cause instanceof CrmError && cause.status === 404 && cause.reasonToken === 'NOT_FOUND') return null;
      throw cause;
    }
  }, [client, definition.id, id]);
  const { data, read, error, loading } = usePanelRead(load, register);
  const current = typeof data === 'object' && data !== null ? data.numberToken : String(data ?? '');
  const inFlight = useRef(false);
  const form = useForm({
    defaultValues: { draft: undefined as string | undefined },
    onSubmit: async ({ value: values }) => {
      const value = values.draft;
      if (inFlight.current || value === undefined || value === current || disabled || loading || !!error) return;
      let scalar: FieldValue = value || null;
      if (!value && definition.required) return;
      if (value && definition.dataType === 'number') {
        try { scalar = exactNumber(value); } catch { return; }
      } else if (value && definition.dataType === 'boolean') scalar = value === 'true';
      inFlight.current = true;
      try {
        if (await run(async etag => { onRecord(await client.setField(id, definition.id, scalar, etag)); }, read)) form.reset();
      } finally { inFlight.current = false; }
    },
  });
  const draft = useStore(form.store, state => state.values.draft);
  const submitting = useStore(form.store, state => state.isSubmitting);
  const value = draft ?? current;
  const inactive = disabled || loading || !!error || submitting;
  let valid = !!value || !definition.required;
  if (value && definition.dataType === 'number') {
    try { exactNumber(value); } catch { valid = false; }
  }
  return <form className="contact-form" onSubmit={event => { event.preventDefault(); void form.handleSubmit(); }}>
    <form.Field name="draft">{field => <label>{definition.displayName}{definition.dataType === 'boolean' ?
      <select value={value} required={definition.required} disabled={inactive} onBlur={field.handleBlur} onChange={e => field.handleChange(e.target.value)}>
        <option value="">—</option><option value="true">Đúng</option><option value="false">Sai</option></select> :
      <input type={definition.dataType === 'date' ? 'date' : 'text'} required={definition.required} disabled={inactive} value={value} onBlur={field.handleBlur} onChange={e => field.handleChange(e.target.value)} />}</label>}</form.Field>
    {draft !== undefined && draft !== current && <small>Giá trị hiện tại: {current || '—'}</small>}
    <button type="submit" disabled={inactive || !valid || draft === undefined || draft === current}>Lưu {definition.displayName}</button>
    {error && <p role="alert">{error.message} <button type="button" onClick={() => void read().catch(() => { })}>Tải lại giá trị</button></p>}
  </form>;
}
export function ContactAttributes(props: Props) {
  const { client, id, run, disabled, onRecord } = props;
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
  const attachingRef = useRef(false), creatingRef = useRef(false);
  const attachForm = useForm({
    defaultValues: { selected: '' },
    onSubmit: async ({ value }) => {
      if (attachingRef.current || locked || !options.some(tag => tag.id === value.selected)) return;
      attachingRef.current = true;
      try {
        if (await run(async etag => { onRecord(await client.addTag(id, value.selected, etag)); }, tags.read)) attachForm.reset();
      } finally { attachingRef.current = false; }
    },
  });
  const createForm = useForm({
    defaultValues: { name: '' },
    onSubmit: async ({ value }) => {
      if (creatingRef.current || locked || !value.name.trim()) return;
      creatingRef.current = true;
      try {
        if (await run(async () => { await client.createTag({ name: value.name }); }, tags.read)) createForm.reset();
      } finally { creatingRef.current = false; }
    },
  });
  const selected = useStore(attachForm.store, state => state.values.selected);
  const name = useStore(createForm.store, state => state.values.name);
  const attaching = useStore(attachForm.store, state => state.isSubmitting);
  const creating = useStore(createForm.store, state => state.isSubmitting);
  return <><section className="contact-card"><h3>Thẻ</h3>
    {tags.error && <p role="alert">{tags.error.message} <button onClick={() => void tags.read().catch(() => { })}>Tải lại thẻ</button></p>}
    {tags.data && !tags.error && <><ul className="tag-list">{tags.data.assigned.map(tag => <li key={tag.id}>{tag.name}
      <button aria-label={'Gỡ ' + tag.name} disabled={locked} onClick={() => void run(async etag => onRecord(await client.removeTag(id, tag.id, etag)), tags.read)}>×</button></li>)}</ul>
      {!tags.data.assigned.length && <p>Chưa gắn thẻ.</p>}
      <form className="contact-form" onSubmit={e => { e.preventDefault(); void attachForm.handleSubmit(); }}>
        <attachForm.Field name="selected">{field => <label>Thẻ có sẵn<select disabled={locked || attaching} value={field.state.value} onBlur={field.handleBlur} onChange={e => field.handleChange(e.target.value)}><option value="">Chọn thẻ</option>
          {options.map(tag => <option key={tag.id} value={tag.id}>{tag.name}</option>)}</select></label>}</attachForm.Field><button disabled={locked || attaching || !selected}>Gắn thẻ</button></form>
      <form className="contact-form" onSubmit={e => { e.preventDefault(); void createForm.handleSubmit(); }}>
        <createForm.Field name="name">{field => <label>Tên thẻ mới<input disabled={locked || creating} required maxLength={64} value={field.state.value} onBlur={field.handleBlur} onChange={e => field.handleChange(e.target.value)} /></label>}</createForm.Field><button disabled={locked || creating || !name.trim()}>Tạo thẻ</button></form></>}
  </section><section className="contact-card"><h3>Trường tùy chỉnh</h3>
      {fields.error && <p role="alert">{fields.error.message} <button onClick={() => void fields.read().catch(() => { })}>Tải lại trường</button></p>}
      {fields.data && !fields.error && <>{fields.data.map(definition => <FieldEditor key={definition.id} {...props} definition={definition} />)}
        {!fields.data.length && <p>Chưa có trường tùy chỉnh.</p>}
        <CustomFieldSettings client={client} fields={fields.data} disabled={disabled || fields.loading} run={run} refresh={fields.read} /></>}
    </section></>;
}
