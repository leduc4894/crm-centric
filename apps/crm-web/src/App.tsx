/**
 * Application shell.
 *
 * Task 5 delivers the platform shell only: the heading and the minimal navigation. The operational
 * connection status, the retry action and the health states are delivered by the health UI slice
 * and are deliberately not implemented here.
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
        <p>The platform shell is available. Operational health states are not part of this slice.</p>
      </main>
    </>
  );
}
