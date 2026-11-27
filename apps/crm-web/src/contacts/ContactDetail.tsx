import { useCallback, useEffect, useRef, useState } from 'react';
import { ContactForm, mutationMessage, safeError, type Snapshot } from './ContactForm';
import { navigate, openContacts } from './ContactShell';
import { ContactTimeline } from './ContactTimeline';
import { ContactAttributes } from './ContactAttributes';
import type { RecordWrite, RegisterPanelRead } from './recordPanels';
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
  const [panelFailure, setPanelFailure] = useState<CrmError | null>(null);
  const [reconcile, setReconcile] = useState<(() => Promise<void>) | null>(null);
  const panelReads = useRef(new Set<() => Promise<void>>());
  const register = useCallback<RegisterPanelRead>(read => {
    panelReads.current.add(read);
    return () => { panelReads.current.delete(read); };
  }, []);
  async function refreshAttributes(extra?: () => Promise<void>) {
    const reads = new Set(panelReads.current);
    if (extra) reads.add(extra);
    await Promise.all(Array.from(reads, read => read()));
  }
  function accept(result: Snapshot) {
    if (result.data.id.toLowerCase() !== id.toLowerCase()) throw new CrmError('WEB_CRM_CONTRACT_INVALID');
    setRecord(result);
  }
  async function updateVersion(etag: string | null) {
    if (etag) setRecord(old => old && { ...old, etag });
    else {
      const snapshot = await client.getContact(id);
      await refreshAttributes();
      accept(snapshot);
    }
  }
  const run: RecordWrite = async (write, refresh, onForbidden) => {
    if (!record || busy || readOnly || reconcile || editing || deleteFailed) return;
    setBusy(true); setPanelFailure(null);
    try {
      await write(await client.requireETag(id, record.etag));
      await refresh();
    } catch (cause) {
      const error = safeError(cause);
      setPanelFailure(error);
      if (error.status === 403) {
        if (onForbidden) onForbidden(); else setReadOnly(true);
      } else if (error.status === 412 || error.status === 404 || !error.status || error.status >= 500) {
        setReconcile(() => async () => {
          let snapshot: Snapshot;
          try { snapshot = await client.getContact(id); }
          catch (cause) {
            const readError = safeError(cause);
            if (readError.status === 403 || readError.status === 404) {
              setRecord(null); setFailure(readError); setReconcile(null);
              return;
            }
            throw cause;
          }
          await refreshAttributes(refresh);
          accept(snapshot);
        });
      }
    } finally { setBusy(false); }
  };
  async function review() {
    if (!reconcile || busy) return;
    setBusy(true);
    try {
      await reconcile(); setReconcile(null); setPanelFailure(null);
    } catch (cause) { setPanelFailure(safeError(cause)); }
    finally { setBusy(false); }
  }
  const locked = busy || !!reconcile || readOnly || editing || deleteFailed;
  async function remove() {
    if (!record || busy || readOnly) return;
    setConfirm(false); setBusy(true);
    try {
      await client.deleteContact(id, await client.requireETag(id, record.etag));
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
    {panelFailure && <p role="alert">{mutationMessage(panelFailure)} {reconcile && <button disabled={busy} onClick={() => void review()}>Đối chiếu dữ liệu</button>}</p>}
    {!record && !failure && <p role="status">Đang tải…</p>}
    {record && <div className="contact-record"><div className="contact-profile"><section className="contact-card">
      <h3>Thông tin liên hệ</h3>
      {editing ? <ContactForm client={client} readOnly={readOnly} record={record} refreshAttributes={refreshAttributes} onForbidden={() => setReadOnly(true)} onCancel={() => setEditing(false)}
        onSaved={saved => { setRecord(saved); setEditing(false); }} /> :
        <><dl><dt>Email</dt><dd>{record.data.email ?? '—'}</dd><dt>Điện thoại</dt><dd>{record.data.phone ?? '—'}</dd></dl>
        {!readOnly && <div className="contact-actions"><button disabled={locked} onClick={() => setEditing(true)}>Chỉnh sửa</button>
        <button disabled={locked} onClick={() => setConfirm(true)}>Xóa liên hệ</button></div>}</>}
    </section><ContactAttributes client={client} id={id} disabled={locked} run={run} register={register} onRecord={accept} /></div>
      <ContactTimeline client={client} id={id} disabled={locked} run={run} onVersion={updateVersion} /></div>}
    {confirm && record && <DeleteDialog name={record.data.displayName} onCancel={() => setConfirm(false)} onConfirm={() => void remove()} />}
  </>;
}
