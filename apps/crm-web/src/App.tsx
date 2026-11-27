import { TenantProvider } from './tenant/TenantProvider.tsx';
import { TenantPicker } from './tenant/TenantPicker.tsx';
import { HealthStatus } from './health/HealthStatus.tsx';
import { ContactShell, navigate } from './contacts/ContactShell';
import './contacts/shell.css';

export function App() {
  return <TenantProvider><div className="crm-shell">
    <aside><header><h1><span className="brand-mark" aria-hidden="true">C</span> CRM Centric</h1></header>
      <nav aria-label="Primary"><a href="/" onClick={navigate}>Platform</a><a href="/app/contacts" onClick={navigate}>Liên hệ</a></nav>
      <TenantPicker />
    </aside>
    <main><ContactShell /><HealthStatus /></main>
  </div></TenantProvider>;
}
