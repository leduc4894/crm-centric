import { createApiClient } from '@crm-centric/operations-client';

/**
 * The total deadline of a single health check, including the body read.
 */
export const HEALTH_DEADLINE_MS = 5_000;

/**
 * The local classification codes of the closed error paths of the health adapter.
 *
 * The server reason token is used only when the server sent a valid Problem. A timeout, a
 * transport failure and a response that violates the contract are local classifications and are
 * never presented as a server reason.
 */
export const LOCAL_REASON_TOKEN = {
  timeout: 'WEB_REQUEST_TIMEOUT',
  network: 'WEB_NETWORK_ERROR',
  invalid: 'WEB_CONTRACT_INVALID'
} as const;

/**
 * The result of a single operational health check.
 *
 * `ready` mirrors the backend semantics: readiness covers the database and schema only and is not
 * evidence of user, tenant or business readiness. The failing variants are discriminated by
 * `kind`, and a server correlation identifier is only ever carried by the variant that the server
 * actually answered with.
 */
export type HealthResult =
  | { readonly kind: 'ready' }
  | {
      readonly kind: 'unavailable';
      readonly httpStatus: number;
      readonly reasonToken: string;
      readonly serverCorrelationId: string | null;
      readonly clientCorrelationId: null;
    }
  | {
      readonly kind: 'timeout';
      readonly reasonToken: typeof LOCAL_REASON_TOKEN.timeout;
      readonly serverCorrelationId: null;
      readonly clientCorrelationId: string;
    }
  | {
      readonly kind: 'network';
      readonly reasonToken: typeof LOCAL_REASON_TOKEN.network;
      readonly serverCorrelationId: null;
      readonly clientCorrelationId: string;
    }
  | {
      readonly kind: 'invalid';
      readonly reasonToken: typeof LOCAL_REASON_TOKEN.invalid;
      readonly serverCorrelationId: null;
      readonly clientCorrelationId: string;
    };

/** The operational readiness endpoint of the generated operations client. */
const READINESS_PATH = '/health/ready';

/** The reason token pattern of the canonical Problem schema. */
const REASON_TOKEN_PATTERN = /^[A-Z][A-Z0-9_]+$/;

/**
 * The correlation identifier pattern that is safe to display.
 *
 * The canonical contract allows any string, but only an identifier without whitespace or
 * punctuation is presented to the operator.
 */
const CORRELATION_ID_PATTERN = /^[A-Za-z0-9-]{1,64}$/;

const MAX_REASON_TOKEN_LENGTH = 64;
const MAX_PROBLEM_TYPE_LENGTH = 512;
const MAX_PROBLEM_TITLE_LENGTH = 256;
const MAX_PROBLEM_DETAIL_LENGTH = 1000;
const MAX_CORRELATION_ID_LENGTH = 128;

type LocalFailureKind = 'timeout' | 'network' | 'invalid';

interface ValidatedProblem {
  readonly reasonToken: string;
  readonly correlationId: string;
}

/**
 * Performs one operational health check.
 *
 * The request is anonymous and same-origin: it travels through the development proxy to the
 * loopback API and carries no authorization, cookie or tenant header. The whole check, including
 * the body read, is bound by a deadline of five seconds. Every outcome is a result; the only
 * rejection is the cancellation the caller asked for.
 */
export async function checkHealth(signal: AbortSignal): Promise<HealthResult> {
  if (signal.aborted) {
    throw abortReason(signal);
  }

  const controller = new AbortController();
  let deadlineExpired = false;
  const cancel = (): void => {
    controller.abort(signal.reason);
  };
  signal.addEventListener('abort', cancel, { once: true });
  const deadline = setTimeout(() => {
    deadlineExpired = true;
    controller.abort();
  }, HEALTH_DEADLINE_MS);

  try {
    const client = createApiClient(resolveBaseUrl());
    const response = await client.GET(READINESS_PATH, { signal: controller.signal });
    if (response.error !== undefined) {
      return classifyProblem(response.error, response.response);
    }
    return isReadinessPayload(response.data) ? { kind: 'ready' } : localFailure('invalid');
  } catch (error: unknown) {
    if (deadlineExpired) {
      return localFailure('timeout');
    }
    if (signal.aborted) {
      throw abortReason(signal);
    }
    if (isBodyParseFailure(error)) {
      return localFailure('invalid');
    }
    return localFailure('network');
  } finally {
    clearTimeout(deadline);
    signal.removeEventListener('abort', cancel);
  }
}

/**
 * Maps a Problem response to an unavailable result.
 *
 * The Problem boundary is validated before anything is exposed: a body that does not match the
 * canonical shape is a contract violation, not a server failure.
 */
