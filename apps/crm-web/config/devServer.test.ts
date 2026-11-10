import { createServer as createHttpServer, type Server as HttpServer } from 'node:http';
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import { createServer, type ViteDevServer } from 'vite';
import { createWebConfig } from './devServer';
import { DEV_SERVER_HOST, DEV_SERVER_PORT } from './healthProxy';

interface StubApi {
  readonly server: HttpServer;
  readonly port: number;
  readonly receivedPaths: string[];
}

async function startStubApi(): Promise<StubApi> {
  const receivedPaths: string[] = [];
  const server = createHttpServer((request, response) => {
    receivedPaths.push(request.url ?? '');
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end(JSON.stringify({ stub: 'loopback-api' }));
  });

  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
  });

  const address = server.address();
  if (address === null || typeof address === 'string') {
    throw new Error('the stub API did not bind to a TCP port');
  }

  return { server, port: address.port, receivedPaths };
}

async function startDevServer(env: NodeJS.ProcessEnv, port: number): Promise<ViteDevServer> {
  const server = await createServer({
    ...createWebConfig(env),
    appType: 'custom'
  });
  await server.listen(port);
  return server;
}

function devServerOrigin(server: ViteDevServer): string {
  const address = server.httpServer?.address();
  if (address === null || address === undefined || typeof address === 'string') {
    throw new Error('the dev server did not bind to a TCP port');
  }
  return `http://127.0.0.1:${address.port}`;
}

describe('createWebConfig', () => {
  it('binds the dev server to localhost on the pinned port with a strict port', () => {
    const server = createWebConfig({}).server;

    expect(server?.host).toBe(DEV_SERVER_HOST);
    expect(server?.port).toBe(DEV_SERVER_PORT);
    expect(server?.strictPort).toBe(true);
  });

  it('does not enable a wildcard CORS origin', () => {
    const server = createWebConfig({}).server;

    expect(server?.cors).toBeUndefined();
  });

  it('fails closed on a malformed proxy target before any server is created', () => {
    expect(() => createWebConfig({ LOCAL_API_PORT: 'not-a-port' })).toThrow(
      /WEB_PROXY_TARGET_INVALID/
    );
  });

  it('fails closed on a non-loopback proxy target before any server is created', () => {
    expect(() => createWebConfig({ LOCAL_API_HOST: 'example.com' })).toThrow(
      /WEB_PROXY_TARGET_NOT_LOOPBACK/
    );
  });
});

describe('the dev server proxy', () => {
  let stubApi: StubApi;
  let devServer: ViteDevServer | undefined;

  beforeAll(async () => {
    stubApi = await startStubApi();
  });

  afterAll(async () => {
    await new Promise<void>((resolve) => {
      stubApi.server.close(() => resolve());
    });
  });

  afterEach(async () => {
    await devServer?.close();
    devServer = undefined;
    stubApi.receivedPaths.length = 0;
  });

  it('proxies the two operational health paths to the loopback API', async () => {
    devServer = await startDevServer({ LOCAL_API_PORT: String(stubApi.port) }, 0);
    const origin = devServerOrigin(devServer);

    const live = await fetch(`${origin}/health/live`);
    const ready = await fetch(`${origin}/health/ready`);

    expect(live.status).toBe(200);
    expect(ready.status).toBe(200);
    expect(await live.json()).toEqual({ stub: 'loopback-api' });
    expect(stubApi.receivedPaths).toEqual(['/health/live', '/health/ready']);
  });

  it('does not proxy any path outside the allowlist', async () => {
    devServer = await startDevServer({ LOCAL_API_PORT: String(stubApi.port) }, 0);
    const origin = devServerOrigin(devServer);

    for (const path of ['/api/contacts', '/admin/tenants', '/realms/crm', '/health', '/health/live/extra']) {
      const response = await fetch(`${origin}${path}`);
      expect(response.status, `${path} must not be served by the proxy`).toBe(404);
    }

    expect(stubApi.receivedPaths).toEqual([]);
  });

  it('never answers with a wildcard CORS origin', async () => {
    devServer = await startDevServer({ LOCAL_API_PORT: String(stubApi.port) }, 0);
    const origin = devServerOrigin(devServer);

    const response = await fetch(`${origin}/health/live`, {
      headers: { origin: 'http://localhost:5173' }
    });

    expect(response.headers.get('access-control-allow-origin')).not.toBe('*');
  });
});

describe('the dev server port', () => {
  let foreignListener: HttpServer | undefined;

  afterEach(async () => {
    if (foreignListener !== undefined) {
      await new Promise<void>((resolve) => {
        foreignListener?.close(() => resolve());
      });
      foreignListener = undefined;
    }
  });

  it('fails instead of selecting another port when the port is already occupied', async () => {
    foreignListener = createHttpServer((_request, response) => {
      response.writeHead(200);
      response.end();
    });
    await new Promise<void>((resolve, reject) => {
      foreignListener?.once('error', reject);
      foreignListener?.listen(DEV_SERVER_PORT, DEV_SERVER_HOST, resolve);
    });

    const server = await createServer(createWebConfig({}));

    await expect(server.listen()).rejects.toThrow(/already in use/i);
  });
});
