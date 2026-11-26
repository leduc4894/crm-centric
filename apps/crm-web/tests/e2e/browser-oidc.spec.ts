import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { expect, test, type Page } from '@playwright/test';

async function verifyCleanupBeforeResources(page: Page, url: string) {
  await page.addInitScript(() => {
    const original = history.replaceState.bind(history);
    history.replaceState = (...args) => { performance.mark('callback-cleaned'); original(...args); };
  });
  const requests: { url: string; referer: string }[] = [];
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
