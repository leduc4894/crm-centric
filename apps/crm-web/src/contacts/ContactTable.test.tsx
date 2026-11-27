// @vitest-environment jsdom
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useTable } from '@tanstack/react-table';
import { afterEach, expect, it, vi } from 'vitest';
import type { Contact } from './ContactForm';
import { ContactTable } from './ContactTable';

vi.mock('@tanstack/react-table', { spy: true });

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

const zoe: Contact = {
  id: 'ABCDEF00-0000-4000-8000-000000000001',
  displayName: 'Zoe',
  email: 'zoe@example.com',
  phone: '0123456789',
  version: 1,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};
const anna: Contact = { ...zoe, id: 'abcdef00-0000-4000-8000-000000000002', displayName: 'Anna' };

it('renders_server_order_with_stable_contact_ids', () => {
  const { rerender } = render(<ContactTable items={[zoe, anna]} />);
  const [zoeLink, annaLink] = screen.getAllByRole('link');
  expect(zoeLink?.textContent).toBe('Zoe');
  expect(zoeLink?.getAttribute('href')).toBe('/app/contacts/ABCDEF00-0000-4000-8000-000000000001');
  expect(annaLink?.textContent).toBe('Anna');
  expect(annaLink?.getAttribute('href')).toBe('/app/contacts/abcdef00-0000-4000-8000-000000000002');

  rerender(<ContactTable items={[anna, { ...zoe, displayName: 'Zoe Updated' }]} />);
  const links = screen.getAllByRole('link');
  expect(links.map(link => link.textContent)).toEqual(['Anna', 'Zoe Updated']);
  expect(links[0]).toBe(annaLink);
  expect(links[1]).toBe(zoeLink);
});

it('renders_nulls_and_markup_safely', async () => {
  const user = userEvent.setup();
  const displayName = '<script>example</script>';
  render(<ContactTable items={[{ ...zoe, displayName, email: null, phone: null }]} />);

  const table = screen.getByRole('table');
  expect(within(table).getAllByRole('columnheader').map(header => header.textContent))
    .toEqual(['Tên liên hệ', 'Email', 'Điện thoại']);
  expect(within(table).getAllByRole('cell').map(cell => cell.textContent))
    .toEqual([displayName, '—', '—']);
  expect(table.querySelector('script')).toBeNull();

  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Danh sách liên hệ' }));
  await user.tab();
  expect(document.activeElement).toBe(within(table).getByRole('link', { name: displayName }));
});

it('uses_table_row_model_without_client_transforms', () => {
  const items = [zoe, anna];
  render(<ContactTable items={items} />);

  const hook = vi.mocked(useTable);
  const options = hook.mock.lastCall?.[0];
  const table = hook.mock.results.at(-1)?.value as ReturnType<typeof useTable> | undefined;
  expect(options?.data).toBe(items);
  expect(options?.features).toEqual({});
  expect(table?.getRowModel()).toBe(table?.getCoreRowModel());
  expect(table?.getRowModel().rows.map(row => row.id)).toEqual([
    'ABCDEF00-0000-4000-8000-000000000001',
    'abcdef00-0000-4000-8000-000000000002',
  ]);
});
