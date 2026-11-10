import { describe, expect, it } from 'vitest';
import {
  DEFAULT_LOCAL_API_PORT,
  HEALTH_PROXY_MATCHER,
  HEALTH_PROXY_PATHS,
  ProxyConfigurationError,
  assertExactProxyPaths,
  buildProxyOptions,
  resolveProxyTarget
} from './healthProxy.ts';

describe('resolveProxyTarget', () => {
  it('uses the default loopback port when LOCAL_API_PORT is not set', () => {
    const target = resolveProxyTarget({});

    expect(target).toEqual({
      host: '127.0.0.1',
      port: DEFAULT_LOCAL_API_PORT,
      origin: `http://127.0.0.1:${DEFAULT_LOCAL_API_PORT}`
    });
  });

  it('uses a valid explicit port', () => {
    const target = resolveProxyTarget({ LOCAL_API_PORT: '5123' });

    expect(target.port).toBe(5123);
    expect(target.origin).toBe('http://127.0.0.1:5123');
  });

  it('rejects a malformed port with a reason token and correlation id', () => {
    for (const malformed of ['abc', '80.5', '0', '65536', '-1', ' 5080', '']) {
      let raised: unknown;
      try {
        resolveProxyTarget({ LOCAL_API_PORT: malformed });
      } catch (error) {
        raised = error;
      }

      expect(raised, `expected '${malformed}' to be rejected`).toBeInstanceOf(ProxyConfigurationError);
      const error = raised as ProxyConfigurationError;
      expect(error.reason).toBe('WEB_PROXY_TARGET_INVALID');
      expect(error.correlationId).toMatch(/^[0-9a-f-]{36}$/);
      expect(error.message).toContain('WEB_PROXY_TARGET_INVALID');
    }
  });

  it('rejects a non-loopback host with a reason token and correlation id', () => {
    for (const host of ['example.com', '10.0.0.5', '0.0.0.0', '169.254.169.254']) {
      let raised: unknown;
      try {
        resolveProxyTarget({ LOCAL_API_HOST: host });
      } catch (error) {
        raised = error;
      }

      expect(raised, `expected '${host}' to be rejected`).toBeInstanceOf(ProxyConfigurationError);
      const error = raised as ProxyConfigurationError;
      expect(error.reason).toBe('WEB_PROXY_TARGET_NOT_LOOPBACK');
      expect(error.correlationId).toMatch(/^[0-9a-f-]{36}$/);
    }
  });

  it('accepts the IPv6 loopback host', () => {
    const target = resolveProxyTarget({ LOCAL_API_HOST: '::1', LOCAL_API_PORT: '5080' });

    expect(target.origin).toBe('http://[::1]:5080');
  });
});

describe('assertExactProxyPaths', () => {
  it('accepts the exact operational health paths', () => {
    expect(() => assertExactProxyPaths(HEALTH_PROXY_PATHS)).not.toThrow();
  });

  it('rejects wildcards, the root path, relative paths, duplicates and trailing slashes', () => {
    const invalid: string[][] = [
      [],
      ['/'],
      ['/health/*'],
      ['/health/live?x=1'],
      ['health/live'],
      ['/health/live', '/health/live'],
      ['/health/live/']
    ];

    for (const paths of invalid) {
      expect(() => assertExactProxyPaths(paths), `expected ${JSON.stringify(paths)} to be rejected`).toThrow(
        ProxyConfigurationError
      );
    }
  });
});

describe('buildProxyOptions', () => {
  it('proxies only the two operational health paths and nothing else', () => {
    const options = buildProxyOptions(resolveProxyTarget({}));
    const keys = Object.keys(options);

    expect(keys).toHaveLength(1);
    const matcher = new RegExp(keys[0] as string);

    expect(matcher.test('/health/live')).toBe(true);
    expect(matcher.test('/health/ready')).toBe(true);
    expect(matcher.test('/health/live?probe=1')).toBe(true);
    expect(matcher.test('/health')).toBe(false);
    expect(matcher.test('/health/live/extra')).toBe(false);
    expect(matcher.test('/health/liveness')).toBe(false);
    expect(matcher.test('/api/contacts')).toBe(false);
    expect(matcher.test('/admin/tenants')).toBe(false);
    expect(matcher.test('/realms/crm')).toBe(false);
  });

  it('keeps the browser host header and targets the loopback API', () => {
    const options = buildProxyOptions(resolveProxyTarget({ LOCAL_API_PORT: '5080' }));
    const entry = Object.values(options)[0];

    expect(entry?.target).toBe('http://127.0.0.1:5080');
    expect(entry?.changeOrigin).toBe(false);
  });

  it('exposes a matcher source that Vite can compile as a regular expression', () => {
    expect(() => new RegExp(HEALTH_PROXY_MATCHER)).not.toThrow();
  });
});
