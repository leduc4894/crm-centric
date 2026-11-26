import { describe, expect, it } from 'vitest';
import { parseOidcConfig } from './oidcConfig.ts';

const local = {
  MODE: 'development', VITE_OIDC_ISSUER: 'http://localhost:8080/realms/crm-local',
  VITE_OIDC_CLIENT_ID: 'crm-web-local', VITE_OIDC_CALLBACK_URL: 'http://localhost:5173/auth/callback',
  VITE_OIDC_LOGOUT_URL: 'http://localhost:5173/', VITE_OIDC_LOCAL_HTTP: 'true'
};

describe('OIDC configuration', () => {
  it('accepts explicitly enabled localhost development', () => {
    expect(parseOidcConfig(local).issuer).toBe(local.VITE_OIDC_ISSUER);
  });
  it('rejects callbacks on another origin', () => {
    expect(() => parseOidcConfig(local, 'http://localhost:9999')).toThrow(/OIDC_CONFIG_INVALID/);
  });
  it.each([
    { ...local, VITE_OIDC_LOCAL_HTTP: 'false' },
    { ...local, MODE: 'production' },
    { ...local, VITE_OIDC_ISSUER: 'http://evil.example/realm' },
    { ...local, VITE_OIDC_CLIENT_ID: '' },
    { ...local, VITE_OIDC_CALLBACK_URL: 'http://localhost:5173/other' },
    { ...local, VITE_OIDC_CALLBACK_URL: 'http://localhost:5173/auth/callback?code=bad' },
    { ...local, VITE_OIDC_LOGOUT_URL: 'http://localhost:5173/other' },
    { ...local, VITE_OIDC_LOGOUT_URL: 'http://evil.example/' }
  ])('rejects unsafe configuration', (env) => {
    expect(() => parseOidcConfig(env)).toThrow(/OIDC_CONFIG_INVALID/);
  });
});
