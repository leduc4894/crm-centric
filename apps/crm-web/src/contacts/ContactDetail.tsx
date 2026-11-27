import { useEffect, useRef, useState } from 'react';
import { ContactForm, mutationMessage, safeError, type Snapshot } from './ContactForm';
import { navigate, openContacts } from './ContactShell';
import { CrmError, type CrmClient } from './crmClient';

function DeleteDialog({ name, onCancel, onConfirm }: { name: string; onCancel(): void; onConfirm(): void }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current!;
    if (dialog.showModal) dialog.showModal(); else dialog.open = true;
    return () => dialog.close?.();
  }, []);
  return <dialog ref={ref} aria-labelledby="delete-title" onCancel={onCancel}>
    <h3 id="delete-title">Xóa {name}?</h3>
    <div className="contact-actions"><button autoFocus onClick={onCancel}>Hủy</button><button onClick={onConfirm}>Xác nhận xóa</button></div>
  </dialog>;
}
export function ContactDetail({ client, id }: { client: CrmClient; id: string }) {
  const [record, setRecord] = useState<Snapshot | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [failure, setFailure] = useState<CrmError | null>(null);
  const [editing, setEditing] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  const [readOnly, setReadOnly] = useState(false);
  const [deleteFailed, setDeleteFailed] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    void client.getContact(id, controller.signal).then(result => {
      if (result.data.id.toLowerCase() !== id.toLowerCase()) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
      if (!controller.signal.aborted) { setRecord(result); setDeleteFailed(false); setFailure(null); }
    }).catch(error => { if (!controller.signal.aborted) setFailure(safeError(error)); });
    return () => controller.abort();
  }, [id, client, attempt]);
  async function remove() {
    if (!record || busy || readOnly) return;
    setConfirm(false); setBusy(true);
    try {
      if (!record.etag) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
      await client.deleteContact(id, record.etag);
      openContacts();
    } catch (error) {
      const result = safeError(error);
      setFailure(result); setDeleteFailed(true);
      if (result.status === 403) setReadOnly(true);
    } finally { setBusy(false); }
  }
  return <>
    <a href="/app/contacts" onClick={navigate}>Liên hệ</a>
    <h2>{record?.data.displayName ?? 'Hồ sơ liên hệ'}</h2>
    {failure && <p role="alert">{deleteFailed ? mutationMessage(failure) : failure.message} <button onClick={() => { setRecord(null); setFailure(null); setAttempt(n => n + 1); }}>Tải lại dữ liệu</button></p>}
    {!record && !failure && <p role="status">Đang tải…</p>}
    {record && <div className="contact-record"><div className="contact-profile"><section className="contact-card">
      <h3>Thông tin liên hệ</h3>
      {editing ? <ContactForm client={client} readOnly={readOnly} record={record} onForbidden={() => setReadOnly(true)} onCancel={() => setEditing(false)}
        onSaved={saved => { setRecord(saved); setEditing(false); }} /> :
        <><dl><dt>Email</dt><dd>{record.data.email ?? '—'}</dd><dt>Điện thoại</dt><dd>{record.data.phone ?? '—'}</dd></dl>
        {!readOnly && <div className="contact-actions"><button disabled={busy || deleteFailed} onClick={() => setEditing(true)}>Chỉnh sửa</button>
        <button disabled={busy || deleteFailed} onClick={() => setConfirm(true)}>Xóa liên hệ</button></div>}</>}
    </section></div></div>}
    {confirm && record && <DeleteDialog name={record.data.displayName} onCancel={() => setConfirm(false)} onConfirm={() => void remove()} />}
  </>;
}
