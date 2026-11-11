import { defineConfig, devices } from '@playwright/test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = fileURLToPath(new URL('../..', import.meta.url));

/**
 * Browser end-to-end configuration.
 *
 * The browser runs against the development server on `localhost:5173`, which is the same origin
 * the application uses in development. The development server is started by Playwright with the
 * strict port, so the suite never runs against a foreign listener that happens to answer on 5173.
 *
 * The browser version is pinned by the `@playwright/test` version in the workspace lockfile; the
 * browser binary itself is a local prerequisite and is not committed.
 */
export default defineConfig({
  testDir: './tests/e2e',
  testMatch: '**/*.spec.ts',
  fullyParallel: false,
  workers: 1,
  forbidOnly: true,
  retries: 0,
  reporter: process.env.PLAYWRIGHT_JSON_OUTPUT_NAME ? [['json']] : [['list']],
  timeout: 30_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'off',
    video: 'off',
    screenshot: 'off'
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    }
  ],
  webServer: {
    command: 'npm run dev',
    cwd: repositoryRoot,
    url: 'http://localhost:5173/',
    reuseExistingServer: false,
    timeout: 120_000
  }
});
