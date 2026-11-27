import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from '@tanstack/react-router';
import { createAppRouter } from './router';
import { ConfiguredAuthProvider } from './auth/AuthProvider.tsx';

declare global { interface Window { __crmOidcCallback?: string } }
const callbackUrl = window.__crmOidcCallback;
delete window.__crmOidcCallback;

const container = document.getElementById('root');

if (container === null) {
  throw new Error('the application root element is missing');
}

createRoot(container).render(
  <StrictMode>
    <ConfiguredAuthProvider callbackUrl={callbackUrl}><RouterProvider router={createAppRouter()} /></ConfiguredAuthProvider>
  </StrictMode>
);
