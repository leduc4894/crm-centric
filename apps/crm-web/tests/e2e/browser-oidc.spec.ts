import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { expect, test, type Page } from '@playwright/test';

async function verifyCleanupBeforeResources(page: Page, url: string) {
  await page.addInitScript(() => {
    const recorded: string[] = [];
    Object.assign(window, { __testHistoryUrls: recorded });
    const originalReplace = history.replaceState.bind(history);
    const originalPush = history.pushState.bind(history);
    history.replaceState = (...args) => { recorded.push(JSON.stringify(args)); performance.mark('callback-cleaned'); originalReplace(...args); };
    history.pushState = (...args) => { recorded.push(JSON.stringify(args)); originalPush(...args); };
  });
  const requests: { url: string; referer: string }[] = [];
  const logs: string[] = [];
  page.on('console', message => logs.push(message.text()));
  page.on('request', (request) => {
    if (request.isNavigationRequest()) return;
    requests.push({ url: request.url(), referer: request.headers().referer ?? '' });
  });
  await page.goto(`${url}/auth/callback?code=synthetic-callback-sentinel&state=synthetic-state-sentinel`);
  await expect(page).toHaveURL(/\/auth\/callback$/);
  await expect(page.getByRole('heading', { name: 'CRM Centric' })).toBeVisible();
  expect(requests.length).toBeGreaterThan(0);
  const timing = await page.evaluate(() => ({
    cleaned: performance.getEntriesByName('callback-cleaned')[0]?.startTime,
    resources: performance.getEntriesByType('resource').map((entry) => ({ name: entry.name, start: entry.startTime }))
  }));
  expect(timing.cleaned).toBeDefined();
  expect(timing.resources.length).toBeGreaterThan(0);
  for (const resource of timing.resources) expect(resource.start, resource.name).toBeGreaterThanOrEqual(timing.cleaned!);
  const links = await page.locator('a').evaluateAll(nodes => nodes.map(node => (node as HTMLAnchorElement).href));
  await page.getByRole('link', { name: 'Liên hệ' }).click();
  await expect(page).toHaveURL(/\/app\/contacts$/);
  await page.goBack();
  await expect(page).toHaveURL(/\/auth\/callback$/);
  const historyWrites = await page.evaluate(() => Reflect.get(window, '__testHistoryUrls') as string[]);
  for (const value of [...links, ...logs, ...historyWrites]) {
    expect(value).not.toContain('synthetic-callback-sentinel');
    expect(value).not.toContain('synthetic-state-sentinel');
  }
  for (const request of requests) {
    expect(request.url).not.toContain('synthetic-callback-sentinel');
    expect(request.referer).not.toContain('synthetic-callback-sentinel');
    expect(request.referer).not.toContain('synthetic-state-sentinel');
  }
}

test('cleans callback before development resources are requested', async ({ page, baseURL }) => {
  await verifyCleanupBeforeResources(page, baseURL!);
});

test('cleans callback before built resources are requested', async ({ page }) => {
  const server = createServer(async (request, response) => {
    const path = new URL(request.url ?? '/', 'http://localhost').pathname;
    const name = path.startsWith('/assets/') && /^\/assets\/[\w.-]+$/.test(path) ? path : '/index.html';
    try {
      const content = await readFile(new URL(`../../dist${name}`, import.meta.url));
      response.setHeader('content-type', name.endsWith('.js') ? 'text/javascript' : 'text/html'); response.end(content);
    } catch { response.writeHead(404); response.end(); }
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  try {
    const address = server.address(); if (!address || typeof address === 'string') throw new Error('test server failed');
    await verifyCleanupBeforeResources(page, `http://127.0.0.1:${address.port}`);
  } finally { await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve())); }
});

test('real provider signs in isolated tenants and terminates provider session', async ({ browser }) => {
  test.skip(process.env['CRM_REAL_OIDC_TEST'] !== '1', 'Requires isolated real-provider fixture');
  test.setTimeout(900_000);
  const { RealOidcFixture } = await import('./realOidcFixture.ts');
  const fixture = new RealOidcFixture();
  await fixture.run(browser);
});


test('redacts a forced browser credential-fill failure', async ({ page }) => {
  const { privateOperation } = await import('./realOidcFixture.ts');
  const sentinel = 'SYNTHETIC_PASSWORD_SECRET_8c0b';
  let captured: unknown;
  try { await privateOperation(() => page.locator('#not-present').fill(sentinel, { timeout: 250 })); } catch (error) { captured = error; }
  expect(captured).toBeInstanceOf(Error);
  const failure = captured as Error;
  expect(failure.message).toBe('E2E_PRIVATE_OPERATION_FAILED');
  expect(failure.stack?.includes(sentinel)).toBe(false);
  expect(failure.cause).toBeUndefined();
});
