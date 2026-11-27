import { useState } from 'react';
import type { components } from '@crm-centric/crm-client';
import type { CrmClient } from './crmClient';
import type { RecordWrite } from './recordPanels';
import { DefinitionForm } from './DefinitionForm';
export type Definition = components['schemas']['FieldDefinition'];
interface Props { client: CrmClient; fields: Definition[]; disabled: boolean; run: RecordWrite; refresh(): Promise<void> }
export function CustomFieldSettings({ client, fields, disabled, run, refresh }: Props) {
  const [open, setOpen] = useState(false);
  const [forbidden, setForbidden] = useState(false);
  return <div><button aria-expanded={open} onClick={() => setOpen(!open)}>Định nghĩa trường</button>
    <div hidden={!open}><ul>{fields.map(field => <li key={field.id}>{field.displayName}</li>)}</ul>
      <DefinitionForm disabled={disabled || forbidden} save={value => run(async () => { await client.createField(value); }, refresh, () => setForbidden(true))} /></div>
  </div>;
}
