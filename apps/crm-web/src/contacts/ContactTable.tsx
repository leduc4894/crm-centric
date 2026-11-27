import { tableFeatures, useTable, type ColumnDef } from '@tanstack/react-table';
import type { Contact } from './ContactForm';
import { Link } from '@tanstack/react-router';

const features = tableFeatures({});
const columns: ColumnDef<typeof features, Contact>[] = [
  {
    id: 'displayName',
    header: 'Tên liên hệ',
    cell: ({ row }) => <Link to="/app/contacts/$contactId" params={{ contactId: row.id }}>{row.original.displayName}</Link>,
  },
  {
    id: 'email',
    header: 'Email',
    cell: ({ row }) => row.original.email ?? '—',
  },
  {
    id: 'phone',
    header: 'Điện thoại',
    cell: ({ row }) => row.original.phone ?? '—',
  },
];

export function ContactTable({ items }: { items: Contact[] }) {
  const table = useTable({
    features,
    columns,
    data: items,
    getRowId: contact => contact.id,
  });

  return (
    <div className="contact-table-scroll" role="region" aria-label="Danh sách liên hệ" tabIndex={0}>
      <table>
        <thead>
          {table.getHeaderGroups().map(group => (
            <tr key={group.id}>
              {group.headers.map(header => (
                <th key={header.id} scope="col"><table.FlexRender header={header} /></th>
              ))}
            </tr>
          ))}
        </thead>
        <tbody>
          {table.getRowModel().rows.map(row => (
            <tr key={row.id}>
              {row.getAllCells().map(cell => (
                <td key={cell.id}><table.FlexRender cell={cell} /></td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
