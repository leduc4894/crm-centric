import { HealthStatus } from './health/HealthStatus.tsx';

/**
 * Application shell.
 *
 * The shell provides the heading and the minimal navigation. The operational connection status,
 * the retry action and the health states are delivered by the health UI slice.
 */
export function App() {
  return (
    <>
      <header>
        <h1>CRM Centric</h1>
      </header>
      <nav aria-label="Primary">
        <ul>
          <li>
            <span aria-current="page">Platform</span>
          </li>
        </ul>
      </nav>
      <main>
        <HealthStatus />
      </main>
    </>
  );
}
