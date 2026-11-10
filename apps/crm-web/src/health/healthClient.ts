import { createApiClient } from '@crm-centric/operations-client';

/**
 * Result of a single operational health check.
 *
 * `ready` mirrors the backend semantics: readiness covers the database and schema only and is
 * not evidence of user, tenant or business readiness.
 */
export type HealthCheckResult =
  | { readonly kind: 'live'; readonly status: 'live' }
  | { readonly kind: 'ready'; readonly status: 'ready' }
  | {
      readonly kind: 'unavailable';
      readonly httpStatus: number;
      readonly reasonToken: string;
      readonly correlationId: string | null;
    };

export interface HealthClient {
  /** Liveness probe. Resolves even when the backend reports a failure. */
  checkLive(signal?: AbortSignal): Promise<HealthCheckResult>;
  /** Readiness probe. Resolves even when the backend reports a failure. */
  checkReady(signal?: AbortSignal): Promise<HealthCheckResult>;
}

export interface HealthClientOptions {
  /**
   * Same-origin base URL. The browser must call the dev server, never the API host directly,
   * so the default keeps every request relative to the current origin.
   */
  readonly baseUrl?: string;
}

const REASON_TOKEN_PATTERN = /^[A-Z][A-Z0-9_]{0,63}$/;
const CORRELATION_ID_PATTERN = /^[A-Za-z0-9-]{1,64}$/;

/** Local classification used when the server does not supply a usable reason token. */
const LOCAL_REASON_TOKEN = 'WEB_HEALTH_UNAVAILABLE';

interface LocationLike {
  readonly origin?: string;
}

/**
 * Resolves the base URL of the health client.
 *
 * The browser origin is used so the request stays same-origin and travels through the dev server
 * proxy. No environment, credential or server configuration is read here.
 */
function resolveBaseUrl(explicitBaseUrl: string | undefined): string {
  if (explicitBaseUrl !== undefined) {
    return explicitBaseUrl;
  }
  const location = (globalThis as unknown as { location?: LocationLike }).location;
  return location?.origin ?? '';
}

/**
 * Creates a health client on top of the generated operations client.
 *
 * The adapter adds no authentication, no cookie or tenant headers and stores no token: the
 * operational endpoints are anonymous by contract. A transport failure is not swallowed, it
 * propagates to the caller; the closed error path of the adapter is the HTTP Problem response.
 */
export function createHealthClient(options: HealthClientOptions = {}): HealthClient {
  const client = createApiClient(resolveBaseUrl(options.baseUrl));

  const run = async (
    kind: 'live' | 'ready',
    signal: AbortSignal | undefined
  ): Promise<HealthCheckResult> => {
    const requestOptions: { signal?: AbortSignal } = signal === undefined ? {} : { signal };
    const response = await (kind === 'live'
      ? client.GET('/health/live', requestOptions)
      : client.GET('/health/ready', requestOptions));
    if (response.error) {
      return {
        kind: 'unavailable',
        httpStatus: response.response.status,
        reasonToken: readReasonToken(response.error),
        correlationId: readCorrelationId(response.error, response.response)
      };
    }
    return kind === 'live'
      ? { kind: 'live', status: 'live' }
      : { kind: 'ready', status: 'ready' };
  };

  return {
    checkLive: (signal) => run('live', signal),
    checkReady: (signal) => run('ready', signal)
  };
}

function readReasonToken(error: unknown): string {
  if (typeof error === 'object' && error !== null && 'reasonToken' in error) {
    const token = (error as { reasonToken: unknown }).reasonToken;
    if (typeof token === 'string' && REASON_TOKEN_PATTERN.test(token)) {
      return token;
    }
  }
  return LOCAL_REASON_TOKEN;
}

function readCorrelationId(error: unknown, response: Response): string | null {
  const header = response.headers.get('x-correlation-id');
  if (header !== null && CORRELATION_ID_PATTERN.test(header)) {
    return header;
  }
  if (typeof error === 'object' && error !== null && 'correlationId' in error) {
    const correlationId = (error as { correlationId: unknown }).correlationId;
    if (typeof correlationId === 'string' && CORRELATION_ID_PATTERN.test(correlationId)) {
      return correlationId;
    }
  }
  return null;
}
