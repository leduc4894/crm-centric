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
  await page.getByRole('link', { name: contact, exact: true }).click();
  await expect(page.getByRole('heading', { name: contact, exact: true })).toBeVisible();
}

type ListPage = { items: { id: string; displayName: string }[]; nextCursor: string | null };

async function checkContactTable(page: Page) {
  const prefix = 'Synthetic browser';
  const markup = prefix + ' <b>literal</b>';
  // The default server page holds 20. The original contact plus these 20 require a real continuation.
  for (let index = 1; index <= 20; index++) {
    const name = index === 20 ? markup : prefix + ' ' + String(index).padStart(2, '0');
    await list(page);
    await button(page, 'Thêm liên hệ').click();
    await page.getByLabel('Tên liên hệ').fill(name);
    await button(page, 'Tạo').click();
    await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
  }
  await list(page);
  await page.setViewportSize({ width: 390, height: 844 });
  const waitForPage = (cursor: string | null) => page.waitForResponse(response => {
    const url = new URL(response.url());
    return response.request().method() === 'GET' && url.pathname.endsWith('/contacts') &&
      url.searchParams.get('q') === prefix && url.searchParams.get('cursor') === cursor;
  });
  const firstResponse = waitForPage(null);
  await page.getByLabel('Tìm kiếm').fill(prefix);
  await button(page, 'Lọc').click();
  const first = await firstResponse;
  expect(first.status()).toBe(200);
  const firstPage = await first.json() as ListPage;
  expect(firstPage.items).toHaveLength(20);
  expect(typeof firstPage.nextCursor).toBe('string');
  const region = page.getByRole('region', { name: 'Danh sách liên hệ' });
  const table = region.getByRole('table');
  const links = table.getByRole('link');
  await expect(links).toHaveText(firstPage.items.map(item => item.displayName));
  await expect(table.getByRole('columnheader')).toHaveText(['Tên liên hệ', 'Email', 'Điện thoại']);
  await expect(table.locator('b')).toHaveCount(0);
  const secondResponse = waitForPage(firstPage.nextCursor);
  await button(page, 'Tải thêm').click();
  const second = await secondResponse;
  expect(second.status()).toBe(200);
  const secondPage = await second.json() as ListPage;
  expect(secondPage.items).toHaveLength(1);
  expect(secondPage.nextCursor).toBeNull();
  await expect(links).toHaveText([...firstPage.items, ...secondPage.items].map(item => item.displayName));
  await expect(button(page, 'Tải thêm')).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await button(page, 'Lọc').focus();
  await page.keyboard.press('Tab');
  await expect(region).toBeFocused();
  await region.press('End');
  for (let i = 0; i < 8; i++) await region.press('ArrowRight');
  await expect.poll(() => region.evaluate(element => {
    const lastColumn = element.querySelector('th:last-child')!;
    return element.scrollLeft > 0 && lastColumn.getBoundingClientRect().right <= element.getBoundingClientRect().right + 1;
  })).toBe(true);
  await page.keyboard.press('Tab');
  const link = links.first();
  await expect(link).toBeFocused();
  await expect(link).toHaveText(markup);
  await expect(link).toHaveAttribute('href', '/app/contacts/' + firstPage.items[0]!.id);
  const popup = page.context().waitForEvent('page');
  await link.click({ modifiers: [process.platform === 'darwin' ? 'Meta' : 'Control'] });
  const newTab = await popup;
  await expect(newTab).toHaveURL(new RegExp('/app/contacts/' + firstPage.items[0]!.id + '$'));
  await newTab.close();
  await expect(table).toBeVisible();
  await link.press('Enter');
  await expect(page.getByRole('heading', { name: markup, exact: true })).toBeVisible();
  await page.goBack();
  await expect(table.getByRole('link', { name: markup, exact: true })).toBeVisible();
  await page.goForward();
  await expect(page.getByRole('heading', { name: markup, exact: true })).toBeVisible();
}

