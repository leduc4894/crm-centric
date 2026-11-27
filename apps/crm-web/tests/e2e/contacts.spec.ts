import { expect, test, type Page } from '@playwright/test';
import { privateOperation, RealOidcFixture } from './realOidcFixture.ts';

const contact = 'Synthetic browser contact';
const note = '<img src=x onerror="document.body.dataset.xss=1"> — ghi chú';
const precise = '1234567890123456789012345.6789';
const button = (page: Page, name: string) => page.getByRole('button', { name, exact: true });
const list = (page: Page) => page.getByRole('navigation').getByRole('link', { name: 'Liên hệ' }).click();

function signal() {
  let resolve!: () => void;
  const promise = new Promise<void>(done => { resolve = done; });
  return { promise, resolve };
}

async function openContact(page: Page) {
  await list(page);
  await page.getByLabel('Tìm kiếm').fill(contact);
  await button(page, 'Lọc').click();
  await page.getByRole('link', { name: contact }).click();
  await expect(page.getByRole('heading', { name: contact })).toBeVisible();
}

test('contact persists across sessions, rejects stale notes and isolates delayed tenant reads', async ({ browser }) => {
  test.skip(process.env['CRM_REAL_OIDC_TEST'] !== '1', 'Requires isolated real-provider stack');
  test.setTimeout(900_000);
  const fixture = new RealOidcFixture();
  await fixture.withContactSessions(browser, async (first: Page, second: Page) => {
    first.setDefaultTimeout(15_000);
    second.setDefaultTimeout(15_000);
    await button(first, 'Tenant A').click();
    await list(first);
    await button(first, 'Thêm liên hệ').click();
    await expect(first.getByLabel('Tên liên hệ')).toBeFocused();
    await first.getByLabel('Tên liên hệ').fill(contact);
    await button(first, 'Tạo').press('Enter');
    await expect(first.getByRole('heading', { name: contact })).toBeVisible();
    await openContact(first);
    await first.getByLabel('Tên thẻ mới').fill('Browser tag');
    await button(first, 'Tạo thẻ').click();
    await first.getByLabel('Thẻ có sẵn').selectOption({ label: 'Browser tag' });
    await button(first, 'Gắn thẻ').click();
    await expect(button(first, 'Gỡ Browser tag')).toBeVisible();
    await button(first, 'Định nghĩa trường').click();
    await first.getByLabel('Khóa trường').fill('browser_number');
    await first.getByLabel('Tên trường').fill('Browser number');
    await first.getByLabel('Kiểu dữ liệu').selectOption('number');
    await button(first, 'Tạo trường').click();
    await first.getByLabel('Browser number').fill(precise);
    await button(first, 'Lưu Browser number').click();
    await expect(button(first, 'Lưu Browser number')).toBeDisabled();
    await button(second, 'Tenant A').click();
    await openContact(second);
    await second.getByLabel('Ghi chú').fill('Unsubmitted stale draft');
    await first.getByLabel('Ghi chú').fill(note);
    await button(first, 'Thêm ghi chú').click();
    await expect(first.getByText(note, { exact: true })).toBeVisible();
    const stale = second.waitForResponse(r => r.url().endsWith('/notes') && r.request().method() === 'POST');
    await button(second, 'Thêm ghi chú').click();
    expect((await stale).status()).toBe(412);
    await expect(second.getByLabel('Ghi chú')).toHaveValue('Unsubmitted stale draft');
    let additionalNotes = 0;
    const countNotes = (request: import('@playwright/test').Request) => { if (request.url().endsWith('/notes') && request.method() === 'POST') additionalNotes++; };
    second.on('request', countNotes);
    await button(second, 'Đối chiếu dữ liệu').click();
    await expect(second.getByLabel('Ghi chú')).toHaveValue('Unsubmitted stale draft');
    await expect(button(second, 'Thêm ghi chú')).toBeEnabled();
    expect(additionalNotes).toBe(0);
    second.off('request', countNotes);


    await first.reload();
    await button(first, 'Sign in').click();
    await button(first, 'Tenant A').click();
    await openContact(first);
    await expect(button(first, 'Gỡ Browser tag')).toBeVisible();
    await expect(first.getByLabel('Browser number')).toHaveValue(precise);
    await expect(first.getByText(note, { exact: true })).toBeVisible();
    expect(await first.locator('body').getAttribute('data-xss')).toBeNull();
    await first.setViewportSize({ width: 390, height: 844 });
    expect(await first.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await first.getByLabel('Ghi chú').focus();
    await first.getByLabel('Ghi chú').press('a');
    await first.keyboard.press('Tab');
    await expect(button(first, 'Thêm ghi chú')).toBeFocused();

    await list(first);
    const received = signal();
    const release = signal();
    const delivered = signal();
    await first.route(url => url.pathname.endsWith('/contacts') && url.searchParams.get('q') === contact, async route => {
      const response = await privateOperation(() => route.fetch());
      expect(response.status()).toBe(200);
      expect((await response.text()).includes(contact)).toBe(true);
      received.resolve();
      await release.promise;
      try { await privateOperation(() => route.fulfill({ response })); } finally { delivered.resolve(); }
    }, { times: 1 });
    await first.getByLabel('Tìm kiếm').fill(contact);
    await button(first, 'Lọc').click();
    await received.promise;
    try {
      await button(first, 'Tenant B').click();
      await expect(first.getByText('Chưa có liên hệ.')).toBeVisible();
      await expect(first.getByRole('link', { name: contact })).toHaveCount(0);
      await button(first, 'Tenant A').click();
      await first.getByLabel('Tìm kiếm').fill('no matching synthetic contact');
      await button(first, 'Lọc').click();
      await expect(first.getByText('Chưa có liên hệ.')).toBeVisible();
    } finally { release.resolve(); }
    await delivered.promise;
    await expect(first.getByRole('link', { name: contact })).toHaveCount(0);
    await button(first, 'Sign out').click();
    await expect(button(first, 'Sign in')).toBeVisible();
    await fixture.signInAgain(first);
    await button(first, 'Tenant A').click();
    await openContact(first);
    await expect(first.getByLabel('Browser number')).toHaveValue(precise);
  });
});
