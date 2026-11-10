import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';

const container = document.getElementById('root');

if (container === null) {
  throw new Error('the application root element is missing');
}

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>
);
