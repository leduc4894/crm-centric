import { useRef, useState } from 'react';
import { useForm, useStore } from '@tanstack/react-form';
import type { components } from '@crm-centric/crm-client';
import { CrmError, type CrmClient } from './crmClient';

export type Contact = components['schemas']['Contact'];
export type Snapshot = Awaited<ReturnType<CrmClient['getContact']>>;
export function safeError(error: unknown) {
  return error instanceof CrmError ? error : new CrmError('WEB_CRM_NETWORK_ERROR');
}
export function mutationMessage(error: CrmError) {
  const hint = error.status === 403 ? 'Chỉ đọc.' : !error.status || error.status >= 500 ? 'Chưa xác định kết quả. Kiểm tra dữ liệu trước khi tiếp tục.' :
    error.status === 412 ? 'Dữ liệu đã thay đổi. Tải lại để đối chiếu.' : error.status === 428 ? 'Chọn liên hệ hiện có để cập nhật.' : '';
  return [hint, error.message].filter(Boolean).join(' ');
}
interface Props {
  client: CrmClient;
  record?: Snapshot;
  choices?: Contact[];
  readOnly: boolean;
  refreshAttributes?: () => Promise<void>;
  refreshList?: () => Promise<void>;
  onSaved: (record: Snapshot) => void;
  onCancel: () => void;
  onForbidden: () => void;
}
export function ContactForm({ client, record, choices = [], readOnly, refreshList, refreshAttributes, onSaved, onCancel, onForbidden }: Props) {
  const [original] = useState(record);
  const [base, setBase] = useState(record);
  const [failure, setFailure] = useState<CrmError | null>(null);
  const [blocked, setBlocked] = useState(false);
  const [choosing, setChoosing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [review, setReview] = useState(false);
  const [reconcileId, setReconcileId] = useState<string | null>(null);
  const submitting = useRef(false);
  const form = useForm({
    defaultValues: { name: record?.data.displayName ?? '', email: record?.data.email ?? '', phone: record?.data.phone ?? '' },
    validators: { onSubmit: ({ value }) => Array.from(value.name).length > 200 || Array.from(value.email).length > 320 || Array.from(value.phone).length > 40
      ? 'Giá trị vượt quá độ dài cho phép.' : undefined },
    onSubmit: async ({ value }) => { await save(value); },
  });
  const { name, email, phone } = useStore(form.store, state => state.values);
  const validationError = useStore(form.store, state => state.errorMap.onSubmit);
  async function read(id?: string) {
    setBusy(true);
    if (choosing || reconcileId) { setBase(undefined); setReview(false); }
    try {
      if (id) {
        const latest = await client.getContact(id);
        if (latest.data.id.toLowerCase() !== id.toLowerCase() || !latest.etag) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
        await refreshAttributes?.();
        setBase(latest); setReview(true);
      } else await refreshList?.();
      setFailure(null); if (!reconcileId) setBlocked(false);
    } catch (error) { setFailure(safeError(error)); }
    finally { setBusy(false); }
  }
  function changesFor(value: { name: string; email: string; phone: string }) { return original ? {
    ...(value.name !== original.data.displayName && { displayName: value.name }),
    ...(value.email !== (original.data.email ?? '') && { email: value.email || null }),
    ...(value.phone !== (original.data.phone ?? '') && { phone: value.phone || null })
  } : null;
  }
  const changes = changesFor({ name, email, phone });
  const unchanged = changes !== null && Object.keys(changes).length === 0;
  async function save(value: { name: string; email: string; phone: string }) {
    const submittedChanges = changesFor(value);
    if (submitting.current || busy || readOnly || (submittedChanges !== null && Object.keys(submittedChanges).length === 0) || blocked || (choosing && !base)) return;
    submitting.current = true;
    setBusy(true);
    const body = { displayName: value.name, email: value.email || null, ...(record || value.phone ? { phone: value.phone || null } : {}) };
    try {
      // A conditional edit needs the version displayed to the user.
      if (base && !base.etag) {
        setBlocked(true); throw new CrmError('WEB_CRM_CONTRACT_INVALID');
      }
      const saved = original && submittedChanges && base ? await client.patchContact(original.data.id, submittedChanges, base.etag!) :
        await client.createContact(body, base ? { id: base.data.id, etag: base.etag! } : undefined);
      const expectedId = original?.data.id ?? base?.data.id;
      if (!saved.etag || (expectedId && expectedId.toLowerCase() !== saved.data.id.toLowerCase())) {
        setReconcileId(expectedId ?? saved.data.id);
        setBlocked(true); setReview(false);
        setFailure(new CrmError('WEB_CRM_CONTRACT_INVALID'));
        return;
      }
      onSaved(saved);
    } catch (error) {
      const result = safeError(error);
      setFailure(result);
      if (result.status === 403) onForbidden();
      if (!record && result.status === 428) { setChoosing(true); setBase(undefined); }
      setBlocked(result.status === 412 || result.status === 403 || !result.status || result.status >= 500);
    } finally { submitting.current = false; setBusy(false); }
  }
  return <form className="contact-form" onSubmit={event => { event.preventDefault(); void form.handleSubmit(); }}>
    <form.Field name="name">{field => <label>Tên liên hệ<input disabled={busy || readOnly || !!reconcileId} autoFocus required value={field.state.value} onBlur={field.handleBlur} onChange={e => field.handleChange(e.target.value)} /></label>}</form.Field>
    <form.Field name="email">{field => <label>Email<input disabled={busy || readOnly || !!reconcileId} type="email" value={field.state.value} onBlur={field.handleBlur} onChange={e => field.handleChange(e.target.value)} /></label>}</form.Field>
    <form.Field name="phone">{field => <label>Điện thoại<input disabled={busy || readOnly || !!reconcileId} type="tel" value={field.state.value} onBlur={field.handleBlur} onChange={e => field.handleChange(e.target.value)} /></label>}</form.Field>
    {validationError && <p role="alert">{validationError}</p>}
    {failure && <p role="alert">{mutationMessage(failure)}</p>}
    {choosing && <fieldset><legend>Chọn liên hệ để đối chiếu</legend>
      {choices.map(item => <button key={item.id} type="button" disabled={busy || readOnly || !!reconcileId} onClick={() => void read(item.id)}>Chọn {item.displayName}{choices.filter(choice => choice.displayName === item.displayName).length > 1 ? ' — ' + item.id : ''}</button>)}
    </fieldset>}
    {review && base && <section><h4>Dữ liệu hiện tại</h4><dl><dt>Tên</dt><dd>{base.data.displayName}</dd><dt>Email</dt><dd>{base.data.email ?? '—'}</dd><dt>Điện thoại</dt><dd>{base.data.phone ?? '—'}</dd></dl></section>}
    {blocked && !(reconcileId && review) && failure?.status !== 403 && <button type="button" disabled={busy} onClick={() => void read(reconcileId ?? base?.data.id)}>Tải lại dữ liệu</button>}
    {reconcileId && review && base && <div>
      <p>Chọn giữ bản nháp để tiếp tục chỉnh sửa, hoặc bỏ bản nháp để dùng dữ liệu hiện tại.</p>
      <button type="button" disabled={busy || readOnly} onClick={() => { setReconcileId(null); setBlocked(false); }}>Giữ và chỉnh sửa bản nháp</button>
      <button type="button" disabled={busy} onClick={() => onSaved(base)}>Bỏ bản nháp và dùng dữ liệu hiện tại</button>
    </div>}
    <div className="contact-actions"><button type="submit" disabled={busy || readOnly || unchanged || blocked || (choosing && !base)}>{record ? 'Lưu' : base ? 'Cập nhật ' + base.data.displayName : 'Tạo'}</button>
    <button type="button" disabled={busy} onClick={onCancel}>Hủy</button></div>
  </form>;
}
