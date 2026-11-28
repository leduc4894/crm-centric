import { createContext, useContext, type ReactNode } from 'react';
import { render } from '@testing-library/react';
import { createRootRoute, createRoute, createRouter, Outlet, RouterProvider } from '@tanstack/react-router';
import { ContactShell } from './ContactShell';

// jsdom does not implement scrolling; route tests do not depend on viewport movement.
window.scrollTo = () => {};

const Refresh = createContext(0);
function ScopedShell({ recordId }: { recordId?: string }) { useContext(Refresh); return <ContactShell recordId={recordId ?? null} />; }
const root = createRootRoute({ component: () => <Outlet /> });
const list = createRoute({ getParentRoute: () => root, path: '/app/contacts', component: () => <ScopedShell /> });
const detail = createRoute({ getParentRoute: () => root, path: '/app/contacts/$contactId', component: () => <ScopedShell recordId={detail.useParams().contactId} /> });
const routeTree = root.addChildren([list, detail]);
export function renderContactRoute(wrap: (children: ReactNode) => ReactNode = children => children) {
  const router = createRouter({ routeTree });
  let revision = 0;
  const tree = () => <Refresh.Provider value={revision}>{wrap(<RouterProvider router={router} />)}</Refresh.Provider>;
  const view = render(tree());
  return { ...view, router, rerender: () => { revision++; view.rerender(tree()); } };
}
