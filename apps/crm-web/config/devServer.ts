import { randomUUID } from 'node:crypto';
import react from '@vitejs/plugin-react';
import type { Plugin, UserConfig } from 'vite';
import { tenantProxy } from './tenantProxy.ts';
import { workspaceAliases } from './aliases.ts';
import {
  assertExactProxyPaths,
  buildProxyOptions,
  credentialStripPlugin,
  DEV_SERVER_HOST,
  DEV_SERVER_PORT,
  HEALTH_PROXY_PATHS,
  resolveProxyTarget
} from './healthProxy.ts';

/** Keep the callback prelude ahead of scripts injected by Vite and its plugins. */
function callbackPrelude(): Plugin {
  return {
    name: 'crm-callback-prelude',
    transformIndexHtml: {
      order: 'post',
      handler(html) {
        const pattern = /\s*<!-- oidc-callback-prelude-start -->[\s\S]*?<!-- oidc-callback-prelude-end -->/;
        const match = html.match(pattern);
        if (!match || !html.includes('<head>')) throw new Error(`WEB_CALLBACK_PRELUDE_MISSING ${randomUUID()}`);
        const delayed: string[] = [];
        let remaining = html.replace(pattern, '');
        remaining = remaining.replace(/<script\b(?=[^>]*\btype=["']module["'])[^>]*>[\s\S]*?<\/script>/gi, (tag) => { delayed.push(tag); return ''; });
        remaining = remaining.replace(/<link\b(?=[^>]*\brel=["'](?:modulepreload|preload|stylesheet)["'])[^>]*\/?>/gi, (tag) => { delayed.push(tag); return ''; });
        if (/<(?:script\b[^>]*\bsrc|link\b[^>]*\bhref|img\b|iframe\b|audio\b|video\b|source\b|embed\b|object\b)/i.test(remaining)) {
          throw new Error(`WEB_CALLBACK_RESOURCE_UNSAFE ${randomUUID()}`);
        }
        const encoded = JSON.stringify(delayed.join('')).replace(/</g, '\\u003c');
        const prelude = match[0].replace('</script>', `document.write(${encoded});</script>`);
        return remaining.replace('<head>', `<head>${prelude}`);
      }
    }
  };
}

/**
 * Builds the web application configuration.
 *
 * The proxy target and the proxy allowlist are validated before the configuration is returned, so
 * a malformed or non-loopback target fails with a reason token and a correlation ID before any
 * server is started. Only the two health paths and the exact GET tenant-list path are proxied; other API paths remain unavailable.
 */
export function createWebConfig(env: NodeJS.ProcessEnv = process.env): UserConfig {
  const target = resolveProxyTarget(env);
  assertExactProxyPaths(HEALTH_PROXY_PATHS);

  return {
    plugins: [react(), credentialStripPlugin(), tenantProxy(target), callbackPrelude()],
    resolve: {
      alias: workspaceAliases
    },
    server: {
      host: DEV_SERVER_HOST,
      port: DEV_SERVER_PORT,
      strictPort: true,
      proxy: buildProxyOptions(target)
    },
    preview: {
      host: DEV_SERVER_HOST,
      port: 4173,
      strictPort: true
    },
    build: {
      outDir: 'dist',
      sourcemap: false
    }
  };
}
