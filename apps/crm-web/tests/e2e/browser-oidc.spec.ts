import { expect, test } from '@playwright/test';

test('cleans callback before development modules are requested', async ({ page }) => {
  const requests: { url: string; referer: string }[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest()) return;
    requests.push({ url: request.url(), referer: request.headers().referer ?? '' });
  });
  await page.goto('/auth/callback?code=synthetic-callback-sentinel&state=synthetic-state-sentinel');
  await expect(page).toHaveURL(/\/auth\/callback$/);
  await expect(page.getByRole('heading', { name: 'CRM Centric' })).toBeVisible();
  expect(requests.length).toBeGreaterThan(0);
  for (const request of requests) {
    expect(request.url).not.toContain('synthetic-callback-sentinel');
    expect(request.referer).not.toContain('synthetic-callback-sentinel');
    expect(request.referer).not.toContain('synthetic-state-sentinel');
  }
});
