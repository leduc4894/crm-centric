import { createRootRoute, createRoute, createRouter, type RouterHistory } from '@tanstack/react-router';
import { App } from './App';
import { ContactShell } from './contacts/ContactShell';
import { CustomFieldsPage } from './settings/CustomFieldsPage';
import { contactIdPattern } from './contacts/crmClient';

const rootRoute = createRootRoute({ component: App, notFoundComponent: NotFound });
function NotFound() { return <p>Không tìm thấy trang.</p>; }
const platformRoute = createRoute({ getParentRoute: () => rootRoute, path: '/', component: () => null });
const callbackRoute = createRoute({ getParentRoute: () => rootRoute, path: '/auth/callback', component: () => null });
const contactsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/app/contacts', component: () => <ContactShell /> });
const detailRoute = createRoute({ getParentRoute: () => rootRoute, path: '/app/contacts/$contactId', component: Detail });
function Detail() {
  const { contactId } = detailRoute.useParams();
  return contactIdPattern.test(contactId) ? <ContactShell recordId={contactId} /> : <NotFound />;
}
const settingsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/app/settings/custom-fields', component: CustomFieldsPage });
const routeTree = rootRoute.addChildren([platformRoute, callbackRoute, contactsRoute, detailRoute, settingsRoute]);
export function createAppRouter(history?: RouterHistory) {
  return createRouter({ routeTree, ...(history && { history }), defaultPreload: false });
}
export type AppRouter = ReturnType<typeof createAppRouter>;
declare module '@tanstack/react-router' { interface Register { router: AppRouter } }
