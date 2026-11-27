import { randomUUID } from 'node:crypto';
import { request as httpRequest } from 'node:http';
import type { Plugin } from 'vite';
import type { ProxyTarget } from './healthProxy.ts';

import { contactMethods } from './contactsProxy.ts';

/** Forwards explicitly supported API routes to validated loopback. */
export function scopedApiProxy(target: ProxyTarget): Plugin {
  return {
    name: 'crm-centric:scoped-api-proxy',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const raw = req.url ?? '';
        const tenants = /^\/api\/v1\/tenants(?:\?.*)?$/.test(raw);
        const methods = tenants ? ['GET'] : contactMethods(raw);
        if (!methods) { next(); return; }
        const prefix = tenants ? 'WEB_TENANT' : 'WEB_CRM';
        const fail = (status: number, reasonToken: string) => {
          if (res.headersSent) { res.destroy(); return; }
          res.writeHead(status, { 'content-type': 'application/json', 'cache-control': 'no-store' });
          res.end(JSON.stringify({ reasonToken, correlationId: randomUUID() }));
        };
        if (!methods.includes(req.method ?? '')) { fail(405, prefix + '_METHOD_INVALID'); return; }
        const headers: Record<string, string> = { accept: 'application/json' };
        if (typeof req.headers.authorization === 'string') headers.authorization = req.headers.authorization;
        if (!tenants) {
          for (const name of ['x-tenant-id', 'if-match', 'x-contact-id', 'content-type']) {
            const value = req.headers[name];
            if (typeof value === 'string') headers[name] = value;
          }
        }
        const upstream = httpRequest({ hostname: target.host, port: target.port, path: raw, method: req.method, headers, timeout: 10_000 }, (response) => {
          if ((response.statusCode ?? 500) >= 300 && (response.statusCode ?? 500) < 400) { response.resume(); fail(502, prefix + '_REDIRECT_REJECTED'); return; }
          const forwarded: Record<string, string> = { 'cache-control': 'no-store' };
          if (typeof response.headers['content-type'] === 'string') forwarded['content-type'] = response.headers['content-type'];
          if (typeof response.headers['x-correlation-id'] === 'string') forwarded['x-correlation-id'] = response.headers['x-correlation-id'];
          if (!tenants && typeof response.headers.etag === 'string') forwarded.etag = response.headers.etag;
          response.on('aborted', () => res.destroy());
          response.on('error', () => res.destroy());
          res.writeHead(response.statusCode ?? 502, forwarded); response.pipe(res);
        });
        upstream.on('timeout', () => upstream.destroy());
        upstream.on('error', () => fail(502, prefix + '_PROXY_UNAVAILABLE'));
        req.on('aborted', () => upstream.destroy());
        res.on('close', () => upstream.destroy());
        if (tenants || req.method === 'GET') upstream.end();
        else req.pipe(upstream);
      });
    }
  };
}
