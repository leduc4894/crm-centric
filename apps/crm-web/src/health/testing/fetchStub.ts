import { vi } from 'vitest';

/**
 * Test-only support for the health adapter and the health UI.
 *
 * The module is never imported by application code; it only provides the synthetic responses the
 * unit and component tests need. It contains no production behaviour.
 */

export interface CapturedRequest {
  readonly url: string;
  readonly method: string;
  readonly headers: Headers;
  readonly credentials: RequestCredentials;
  readonly signal: AbortSignal | null;
}

export type StubResponder = (request: CapturedRequest) => Response | Promise<Response>;

export interface FetchStub {
  /** Every request the stub received, in order. */
  readonly requests: CapturedRequest[];
  /** Replaces the responder of the stub. */
  respondWith(responder: StubResponder): void;
}

export interface ResponseOptions {
  readonly status?: number;
  readonly headers?: Record<string, string>;
}

export interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
}

/** Creates a promise that the test resolves when it decides to. */
export function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((settle) => {
    resolve = settle;
  });
  return { promise, resolve };
}

/** Builds a JSON response with the given body. */
export function jsonResponse(body: unknown, options: ResponseOptions = {}): Response {
  return new Response(JSON.stringify(body), {
    status: options.status ?? 200,
    headers: { 'content-type': 'application/json', ...(options.headers ?? {}) }
  });
}

/** Builds an RFC 9457 problem response with the given body. */
export function problemResponse(body: unknown, options: ResponseOptions = {}): Response {
  return new Response(JSON.stringify(body), {
    status: options.status ?? 503,
    headers: { 'content-type': 'application/problem+json', ...(options.headers ?? {}) }
  });
}

/**
 * Builds a response whose body never completes.
 *
 * The body fails when the request signal aborts, which is how a real server behaves when a
 * connection is closed mid-body.
 */
export function hungBodyResponse(signal: AbortSignal | null): Response {
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      signal?.addEventListener(
        'abort',
        () => {
          controller.error(new DOMException('The request was aborted.', 'AbortError'));
        },
        { once: true }
      );
    }
  });
  return new Response(body, { status: 200, headers: { 'content-type': 'application/json' } });
}

/**
 * Replaces the global fetch with a recording stub.
 *
 * The stub never performs a network request; it only answers with the synthetic responses the
 * test provides.
 */
export function installFetchStub(): FetchStub {
  const requests: CapturedRequest[] = [];
  let responder: StubResponder = () => jsonResponse({ status: 'ready' });

  globalThis.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(input, init);
    requests.push({
      url: request.url,
      method: request.method,
      headers: request.headers,
      credentials: request.credentials,
      signal: request.signal
    });
    return responder(request);
  }) as unknown as typeof globalThis.fetch;

  return {
    requests,
    respondWith(next: StubResponder) {
      responder = next;
    }
  };
}
