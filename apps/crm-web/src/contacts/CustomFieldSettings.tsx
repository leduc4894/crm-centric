import { useState } from 'react';
import type { components } from '@crm-centric/crm-client';
import type { CrmClient } from './crmClient';
import type { RecordWrite } from './recordPanels';
export type Definition = components['schemas']['FieldDefinition'];
interface Props { client: CrmClient; fields: Definition[]; disabled: boolean; run: RecordWrite; refresh(): Promise<void> }
export function CustomFieldSettings({ client, fields, disabled, run, refresh }: Props) {
  const [open, setOpen] = useState(false);
  const [forbidden, setForbidden] = useState(false);
  const [key, setKey] = useState(''), [name, setName] = useState('');
  const [type, setType] = useState<Definition['dataType']>('string');
  const [required, setRequired] = useState(false);
  return <div><button aria-expanded={open} onClick={() => setOpen(!open)}>Định nghĩa trường</button>
    {open && <><ul>{fields.map(field => <li key={field.id}>{field.displayName}</li>)}</ul>
      <form className="contact-form" onSubmit={event => {
        event.preventDefault();
        if (disabled || forbidden) return;
        void run(async () => {
          await client.createField({ key, displayName: name, dataType: type, required });
          setKey(''); setName(''); setRequired(false); setType('string');
        }, refresh, () => setForbidden(true));
      }}><fieldset disabled={disabled || forbidden}><legend>Tạo định nghĩa</legend>
          <label>Khóa trường<input required pattern="[a-z][a-z0-9_]{0,63}" value={key} onChange={e => setKey(e.target.value)} /></label>
          <label>Tên trường<input required maxLength={200} value={name} onChange={e => setName(e.target.value)} /></label>
          <label>Kiểu dữ liệu<select value={type} onChange={e => setType(e.target.value as Definition['dataType'])}>
            <option value="string">Văn bản</option><option value="number">Số</option><option value="boolean">Đúng/sai</option><option value="date">Ngày</option></select></label>
          <label>Bắt buộc<input type="checkbox" checked={required} onChange={e => setRequired(e.target.checked)} /></label>
          <button type="submit">Tạo trường</button></fieldset></form></>}
  </div>;
}