function classifyProblem(error: unknown, response: Response): HealthResult {
  const problem = validateProblem(error);
  if (problem === null) {
    return localFailure('invalid');
  }
  const header = response.headers.get('x-correlation-id');
  return {
    kind: 'unavailable',
    httpStatus: response.status,
    reasonToken: problem.reasonToken,
    serverCorrelationId:
      displayableCorrelationId(problem.correlationId) ??
      (header === null ? null : displayableCorrelationId(header)),
    clientCorrelationId: null
  };
}

/**
 * Validates the canonical Problem shape.
 *
 * Only the required fields, their types and their length bounds are checked. Nothing of the
 * payload is rendered, so an unknown property is not a reason to hide the server failure.
 */
function validateProblem(value: unknown): ValidatedProblem | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return null;
  }
  const problem = value as Record<string, unknown>;
  const type = problem['type'];
  const title = problem['title'];
  const status = problem['status'];
  const reasonToken = problem['reasonToken'];
  const correlationId = problem['correlationId'];
  const detail = problem['detail'];

  if (typeof type !== 'string' || type.length > MAX_PROBLEM_TYPE_LENGTH) {
    return null;
  }
  if (typeof title !== 'string' || title.length > MAX_PROBLEM_TITLE_LENGTH) {
    return null;
  }
  if (typeof status !== 'number' || !Number.isInteger(status) || status < 400 || status > 599) {
    return null;
  }
  if (
    typeof reasonToken !== 'string' ||
    reasonToken.length > MAX_REASON_TOKEN_LENGTH ||
    !REASON_TOKEN_PATTERN.test(reasonToken)
  ) {
    return null;
  }
  if (
    typeof correlationId !== 'string' ||
    correlationId.length === 0 ||
    correlationId.length > MAX_CORRELATION_ID_LENGTH
  ) {
    return null;
  }
  if (detail !== undefined && (typeof detail !== 'string' || detail.length > MAX_PROBLEM_DETAIL_LENGTH)) {
    return null;
  }
  return { reasonToken, correlationId };
}

/** Validates the documented readiness payload. */
function isReadinessPayload(value: unknown): boolean {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return false;
  }
  const payload = value as Record<string, unknown>;
  const keys = Object.keys(payload);
  return keys.length === 1 && keys[0] === 'status' && payload['status'] === 'ready';
}

/** Returns the correlation identifier only when it is safe to display. */
function displayableCorrelationId(value: string): string | null {
  return CORRELATION_ID_PATTERN.test(value) ? value : null;
}

/** Builds the result of a closed local error path. */
function localFailure(kind: LocalFailureKind): HealthResult {
  const clientCorrelationId = createClientCorrelationId();
  switch (kind) {
    case 'timeout':
      return {
        kind: 'timeout',
        reasonToken: LOCAL_REASON_TOKEN.timeout,
        serverCorrelationId: null,
        clientCorrelationId
      };
    case 'network':
      return {
        kind: 'network',
        reasonToken: LOCAL_REASON_TOKEN.network,
        serverCorrelationId: null,
        clientCorrelationId
      };
    case 'invalid':
      return {
        kind: 'invalid',
        reasonToken: LOCAL_REASON_TOKEN.invalid,
        serverCorrelationId: null,
        clientCorrelationId
      };
  }
}

/**
 * Creates the correlation identifier of a local error path.
 *
 * The prefix marks the identifier as a client one, so it can never be mistaken for a server
 * correlation identifier.
 */
function createClientCorrelationId(): string {
  const uuid = globalThis.crypto?.randomUUID?.();
  if (typeof uuid === 'string') {
    return `web-${uuid.replaceAll('-', '').slice(0, 12)}`;
  }
  return `web-${Math.random().toString(16).slice(2, 14).padEnd(12, '0')}`;
}

/** Detects a body that could not be parsed as JSON. */
function isBodyParseFailure(error: unknown): boolean {
  return (
    typeof error === 'object' &&
    error !== null &&
    'name' in error &&
    (error as { name?: unknown }).name === 'SyntaxError'
  );
}

/** Returns the cancellation reason of the caller's signal. */
function abortReason(signal: AbortSignal): unknown {
  return signal.reason ?? new DOMException('The health check was aborted.', 'AbortError');
}

interface LocationLike {
  readonly origin?: string;
}

/**
 * Resolves the base URL of the health client.
 *
 * The browser origin is used so the request stays same-origin and travels through the dev server
 * proxy. No environment, credential or server configuration is read here.
 */
function resolveBaseUrl(): string {
  const location = (globalThis as unknown as { location?: LocationLike }).location;
  return location?.origin ?? '';
}
