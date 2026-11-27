import { Link, Outlet } from '@tanstack/react-router';
import { TenantProvider } from './tenant/TenantProvider.tsx';
import { TenantPicker } from './tenant/TenantPicker.tsx';
import { HealthStatus } from './health/HealthStatus.tsx';

export function App() {
  return <TenantProvider><div className="crm-shell">
    <aside><header><h1><span className="brand-mark" aria-hidden="true">C</span> CRM Centric</h1></header>
      <nav aria-label="Primary"><Link to="/">Platform</Link><Link to="/app/contacts">Liên hệ</Link><Link to="/app/settings/custom-fields">Trường tùy chỉnh</Link></nav>
      <TenantPicker />
    </aside>
    <main><Outlet /><HealthStatus /></main>
  </div></TenantProvider>;
}
