import { spawn, type ChildProcess } from 'node:child_process';
import { randomBytes, randomUUID } from 'node:crypto';
import { mkdtemp, readFile, readdir, readlink, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createServer } from 'node:net';
import { expect, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { repositoryRoot } from './stackProvenance.ts';

const root = repositoryRoot();
const webOrigin = 'http://localhost:5173';
const owner = `crm-e2e-${randomUUID().replaceAll('-', '')}`;
const secret = () => randomBytes(24).toString('hex');

export function fixtureResourceOwner(projectOwner: string, ciOwner?: string): string {
  if (ciOwner === undefined) return projectOwner;
  if (!/^[a-f0-9]{32}$/.test(ciOwner)) throw new Error('E2E_OWNER_INVALID');
  return ciOwner;
}

async function port(): Promise<number> {
  const s = createServer(); await new Promise<void>((resolve, reject) => { s.once('error', reject); s.listen(0, '127.0.0.1', resolve); });
  const address = s.address(); if (!address || typeof address === 'string') throw new Error('E2E_PORT_INVALID');
  const n = address.port; await new Promise<void>((resolve) => s.close(() => resolve())); return n;
}
async function command(program: string, args: string[], env: NodeJS.ProcessEnv, input?: string): Promise<string> {
  return new Promise((resolve, reject) => {
    const p = spawn(program, args, { cwd: root, env, stdio: ['pipe', 'pipe', 'pipe'] }); let output = '';
    p.stdout.on('data', (chunk: Buffer) => { output += chunk.toString(); }); p.stderr.resume();
    p.once('error', () => reject(new Error('E2E_COMMAND_FAILED')));
    p.once('close', (code) => code === 0 ? resolve(output) : reject(new Error(`E2E_COMMAND_FAILED ${program} ${code}`)));
    p.stdin.end(input);
  });
}
async function responseOk(response: Response, stage: string): Promise<Response> {
  if (!response.ok) throw new Error(`E2E_${stage}_FAILED ${response.status}`); return response;
}

/** Never propagate Playwright errors whose diagnostic text can contain form data. */
export async function privateOperation<T>(action: () => Promise<T>): Promise<T> {
  try { return await action(); } catch { throw new Error('E2E_PRIVATE_OPERATION_FAILED'); }
}

/** Fail closed unless the loopback listener belongs to a process in the spawned Vite group. */
export async function listenerOwnedByProcessGroup(portNumber: number, group: number): Promise<boolean> {
  if (process.platform !== 'linux' || !Number.isInteger(group) || group <= 0) return false;
  const hexPort = portNumber.toString(16).toUpperCase().padStart(4, '0');
  let lines: string[];
  try { lines = (await readFile('/proc/net/tcp', 'utf8')).trim().split('\n').slice(1); }
  catch { return false; }
  const sockets = lines.map((line) => line.trim().split(/\s+/))
    .filter((fields) => fields[1] === `0100007F:${hexPort}` && fields[3] === '0A')
    .map((fields) => fields[9]);
  if (sockets.length !== 1 || !sockets[0]) return false;
  const target = `socket:[${sockets[0]}]`;
  for (const pid of await readdir('/proc')) {
    if (!/^\d+$/.test(pid)) continue;
    try {
      const stat = await readFile(`/proc/${pid}/stat`, 'utf8');
      if (Number(stat.slice(stat.lastIndexOf(')') + 2).split(' ')[2]) !== group) continue;
      for (const fd of await readdir(`/proc/${pid}/fd`)) {
        try { if (await readlink(`/proc/${pid}/fd/${fd}`) === target) return true; } catch { /* process closed a descriptor */ }
      }
    } catch { /* process ended or belongs to another user */ }
  }
  return false;
}

export class RealOidcFixture {
  private stateDir = '';
  private env: NodeJS.ProcessEnv = {};
  private server: ChildProcess | undefined;
  private apiPort = 0;
  private issuer = '';
  private password = secret();
  private users = [secret(), secret()];
  private subjects: string[] = [];
  private tenantIds = [randomUUID(), randomUUID()];
  private tokens: string[] = [];
  private contexts: BrowserContext[] = [];
  private callbackUrl = '';
  private unexpectedBearer = false;
  private healthCredentials = false;
  private observedPkce = false;
  private tokenReads: Promise<void>[] = [];
  private issuedTokens: string[] = [];
  private responseCaptureFailed = false;
  private requestUrls: string[] = [];

  async run(browser: Browser): Promise<void> {
    try {
      await this.start();
      const a = await this.signIn(browser, 0);
      await expect(a.getByRole('button', { name: 'Tenant A', exact: true })).toBeVisible();
      await expect(a.getByRole('button', { name: 'Tenant B', exact: true })).toHaveCount(0);
      await a.getByRole('button', { name: 'Tenant A', exact: true }).click();
      await expect(a.getByRole('button', { name: 'Tenant A', exact: true })).toHaveAttribute('aria-pressed', 'true');
      expect(this.tokens[0]?.startsWith('Bearer ')).toBeTruthy();
      await Promise.all(this.tokenReads);
      expect(this.responseCaptureFailed).toBe(false); expect(this.issuedTokens.length).toBeGreaterThanOrEqual(2);
      const storage = await a.evaluate(async () => ({ local: JSON.stringify(localStorage), session: JSON.stringify(sessionStorage), db: await indexedDB.databases(), url: location.href }));
      for (const token of this.issuedTokens) {
        expect(storage.local.includes(token)).toBe(false); expect(storage.session.includes(token)).toBe(false);
        expect(this.requestUrls.some((url) => url.includes(token))).toBe(false);
      }
      const devtools = await a.context().newCDPSession(a);
      const history = await devtools.send('Page.getNavigationHistory');
      expect(history.entries.length).toBeGreaterThan(0);
      for (const token of this.issuedTokens) expect(history.entries.some((entry) => entry.url.includes(token))).toBe(false);
      await devtools.detach();
      const healthRequest = a.waitForRequest((request) => new URL(request.url()).pathname === '/health/ready');
      await a.getByRole('button', { name: 'Check again' }).click();
      const healthHeaders = await (await healthRequest).allHeaders();
      expect(Boolean(healthHeaders['authorization'] || healthHeaders['cookie'] || healthHeaders['x-tenant-id'])).toBe(false);
      expect(storage.session).not.toContain('crm.oidc.transaction');
      expect(storage.db).toEqual([]);
      expect(new URL(storage.url).search).toBe('');
      expect(this.unexpectedBearer).toBe(false); expect(this.healthCredentials).toBe(false); expect(this.observedPkce).toBe(true);
      const same = await fetch(`http://127.0.0.1:${this.apiPort}/api/v1/memberships`, { headers: { authorization: this.tokens[0]!, 'x-tenant-id': this.tenantIds[0]! } });
      const cross = await fetch(`http://127.0.0.1:${this.apiPort}/api/v1/memberships`, { headers: { authorization: this.tokens[0]!, 'x-tenant-id': this.tenantIds[1]! } });
      expect(same.status, 'own tenant request').toBe(200); expect(cross.status, 'cross tenant request').toBe(403);
      const consumedCallback = this.callbackUrl;
      expect(consumedCallback).not.toBe('');
      let replayTenantRequests = 0;
      a.on('request', (request) => { if (new URL(request.url()).pathname === '/api/v1/tenants') replayTenantRequests++; });
      await privateOperation(() => a.goto(consumedCallback));
      await expect(a.getByRole('alert')).toContainText('OIDC_CALLBACK_FAILED');
      await expect(a.getByRole('button', { name: 'Sign out' })).toHaveCount(0);
      expect(replayTenantRequests).toBe(0);
      await a.getByRole('button', { name: 'Sign in' }).click();
      await expect(a.getByRole('button', { name: 'Sign out' })).toBeVisible({ timeout: 30_000 });
      await a.getByRole('button', { name: 'Sign out' }).click();
      await expect(a.getByRole('button', { name: 'Sign in' })).toBeVisible();
      await a.getByRole('button', { name: 'Sign in' }).click();
      await expect(a.locator('#username')).toBeVisible();
      const b = await this.signIn(browser, 1);
      await expect(b.getByRole('button', { name: 'Tenant B', exact: true })).toBeVisible();
      await expect(b.getByRole('button', { name: 'Tenant A', exact: true })).toHaveCount(0);
      expect(this.callbackUrl).not.toBe('');
      const replayContext = await browser.newContext(); this.contexts.push(replayContext);
      const replay = await replayContext.newPage();
      try { await replay.goto(this.callbackUrl); } catch { throw new Error('E2E_REPLAY_NAVIGATION_FAILED'); }
      await expect(replay.getByRole('alert')).toContainText('OIDC_CALLBACK_FAILED');
      await expect(replay.getByRole('button', { name: 'Sign out' })).toHaveCount(0);
      await replay.goto(`${webOrigin}/auth/callback?code=synthetic-invalid-code&state=synthetic-invalid-state`);
      await expect(replay.getByRole('alert')).toContainText('OIDC_CALLBACK_FAILED');
      await expect(replay).toHaveURL(`${webOrigin}/auth/callback`);
      const wrongStateContext = await browser.newContext(); this.contexts.push(wrongStateContext);
      const wrongState = await wrongStateContext.newPage(); let authenticatedRequests = 0; let changedState = false;
      wrongState.on('request', (request) => { if (new URL(request.url()).pathname === '/api/v1/tenants') authenticatedRequests++; });
      // Playwright does not route a later navigation redirect; intercept the login POST
      // and change only its Location response header before the browser follows it.
      await wrongState.route((url) => url.pathname.includes('/login-actions/authenticate'), async (route) => privateOperation(async () => {
        const original = await route.fetch({ maxRedirects: 0 });
        const location = original.headers()['location'];
        if (!location) { await route.fulfill({ response: original }); return; }
        const target = new URL(location, this.issuer);
        if (target.origin !== webOrigin || target.pathname !== '/auth/callback') { await route.fulfill({ response: original }); return; }
        target.searchParams.set('state', 'synthetic-wrong-state'); changedState = true;
        await route.fulfill({ response: original, headers: { ...original.headers(), location: target.href } });
      }));
      await wrongState.goto(webOrigin); await wrongState.getByRole('button', { name: 'Sign in' }).click();
      await privateOperation(async () => { await wrongState.locator('#username').fill(this.users[0]!); await wrongState.locator('#password').fill(this.password); await wrongState.locator('#kc-login').click(); });
      await expect.poll(() => changedState, { message: 'callback interception reached' }).toBe(true);
      await expect(wrongState.getByRole('alert')).toContainText('OIDC_CALLBACK_FAILED');
      expect(authenticatedRequests).toBe(0);
      await expect(wrongState).toHaveURL(`${webOrigin}/auth/callback`);
      await b.route(`${this.issuer}/.well-known/openid-configuration`, (route) => route.abort());
      await b.getByRole('button', { name: 'Sign out' }).click();
      await expect(b.getByRole('alert')).toContainText('OIDC_LOGOUT_FAILED');
      await expect(b.getByRole('button', { name: 'Sign out' })).toHaveCount(0);
      const expiry = await this.signIn(browser, 1, true);
      await expiry.clock.fastForward('10:00');
      await expect(expiry.getByRole('button', { name: 'Sign in' })).toBeVisible();
      const unavailableContext = await browser.newContext(); this.contexts.push(unavailableContext);
      const unavailable = await unavailableContext.newPage();
      await unavailable.route(`${this.issuer}/.well-known/openid-configuration`, (route) => route.abort());
      await unavailable.goto(webOrigin);
      await unavailable.getByRole('button', { name: 'Sign in' }).click();
      await expect(unavailable.getByRole('alert')).toContainText('OIDC_LOGIN_FAILED');
    } finally { await this.stop(); }
  }
  async withContactSessions(browser: Browser, exercise: (first: Page, second: Page) => Promise<void>) {
    try {
      await this.start(true);
      await exercise(await this.signIn(browser, 0), await this.signIn(browser, 0));
    } finally { await this.stop(); }
  }

  private async start(sharedMembership = false) {
    if (process.env['CRM_REAL_OIDC_TEST'] !== '1') throw new Error('E2E_FIXTURE_OPT_IN_REQUIRED');
    this.stateDir = await mkdtemp(join(tmpdir(), 'crm-browser-oidc-'));
    this.apiPort = await port(); const idpPort = await port(); this.issuer = `http://localhost:${idpPort}/realms/crm-local`;
    this.env = { ...process.env, LOCAL_STACK_PROJECT: owner, LOCAL_STACK_STATE_DIR: join(this.stateDir, 'state'), LOCAL_API_PORT: String(this.apiPort), LOCAL_IDP_PORT: String(idpPort), CI_RESOURCE_OWNER: fixtureResourceOwner(owner, process.env['CI_RESOURCE_OWNER']) };
    await command('bash', ['scripts/dev-up.sh'], { ...this.env, CONTACT_CURSOR_SIGNING_KEY: randomBytes(32).toString('base64') });
    const password = (await readFile(join(this.stateDir, 'state', 'idp_admin_password'), 'utf8')).trim();
    const login = new URLSearchParams({ grant_type: 'password', client_id: 'admin-cli', username: 'local-admin', password });
    const result = await responseOk(await fetch(`http://localhost:${idpPort}/realms/master/protocol/openid-connect/token`, { method: 'POST', body: login }), 'ADMIN_LOGIN');
    const { access_token: admin } = await result.json() as { access_token: string };
    for (const username of this.users) {
      await responseOk(await fetch(`${this.issuer.replace('/realms/', '/admin/realms/')}/users`, { method: 'POST', headers: { authorization: `Bearer ${admin}`, 'content-type': 'application/json' }, body: JSON.stringify({ username, email: `${username}@example.invalid`, firstName: 'Test', lastName: 'User', enabled: true, emailVerified: true, credentials: [{ type: 'password', value: this.password, temporary: false }] }) }), 'USER_CREATE');
      const list = await responseOk(await fetch(`${this.issuer.replace('/realms/', '/admin/realms/')}/users?username=${username}&exact=true`, { headers: { authorization: `Bearer ${admin}` } }), 'USER_READ');
      const users = await list.json() as { id: string }[]; if (users.length !== 1 || !/^[a-f0-9-]{36}$/.test(users[0]!.id)) throw new Error('E2E_USER_INVALID'); this.subjects.push(users[0]!.id);
    }
    const containers = (await command('docker', ['ps', '-q', '--filter', `label=com.docker.compose.project=${owner}`, '--filter', 'label=com.docker.compose.service=crm-db'], this.env)).trim();
    if (!/^[a-f0-9]{12,64}$/.test(containers)) throw new Error('E2E_OWNED_DB_INVALID');
    const sql = `INSERT INTO platform.tenants VALUES ('${this.tenantIds[0]}','Tenant A','active',now()),('${this.tenantIds[1]}','Tenant B','active',now());\n` + this.subjects.map((sub, i) => `INSERT INTO platform.memberships VALUES ('${this.tenantIds[i]}',gen_random_uuid(),'${this.issuer}','${sub}','owner','active',1); INSERT INTO platform.staff_bindings VALUES ('${this.issuer}','${sub}','active','e2e');`).join('\n');
    const membership = sharedMembership ? `INSERT INTO platform.memberships VALUES ('${this.tenantIds[1]}',gen_random_uuid(),'${this.issuer}','${this.subjects[0]}','owner','active',1);` : '';
    await command('docker', ['exec', '-i', containers, 'psql', '-U', 'postgres', '-d', 'crm', '-v', 'ON_ERROR_STOP=1'], this.env, sql + membership);
    this.server = spawn('npm', ['run', 'dev'], { cwd: root, env: { ...this.env, VITE_OIDC_ISSUER: this.issuer, VITE_OIDC_CLIENT_ID: 'crm-web-local', VITE_OIDC_CALLBACK_URL: `${webOrigin}/auth/callback`, VITE_OIDC_LOGOUT_URL: `${webOrigin}/`, VITE_OIDC_LOCAL_HTTP: 'true' }, stdio: 'ignore', detached: true });
    const deadline = Date.now() + 120_000;
    while (Date.now() < deadline) {
      if (this.server.exitCode !== null) throw new Error('E2E_WEB_START_FAILED');
      if (this.server.pid && await listenerOwnedByProcessGroup(5173, this.server.pid)) {
        try { if ((await fetch(webOrigin)).ok && await listenerOwnedByProcessGroup(5173, this.server.pid)) return; } catch { /* startup */ }
      }
      await new Promise((resolve) => setTimeout(resolve, 500));
    }
    throw new Error('E2E_WEB_UNAVAILABLE');
  }
  private async signIn(browser: Browser, index: number, useClock = false): Promise<Page> {
    const context = await browser.newContext(); this.contexts.push(context); const page = await context.newPage();
    if (useClock) await page.clock.install();
    page.on('response', (response) => {
      if (index !== 0 || response.url() !== `${this.issuer}/protocol/openid-connect/token` || response.status() !== 200) return;
      this.tokenReads.push(response.json().then((body: Record<string, unknown>) => {
        for (const key of ['access_token', 'id_token', 'refresh_token']) if (typeof body[key] === 'string') this.issuedTokens.push(body[key] as string);
      }).catch(() => { this.responseCaptureFailed = true; }));
    });
    page.on('request', (request) => {
      this.requestUrls.push(request.url());
      const url = new URL(request.url()); const headers = request.headers();
      if (url.href.startsWith(`${this.issuer}/protocol/openid-connect/auth`) && url.searchParams.get('code_challenge_method') === 'S256' && (url.searchParams.get('code_challenge')?.length ?? 0) >= 43) this.observedPkce = true;
      if (url.origin === webOrigin && url.pathname === '/auth/callback' && url.searchParams.has('code')) this.callbackUrl = request.url();
      if (url.origin === webOrigin && url.pathname === '/api/v1/tenants' && request.method() === 'GET') this.tokens[index] = headers.authorization ?? '';
      else if (headers.authorization?.startsWith('Bearer ')) this.unexpectedBearer = true;
      if (url.origin === webOrigin && url.pathname.startsWith('/health/') && (headers.authorization || headers.cookie || headers['x-tenant-id'])) this.healthCredentials = true;
    });
    await page.goto(webOrigin); await page.getByRole('button', { name: 'Sign in' }).click();
    await privateOperation(async () => { await page.locator('#username').fill(this.users[index]!); await page.locator('#password').fill(this.password); await page.locator('#kc-login').click(); });
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible({ timeout: 30_000 });
    return page;
  }
  private async stop() {
    for (const context of this.contexts) {
      if (context.browser()?.isConnected()) await context.close();
    }
    if (this.server?.pid) { try { process.kill(-this.server.pid, 'SIGTERM'); } catch { /* already stopped */ } }
    if (!this.stateDir) return;
    for (const kind of ['container', 'network', 'volume']) {
      const ids = (await command('docker', [kind, 'ls', kind === 'container' ? '-aq' : '-q', '--filter', `label=com.docker.compose.project=${owner}`, '--filter', `label=io.crm-centric.ci-owner=${this.env['CI_RESOURCE_OWNER']}`], this.env)).trim().split('\n').filter(Boolean);
      for (const id of ids) { if (!/^[a-f0-9]{12,64}$/.test(id) && kind !== 'volume' && kind !== 'network') throw new Error('E2E_CLEANUP_ID_INVALID'); await command('docker', [kind, 'rm', ...(kind === 'container' ? ['-f'] : []), id], this.env); }
    }
    const images = (await command('docker', ['image', 'ls', '--format', '{{.Repository}}', '--filter', `reference=${owner}-*`], this.env)).trim().split('\n').filter(Boolean);
    for (const image of images) {
      if (![`${owner}-api`, `${owner}-migrator`, `${owner}-postgres`, `${owner}-keycloak`].includes(image)) throw new Error('E2E_CLEANUP_IMAGE_INVALID');
      await command('docker', ['image', 'rm', image], this.env);
    }
    this.tokens = []; this.issuedTokens = []; this.callbackUrl = ''; this.requestUrls = [];
    await rm(this.stateDir, { recursive: true, force: true });
  }
}
