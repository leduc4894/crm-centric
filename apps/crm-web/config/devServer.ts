import react from '@vitejs/plugin-react';
import type { UserConfig } from 'vite';
import { workspaceAliases } from './aliases.ts';
import {
  assertExactProxyPaths,
  buildProxyOptions,
  DEV_SERVER_HOST,
  DEV_SERVER_PORT,
  HEALTH_PROXY_PATHS,
  resolveProxyTarget
} from './healthProxy.ts';

/**
 * Builds the web application configuration.
 *
 * The proxy target and the proxy allowlist are validated before the configuration is returned, so
 * a malformed or non-loopback target fails with a reason token and a correlation ID before any
 * server is started. Only the two operational health paths are proxied; the identity provider, the
 * admin API and the business API are never reachable through the dev server.
 */
export function createWebConfig(env: NodeJS.ProcessEnv = process.env): UserConfig {
  const target = resolveProxyTarget(env);
  assertExactProxyPaths(HEALTH_PROXY_PATHS);

  return {
    plugins: [react()],
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
