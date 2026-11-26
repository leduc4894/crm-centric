import { randomUUID } from 'node:crypto';
import { request as httpRequest } from 'node:http';
import type { Plugin } from 'vite';
import type { ProxyTarget } from './healthProxy.ts';

/** Routes only tenant-list GETs to the already validated numeric loopback API. */
export function tenantProxy(target: ProxyTarget): Plugin {
  return {
    name: 'crm-centric:tenant-list-proxy',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const raw = req.url ?? '';
        if (!/^\/api\/v1\/tenants(?:\?.*)?$/.test(raw)) { next(); return; }
        const fail = (status: number, reasonToken: string) => {
          if (res.headersSent) { res.destroy(); return; }
          res.writeHead(status, { 'content-type': 'application/json', 'cache-control': 'no-store' });
          res.end(JSON.stringify({ reasonToken, correlationId: randomUUID() }));
        };
        if (req.method !== 'GET') { fail(405, 'WEB_TENANT_METHOD_INVALID'); return; }
        const headers: Record<string, string> = { accept: 'application/json' };
        if (typeof req.headers.authorization === 'string') headers.authorization = req.headers.authorization;
        const upstream = httpRequest({ hostname: target.host, port: target.port, path: raw, method: 'GET', headers, timeout: 10_000 }, (response) => {
          if ((response.statusCode ?? 500) >= 300 && (response.statusCode ?? 500) < 400) { response.resume(); fail(502, 'WEB_TENANT_REDIRECT_REJECTED'); return; }
          const forwarded: Record<string, string> = { 'cache-control': 'no-store' };
          if (typeof response.headers['content-type'] === 'string') forwarded['content-type'] = response.headers['content-type'];
          if (typeof response.headers['x-correlation-id'] === 'string') forwarded['x-correlation-id'] = response.headers['x-correlation-id'];
          res.writeHead(response.statusCode ?? 502, forwarded); response.pipe(res);
        });
        upstream.on('timeout', () => upstream.destroy());
        upstream.on('error', () => fail(502, 'WEB_TENANT_PROXY_UNAVAILABLE'));
        req.on('aborted', () => upstream.destroy());
        upstream.end();
      });
    }
  };
}
