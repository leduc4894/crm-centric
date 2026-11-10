import { expect, test, type APIRequestContext, type APIResponse } from '@playwright/test';
import {
  readStackApiContainer,
  repositoryRoot,
  resolveStackProject
} from './stackProvenance.ts';

const READINESS_PATH = '/health/ready';
const LIVENESS_PATH = '/health/live';
const API_PORT = process.env['LOCAL_API_PORT'] ?? '5080';
const UPSTREAM_ORIGIN = `http://127.0.0.1:${API_PORT}`;

/**
 * Reads an endpoint of the local stack.
 *
 * A stack that is not running is reported as such instead of being reported as a failed check.
 */
async function readStack(request: APIRequestContext, path: string): Promise<APIResponse> {
  try {
    return await request.get(`${UPSTREAM_ORIGIN}${path}`, { timeout: 10_000 });
  } catch (error) {
    throw new Error(
      `the local stack is not running on ${UPSTREAM_ORIGIN}; start it with bash scripts/dev-up.sh`,
      { cause: error }
    );
  }
}

test.describe('the operational health states', () => {
  test('shows the server reason token and the server correlation of a 503 Problem', async ({ page }) => {
    await page.route(`**${READINESS_PATH}`, async (route) => {
      await route.fulfill({
        status: 503,
        contentType: 'application/problem+json',
        headers: { 'x-correlation-id': '0HNE5Q1J2K3L4' },
        body: JSON.stringify({
          type: 'urn:crm:problem:database-schema-not-ready',
          title: 'Dependency unavailable',
          status: 503,
          reasonToken: 'DATABASE_SCHEMA_NOT_READY',
          correlationId: '0HNE5Q1J2K3L4'
        })
      });
    });

    await page.goto('/');

    const status = page.getByRole('status');
    await expect(status).toContainText('DATABASE_SCHEMA_NOT_READY');
    await expect(status).toContainText('Server correlation: 0HNE5Q1J2K3L4');
  });

  test('shows the timeout classification when the body does not arrive in time', async ({ page }) => {
    const gate: { open: () => void } = { open: () => undefined };
    await page.route(`**${READINESS_PATH}`, async (route) => {
      await new Promise<void>((resolve) => {
        gate.open = resolve;
      });
      await route
        .fulfill({ status: 200, contentType: 'application/json', body: '{"status":"ready"}' })
        .catch(() => undefined);
    });

    const startedAt = Date.now();
    await page.goto('/');
    await expect(page.getByRole('status')).toContainText('WEB_REQUEST_TIMEOUT', { timeout: 15_000 });
    const elapsed = Date.now() - startedAt;

    // The deadline is five seconds: a deadline that is too short and a deadline that is too long
    // both have to fail this test.
    expect(elapsed, 'the deadline must not fire before the five second deadline').toBeGreaterThan(
      4_000
    );
    expect(elapsed, 'the deadline must fire within the five second deadline').toBeLessThan(9_000);
    await expect(page.getByRole('status')).not.toContainText('Server correlation:');
    gate.open();
  });

  test('never renders the raw problem payload', async ({ page }) => {
    const sentinel = 'SENTINEL_TOKEN_4f2c9a';
    await page.route(`**${READINESS_PATH}`, async (route) => {
      await route.fulfill({
        status: 503,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          type: 'urn:crm:problem:database-unavailable',
          title: '<img src=x onerror="window.__xss=1">',
          status: 503,
          reasonToken: 'DATABASE_UNAVAILABLE',
          correlationId: '0HNE5Q1J2K3L4',
          detail: `Bearer ${sentinel}\n    at Object.<anonymous> (/srv/app/index.js:1:1)`
        })
      });
    });

    await page.goto('/');
    await expect(page.getByRole('status')).toContainText('DATABASE_UNAVAILABLE');

    // The rendered markup is scanned, not only the visible text: a payload that reaches an
    // attribute must be caught as well.
    const documentHtml = await page.content();
    const applicationHtml = await page.locator('#root').innerHTML();
    expect(documentHtml).not.toContain(sentinel);
    expect(applicationHtml).not.toContain('<img');
    expect(applicationHtml).not.toContain('onerror');
    expect(applicationHtml).not.toContain('/srv/app/index.js');
    expect(applicationHtml).not.toContain('/health/ready');
    expect(await page.locator('img').count()).toBe(0);
    expect(await page.evaluate(() => (window as unknown as { __xss?: unknown }).__xss)).toBeUndefined();
  });
});

test.describe('the real local stack', () => {
  test('reads the readiness of the real API and PostgreSQL through the development proxy', async ({
    page,
    request
  }) => {
    const live = await readStack(request, LIVENESS_PATH);
    expect(live.status(), 'the local stack must answer the liveness probe').toBe(200);
    expect(await live.json()).toEqual({ status: 'live' });

    const upstreamReady = await readStack(request, READINESS_PATH);
    expect(upstreamReady.status(), 'the local stack must answer the readiness probe').toBe(200);
    expect(await upstreamReady.json()).toEqual({ status: 'ready' });

    // The upstream answers with the characteristics of our API: its own correlation identifier and
    // the no-store cache control of the operational contract.
    const upstreamCorrelationId = upstreamReady.headers()['x-correlation-id'];
    expect(upstreamCorrelationId, 'the API must answer with its own correlation identifier').toMatch(
      /^[0-9a-f]{32}$/
    );
    expect(upstreamReady.headers()['cache-control']).toContain('no-store');

    // The listener on the API port belongs to the stack: the API container of the compose project
    // of this checkout has to publish the port. A foreign container that publishes the same port
    // is not the stack and never satisfies this check.
    const project = resolveStackProject(repositoryRoot());
    const container = readStackApiContainer(project);
    expect(
      container,
      `the local stack must run its own API container of the compose project ${project}`
    ).not.toBeNull();
    expect(container?.ports ?? '', 'the API container of the stack must publish the API port').toContain(
      `127.0.0.1:${API_PORT}->`
    );

    const responsePromise = page.waitForResponse(
      (response) => new URL(response.url()).pathname === READINESS_PATH
    );
    await page.goto('/');
    const response = await responsePromise;

    expect(response.request().method()).toBe('GET');
    expect(new URL(response.url()).host).toBe('localhost:5173');
    expect(response.status()).toBe(200);
    expect(response.headers()['x-correlation-id']).toMatch(/^[0-9a-f]{32}$/);
    expect(await response.text()).toBe(await upstreamReady.text());

    await expect(page.getByRole('status')).toContainText('The platform API is ready.');

    // A path outside the proxy allowlist is answered by the development server itself, so the
    // browser cannot reach anything but the two operational endpoints.
    const notProxied = await page.request.get(`${READINESS_PATH}/extra`);
    expect(notProxied.headers()['x-correlation-id']).toBeUndefined();
    expect(await notProxied.text()).not.toContain('"ready"');
  });
});
