import { randomUUID } from 'node:crypto';
import type { ProxyOptions } from 'vite';

/** Only these two operational paths may leave the browser through the dev proxy. */
export const HEALTH_PROXY_PATHS = ['/health/live', '/health/ready'] as const;

/**
 * Credential-bearing headers that must never cross the browser-to-proxy boundary.
 *
 * The operational health endpoints are anonymous by contract, so relaying these would hand a
 * browser session or a tenant selection to the API. `authorization` carries the `bearerAuth`
 * scheme, `cookie` carries the browser session and `x-tenant-id` is the canonical tenant context
 * header of the CRM API (`docs/contracts/crm-api.openapi.yaml`).
 */
export const STRIPPED_PROXY_HEADERS = ['authorization', 'cookie', 'x-tenant-id'] as const;

const REGEX_METACHARACTERS = /[.*+?^${}()|[\]\\]/g;

/** Escapes a literal allowlist path so it cannot widen the proxy matcher. */
function escapeRegExp(value: string): string {
  return value.replace(REGEX_METACHARACTERS, '\\$&');
}

/**
 * Builds the regular-expression proxy context matcher for an allowlist.
 *
 * Every path is escaped, so a path that contains a regular-expression metacharacter matches only
 * itself and can never widen the allowlist.
 */
export function buildProxyMatcher(paths: readonly string[]): string {
  return `^(${paths.map(escapeRegExp).join('|')})(\\?.*)?$`;
}

/**
 * Regular expression source Vite compiles into the proxy context matcher.
 *
 * The matcher is derived from the allowlist so the two can never drift apart. A trailing query
 * string is accepted because Vite matches the context against the request URL including it.
 */
export const HEALTH_PROXY_MATCHER = buildProxyMatcher(HEALTH_PROXY_PATHS);

export const DEFAULT_LOCAL_API_HOST = '127.0.0.1';
export const DEFAULT_LOCAL_API_PORT = 5080;

export const DEV_SERVER_HOST = 'localhost';
export const DEV_SERVER_PORT = 5173;

export type ProxyConfigurationReason =
  | 'WEB_PROXY_TARGET_INVALID'
  | 'WEB_PROXY_TARGET_NOT_LOOPBACK'
  | 'WEB_PROXY_PATH_INVALID';

export class ProxyConfigurationError extends Error {
  readonly reason: ProxyConfigurationReason;
  readonly correlationId: string;

  constructor(reason: ProxyConfigurationReason, message: string, correlationId = randomUUID()) {
    super(`${reason}: ${message} (correlationId=${correlationId})`);
    this.name = 'ProxyConfigurationError';
    this.reason = reason;
    this.correlationId = correlationId;
  }
}

export interface ProxyTarget {
  readonly host: string;
  readonly port: number;
  readonly origin: string;
}

const LOOPBACK_HOSTS = new Set(['127.0.0.1', '::1']);

/**
 * Resolves the loopback API target from the local environment.
 *
 * Only loopback hosts are accepted so the proxy cannot be pointed at an arbitrary upstream.
 */
export function resolveProxyTarget(env: NodeJS.ProcessEnv = process.env): ProxyTarget {
  const rawPort = env.LOCAL_API_PORT ?? String(DEFAULT_LOCAL_API_PORT);
  if (!/^\d{1,5}$/.test(rawPort)) {
    throw new ProxyConfigurationError(
      'WEB_PROXY_TARGET_INVALID',
      `LOCAL_API_PORT must be a decimal port number, received '${rawPort}'`
    );
  }
  const port = Number(rawPort);
  if (port < 1 || port > 65535) {
    throw new ProxyConfigurationError(
      'WEB_PROXY_TARGET_INVALID',
      `LOCAL_API_PORT must be between 1 and 65535, received '${rawPort}'`
    );
  }
  const host = env.LOCAL_API_HOST ?? DEFAULT_LOCAL_API_HOST;
  if (!LOOPBACK_HOSTS.has(host)) {
    throw new ProxyConfigurationError(
      'WEB_PROXY_TARGET_NOT_LOOPBACK',
      `LOCAL_API_HOST must be a loopback address, received '${host}'`
    );
  }
  return { host, port, origin: `http://${host === '::1' ? `[${host}]` : host}:${port}` };
}

/**
 * Fails closed unless every allowlisted path is an exact absolute path.
 *
 * Wildcards, the root path, relative paths, duplicates and trailing slashes are rejected so the
 * proxy can never widen beyond the two operational health endpoints.
 */
export function assertExactProxyPaths(paths: readonly string[]): void {
  if (paths.length === 0) {
    throw new ProxyConfigurationError('WEB_PROXY_PATH_INVALID', 'the proxy allowlist must not be empty');
  }
  const seen = new Set<string>();
  for (const path of paths) {
    if (!path.startsWith('/')) {
      throw new ProxyConfigurationError(
        'WEB_PROXY_PATH_INVALID',
        `the proxy allowlist must contain absolute paths, received '${path}'`
      );
    }
    if (path === '/') {
      throw new ProxyConfigurationError(
        'WEB_PROXY_PATH_INVALID',
        'the proxy allowlist must not contain the root path'
      );
    }
    if (/[*?#\s]/.test(path)) {
      throw new ProxyConfigurationError(
        'WEB_PROXY_PATH_INVALID',
        `the proxy allowlist must contain exact paths, received '${path}'`
      );
    }
    if (path.endsWith('/')) {
      throw new ProxyConfigurationError(
        'WEB_PROXY_PATH_INVALID',
        `the proxy allowlist must not contain trailing slashes, received '${path}'`
      );
    }
    if (seen.has(path)) {
      throw new ProxyConfigurationError(
        'WEB_PROXY_PATH_INVALID',
        `the proxy allowlist must not contain duplicates, received '${path}'`
      );
    }
    seen.add(path);
  }
}

/**
 * Builds the Vite proxy options for the operational health endpoints only.
 *
 * The proxy forwards the browser request headers by default, so the credential-bearing headers are
 * removed at the proxy boundary before the upstream request is sent. The operational endpoints are
 * anonymous by contract and must stay anonymous even once the browser holds a session.
 */
export function buildProxyOptions(target: ProxyTarget): Record<string, ProxyOptions> {
  return {
    [HEALTH_PROXY_MATCHER]: {
      target: target.origin,
      changeOrigin: false,
      configure: (proxy) => {
        proxy.on('proxyReq', (proxyReq) => {
          for (const header of STRIPPED_PROXY_HEADERS) {
            proxyReq.removeHeader(header);
          }
        });
      }
    }
  };
}
