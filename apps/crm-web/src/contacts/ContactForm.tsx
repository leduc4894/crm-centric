import { useState, type FormEvent } from 'react';
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
  const [base, setBase] = useState(record);
  const [name, setName] = useState(record?.data.displayName ?? '');
  const [email, setEmail] = useState(record?.data.email ?? '');
  const [phone, setPhone] = useState(record?.data.phone ?? '');
  const [failure, setFailure] = useState<CrmError | null>(null);
  const [blocked, setBlocked] = useState(false);
  const [choosing, setChoosing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [review, setReview] = useState(false);
  async function read(id?: string) {
    setBusy(true);
    if (choosing) { setBase(undefined); setReview(false); }
    try {
      if (id) {
        const latest = await client.getContact(id);
        if (latest.data.id.toLowerCase() !== id.toLowerCase() || !latest.etag) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
        await refreshAttributes?.();
        setBase(latest); setReview(true);
      } else await refreshList?.();
      setFailure(null); setBlocked(false);
    } catch (error) { setFailure(safeError(error)); }
    finally { setBusy(false); }
  }
  const changes = record ? {
    ...(name !== record.data.displayName && { displayName: name }),
    ...(email !== (record.data.email ?? '') && { email: email || null }),
    ...(phone !== (record.data.phone ?? '') && { phone: phone || null })
  } : null;
  const unchanged = changes !== null && Object.keys(changes).length === 0;
  async function save(event: FormEvent) {
    event.preventDefault();
    if (busy || readOnly || unchanged || blocked || (choosing && !base)) return;
    setBusy(true);
    const body = { displayName: name, email: email || null, ...(record || phone ? { phone: phone || null } : {}) };
    try {
      // A conditional edit needs the version displayed to the user.
      if (base && !base.etag) {
        setBlocked(true); throw new CrmError('WEB_CRM_CONTRACT_INVALID');
      }
      const saved = record && changes && base ? await client.patchContact(record.data.id, changes, base.etag!) :
        await client.createContact(body, base ? { id: base.data.id, etag: base.etag! } : undefined);
      onSaved(saved);
    } catch (error) {
      const result = safeError(error);
      setFailure(result);
      if (result.status === 403) onForbidden();
      if (!record && result.status === 428) { setChoosing(true); setBase(undefined); }
      setBlocked(result.status === 412 || result.status === 403 || !result.status || result.status >= 500);
    } finally { setBusy(false); }
  }
  return <form className="contact-form" onSubmit={save}>
    <label>Tên liên hệ<input disabled={busy || readOnly} autoFocus required value={name} onChange={e => setName(e.target.value)} /></label>
    <label>Email<input disabled={busy || readOnly} type="email" value={email} onChange={e => setEmail(e.target.value)} /></label>
    <label>Điện thoại<input disabled={busy || readOnly} type="tel" value={phone} onChange={e => setPhone(e.target.value)} /></label>
    {failure && <p role="alert">{mutationMessage(failure)}</p>}
    {choosing && <fieldset><legend>Chọn liên hệ để đối chiếu</legend>
      {choices.map(item => <button key={item.id} type="button" disabled={busy || readOnly} onClick={() => void read(item.id)}>Chọn {item.displayName}</button>)}
    </fieldset>}
    {review && base && <section><h4>Dữ liệu hiện tại</h4><dl><dt>Tên</dt><dd>{base.data.displayName}</dd><dt>Email</dt><dd>{base.data.email ?? '—'}</dd><dt>Điện thoại</dt><dd>{base.data.phone ?? '—'}</dd></dl></section>}
    {blocked && failure?.status !== 403 && <button type="button" disabled={busy} onClick={() => void read(base?.data.id)}>Tải lại dữ liệu</button>}
    <div className="contact-actions"><button type="submit" disabled={busy || readOnly || unchanged || blocked || (choosing && !base)}>{record ? 'Lưu' : base ? 'Cập nhật ' + base.data.displayName : 'Tạo'}</button>
    <button type="button" disabled={busy} onClick={onCancel}>Hủy</button></div>
  </form>;
}
