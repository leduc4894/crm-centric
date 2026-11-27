export interface OidcConfig {
  issuer: string;
  clientId: string;
  callbackUrl: string;
  logoutUrl: string;
  allowLocalHttp: boolean;
}

export function parseOidcConfig(env: Record<string, unknown>, currentOrigin?: string): OidcConfig {
  try {
    const issuerText = String(env.VITE_OIDC_ISSUER ?? '');
    const clientId = String(env.VITE_OIDC_CLIENT_ID ?? '');
    const callbackText = String(env.VITE_OIDC_CALLBACK_URL ?? '');
    const logoutText = String(env.VITE_OIDC_LOGOUT_URL ?? '');
    const issuer = new URL(issuerText);
    const callback = new URL(callbackText);
    const logout = new URL(logoutText);
    const allowLocalHttp = env.MODE === 'development' && env.VITE_OIDC_LOCAL_HTTP === 'true';
    const safeUrl = (url: URL) => !url.username && !url.password && !url.search && !url.hash;
    const secure = (url: URL) => url.protocol === 'https:' ||
      (allowLocalHttp && url.protocol === 'http:' && url.hostname === 'localhost');
    if (!clientId || clientId.trim() !== clientId || !safeUrl(issuer) || !safeUrl(callback) ||
        !safeUrl(logout) || !secure(issuer) || !secure(callback) || !secure(logout) ||
        issuer.href !== issuerText || callback.href !== callbackText || logout.href !== logoutText ||
        callback.pathname !== '/auth/callback' || logout.pathname !== '/' ||
        callback.origin !== logout.origin || (currentOrigin !== undefined && callback.origin !== currentOrigin)) throw new Error();
    return { issuer: issuerText, clientId, callbackUrl: callbackText, logoutUrl: logoutText, allowLocalHttp };
  } catch {
    throw new Error(`OIDC_CONFIG_INVALID ${crypto.randomUUID()}`);
  }
}
