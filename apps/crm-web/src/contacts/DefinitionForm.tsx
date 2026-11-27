import { useRef } from 'react';
import { useForm, useStore } from '@tanstack/react-form';
import type { Definition } from './CustomFieldSettings';

type Values = { key: string; displayName: string; dataType: Definition['dataType']; required: boolean };
const defaults: Values = { key: '', displayName: '', dataType: 'string', required: false };
export function DefinitionForm({ disabled, save }: { disabled: boolean; save(value: Values): Promise<boolean> }) {
  const inFlight = useRef(false);
  const form = useForm({
    defaultValues: defaults,
    onSubmit: async ({ value }) => {
      if (disabled || inFlight.current || !/^[a-z][a-z0-9_]{0,63}$/.test(value.key) || !value.displayName || Array.from(value.displayName).length > 200) return;
      inFlight.current = true;
      try { if (await save(value)) form.reset(); } finally { inFlight.current = false; }
    },
  });
  const submitting = useStore(form.store, state => state.isSubmitting);
  return <form className="contact-form" onSubmit={event => { event.preventDefault(); void form.handleSubmit(); }}><fieldset disabled={disabled || submitting}><legend>Tạo định nghĩa</legend>
    <form.Field name="key">{field => <label>Khóa trường<input required pattern="[a-z][a-z0-9_]{0,63}" value={field.state.value} onBlur={field.handleBlur} onChange={e => field.handleChange(e.target.value)} /></label>}</form.Field>
    <form.Field name="displayName">{field => <label>Tên trường<input required value={field.state.value} onBlur={field.handleBlur} onChange={e => field.handleChange(e.target.value)} /></label>}</form.Field>
    <form.Field name="dataType">{field => <label>Kiểu dữ liệu<select value={field.state.value} onBlur={field.handleBlur} onChange={e => field.handleChange(e.target.value as Definition['dataType'])}>
      <option value="string">Văn bản</option><option value="number">Số</option><option value="boolean">Đúng/sai</option><option value="date">Ngày</option></select></label>}</form.Field>
    <form.Field name="required">{field => <label>Bắt buộc<input type="checkbox" checked={field.state.value} onBlur={field.handleBlur} onChange={e => field.handleChange(e.target.checked)} /></label>}</form.Field>
    <button type="submit">Tạo trường</button></fieldset></form>;
}