test('contact persists across sessions, rejects stale notes and isolates delayed tenant reads', async ({ browser }) => {
  test.skip(process.env['CRM_REAL_OIDC_TEST'] !== '1', 'Requires isolated real-provider stack');
  test.setTimeout(900_000);
  const fixture = new RealOidcFixture();
  await fixture.withContactSessions(browser, async (first: Page, second: Page, agent: Page) => {
    first.setDefaultTimeout(15_000);
    second.setDefaultTimeout(15_000);
    await button(first, 'Tenant A').click();
    await list(first);
    await button(first, 'Thêm liên hệ').click();
    await expect(first.getByLabel('Tên liên hệ')).toBeFocused();
    await first.getByLabel('Tên liên hệ').fill(contact);
    await first.getByLabel('Email').fill('synthetic-browser@example.test');
    await button(first, 'Tạo').press('Enter');
    await expect(first.getByRole('heading', { name: contact, exact: true })).toBeVisible();
    const contactId = first.url().split('/').at(-1)!;
    await button(first, 'Chỉnh sửa').click();
    await first.getByLabel('Điện thoại').fill('123456789');
    const patch = first.waitForRequest(r => r.method() === 'PATCH' && r.url().endsWith('/contacts/' + contactId));
    await button(first, 'Lưu').click();
    const patchRequest = await patch;
    expect(patchRequest.postDataJSON()).toEqual({ phone: '123456789' });
    expect(patchRequest.headers()['if-match']).toMatch(/^"v\d+"$/);
    await expect(button(first, 'Chỉnh sửa')).toBeVisible();
    await list(first);
    await first.getByLabel('Tìm kiếm').fill(contact);
    await button(first, 'Lọc').click();
    await expect(first.getByRole('link', { name: contact, exact: true })).toBeVisible();
    await button(first, 'Thêm liên hệ').click();
    await first.getByLabel('Tên liên hệ').fill(contact);
    await first.getByLabel('Email').fill('synthetic-browser@example.test');
    const duplicate = first.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/contacts'));
    await button(first, 'Tạo').click();
    expect((await duplicate).status()).toBe(428);
    const reviewed = first.waitForResponse(r => r.request().method() === 'GET' && r.url().endsWith('/contacts/' + contactId));
    await button(first, 'Chọn ' + contact).click();
    const reviewedResponse = await reviewed;
    const reviewedEtag = reviewedResponse.headers()['etag'];
    expect(reviewedEtag).toMatch(/^"v\d+"$/);
    const conditional = first.waitForRequest(r => r.method() === 'POST' && r.url().endsWith('/contacts'));
    await button(first, 'Cập nhật ' + contact).click();
    const conditionalRequest = await conditional;
    expect(conditionalRequest.headers()['x-contact-id']).toBe(contactId);
    expect(conditionalRequest.headers()['if-match']).toBe(reviewedEtag);
    await expect(first.getByRole('heading', { name: contact, exact: true })).toBeVisible();
    await checkContactTable(first);
    await first.getByRole('navigation').getByRole('link', { name: 'Trường tùy chỉnh' }).click();
    await expect(first).toHaveURL(/\/app\/settings\/custom-fields$/);
    await expect(first.getByRole('heading', { name: 'Định nghĩa trường tùy chỉnh' })).toBeVisible();
    await first.getByLabel('Khóa trường').fill('browser_settings');
    await first.getByLabel('Tên trường').fill('Browser settings field');
    const definition = first.waitForResponse(r => r.url().endsWith('/custom-fields') && r.request().method() === 'POST');
    await button(first, 'Tạo trường').click();
    expect((await definition).status()).toBe(201);
    await expect(first.getByText('Browser settings field', { exact: true })).toBeVisible();
    await button(agent, 'Tenant A').click();
    await agent.getByRole('navigation').getByRole('link', { name: 'Trường tùy chỉnh' }).click();
    await expect(agent.getByText('Browser settings field', { exact: true })).toBeVisible();
    await agent.getByLabel('Khóa trường').fill('agent_forbidden');
    await agent.getByLabel('Tên trường').fill('Agent forbidden field');
    const denied = agent.waitForResponse(r => r.url().endsWith('/custom-fields') && r.request().method() === 'POST');
    await button(agent, 'Tạo trường').click();
    expect((await denied).status()).toBe(403);
    await expect(agent.getByLabel('Tên trường')).toHaveValue('Agent forbidden field');
    await expect(button(agent, 'Tạo trường')).toBeDisabled();
    await expect(first.getByText('Agent forbidden field', { exact: true })).toHaveCount(0);
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
    await first.getByLabel('Browser number').fill('');
    const nullWrite = first.waitForRequest(r => r.method() === 'PUT' && r.url().includes('/fields/'));
    await button(first, 'Lưu Browser number').click();
    expect((await nullWrite).postDataJSON()).toEqual({ value: null });
    await expect(button(first, 'Lưu Browser number')).toBeDisabled();
    await first.getByLabel('Browser number').fill(precise);
    await button(first, 'Lưu Browser number').click();
    await expect(button(first, 'Lưu Browser number')).toBeDisabled();
    await first.getByLabel('Khóa trường').fill('browser_required');
    await first.getByLabel('Tên trường').fill('Browser required');
    await first.getByLabel('Kiểu dữ liệu').selectOption('string');
    await first.getByLabel('Bắt buộc').check();
    await button(first, 'Tạo trường').click();
    await expect(first.getByLabel('Browser required')).toBeVisible();
    await expect(button(first, 'Lưu Browser required')).toBeDisabled();
    await button(second, 'Tenant A').click();
    await second.route(url => url.pathname.endsWith('/timeline'), route => route.fulfill({ status: 503 }), { times: 1 });
    await openContact(second);
    await expect(button(second, 'Tải lại hoạt động')).toBeVisible();
    await second.getByLabel('Ghi chú').fill('Unsubmitted stale draft');
    await first.getByLabel('Ghi chú').fill(note);
    let submittedNotes = 0;
    const countSubmittedNotes = (request: import('@playwright/test').Request) => { if (request.url().endsWith('/notes') && request.method() === 'POST') submittedNotes++; };
    first.on('request', countSubmittedNotes);
    await first.getByLabel('Ghi chú').evaluate(element => {
      const form = element.closest('form')!;
      form.requestSubmit(); form.requestSubmit();
    });
    await expect(first.getByText(note, { exact: true })).toBeVisible();
    expect(submittedNotes).toBe(1);
    first.off('request', countSubmittedNotes);
    let automaticNotes = 0;
    const countAutomaticNotes = (request: import('@playwright/test').Request) => { if (request.url().endsWith('/notes') && request.method() === 'POST') automaticNotes++; };
    second.on('request', countAutomaticNotes);
    const timelineRefresh = second.waitForResponse(r => r.url().includes('/timeline') && r.request().method() === 'GET' && r.status() === 200);
    await button(second, 'Tải lại hoạt động').click();
    await timelineRefresh;
    await expect(second.getByText(note, { exact: true })).toBeVisible();
    await expect(second.getByLabel('Ghi chú')).toHaveValue('Unsubmitted stale draft');
    expect(automaticNotes).toBe(0);
    second.off('request', countAutomaticNotes);
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


    const directRecordUrl = first.url();
    await first.reload();
    await expect(first).toHaveURL(directRecordUrl);
    await expect(first.getByRole('heading', { name: contact, exact: true })).toHaveCount(0);
    await expect(button(first, 'Sign in')).toBeVisible();
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
    const fulfilled = signal();
    const settled = signal();
    let heldRequest: import('@playwright/test').Request | undefined;
    const onSettled = (request: import('@playwright/test').Request) => {
      if (request === heldRequest) settled.resolve();
    };
    first.on('requestfinished', onSettled);
    first.on('requestfailed', onSettled);
    await first.route(url => url.pathname.endsWith('/contacts') && url.searchParams.get('q') === contact, async route => {
      heldRequest = route.request();
      const response = await privateOperation(() => route.fetch());
      expect(response.status()).toBe(200);
      expect((await response.text()).includes(contact)).toBe(true);
      received.resolve();
      await release.promise;
      await privateOperation(() => route.fulfill({ response }));
      fulfilled.resolve();
    }, { times: 1 });
    await first.getByLabel('Tìm kiếm').fill(contact);
    await button(first, 'Lọc').click();
    await received.promise;
    try {
      await button(first, 'Tenant B').click();
      await expect(first.getByText('Chưa có liên hệ.')).toBeVisible();
      release.resolve();
      await Promise.all([fulfilled.promise, settled.promise]);
      await first.evaluate(() => new Promise<void>(done => requestAnimationFrame(() => requestAnimationFrame(() => done()))));
      await expect(first.getByText('Chưa có liên hệ.')).toBeVisible();
      await expect(first.getByRole('row')).toHaveCount(0);
      await expect(first.locator('a[href^="/app/contacts/"]')).toHaveCount(0);
      await button(first, 'Tenant A').click();
      await first.getByLabel('Tìm kiếm').fill('no matching synthetic contact');
      await button(first, 'Lọc').click();
      await expect(first.getByText('Chưa có liên hệ.')).toBeVisible();
    } finally {
      release.resolve();
      first.off('requestfinished', onSettled);
      first.off('requestfailed', onSettled);
    }
    await expect(first.getByRole('link', { name: contact, exact: true })).toHaveCount(0);
    await button(first, 'Sign out').click();
    await expect(button(first, 'Sign in')).toBeVisible();
    await fixture.signInAgain(first);
    await button(first, 'Tenant A').click();
    await openContact(first);
    await expect(first.getByLabel('Browser number')).toHaveValue(precise);
    const writeReceived = signal();
    const releaseWrite = signal();
    const writeSettled = signal();
    await first.route(url => url.pathname.endsWith('/notes'), async route => {
      const response = await privateOperation(() => route.fetch());
      expect(response.status()).toBe(201);
      writeReceived.resolve();
      await releaseWrite.promise;
      try { await route.fulfill({ response }); } catch { /* a scope change may cancel the browser request */ }
      writeSettled.resolve();
    }, { times: 1 });
    await first.getByLabel('Ghi chú').fill('Pending scope transition note');
    await button(first, 'Thêm ghi chú').click();
    await writeReceived.promise;
    try {
      await button(first, 'Tenant B').click();
      await expect(button(first, 'Tenant B')).toHaveAttribute('aria-pressed', 'true');
      await expect(first.getByLabel('Ghi chú')).toHaveCount(0);
      await expect(first.getByRole('alert')).toContainText('NOT_FOUND');
      await button(first, 'Tenant A').click();
      await button(first, 'Sign out').click();
      await expect(button(first, 'Sign in')).toBeVisible();
      await fixture.signInAgain(first);
      await button(first, 'Tenant A').click();
      releaseWrite.resolve();
      await writeSettled.promise;
      await openContact(first);
      await expect(first.getByLabel('Ghi chú')).toHaveValue('');
      await expect(first.getByText('Pending scope transition note', { exact: true })).toBeVisible();
    } finally {
      releaseWrite.resolve();
    }
  });
});
