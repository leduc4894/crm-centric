import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createHealthClient } from './healthClient.ts';

const ORIGINAL_FETCH = globalThis.fetch;

interface CapturedRequest {
  readonly request: Request;
}

function jsonResponse(body: unknown, init: { status?: number; headers?: Record<string, string> } = {}): Response {
  return new Response(JSON.stringify(body), {
    status: init.status ?? 200,
    headers: { 'content-type': 'application/json', ...(init.headers ?? {}) }
  });
}

let captured: CapturedRequest[] = [];
let respond: (request: Request) => Response = () => jsonResponse({ status: 'live' });

beforeEach(() => {
  captured = [];
  respond = () => jsonResponse({ status: 'live' });
  globalThis.fetch = vi.fn(async (input: RequestInfo | URL) => {
    const request = input instanceof Request ? input : new Request(input);
    captured.push({ request });
    return respond(request);
  }) as unknown as typeof globalThis.fetch;
  vi.stubGlobal('location', { origin: 'http://localhost:5173' });
});

afterEach(() => {
  globalThis.fetch = ORIGINAL_FETCH;
  vi.unstubAllGlobals();
});

describe('createHealthClient', () => {
  it('calls the dev server origin and never the API host directly', async () => {
    const client = createHealthClient();

    await client.checkLive();

    expect(captured).toHaveLength(1);
    expect(captured[0]?.request.url).toBe('http://localhost:5173/health/live');
    expect(captured[0]?.request.method).toBe('GET');
  });

  it('adds no authorization, cookie or tenant header', async () => {
    const client = createHealthClient();

    await client.checkReady();

    const request = captured[0]?.request;
    expect(request).toBeDefined();
    expect(request?.headers.get('authorization')).toBeNull();
    expect(request?.headers.get('cookie')).toBeNull();
    expect(request?.headers.get('x-tenant-id')).toBeNull();
    expect(request?.headers.get('x-correlation-id')).toBeNull();
    expect(request?.credentials).toBe('omit');
  });

  it('reports liveness', async () => {
    respond = () => jsonResponse({ status: 'live' });
    const client = createHealthClient();

    await expect(client.checkLive()).resolves.toEqual({ kind: 'live', status: 'live' });
  });

  it('reports readiness', async () => {
    respond = () => jsonResponse({ status: 'ready' });
    const client = createHealthClient();

    await expect(client.checkReady()).resolves.toEqual({ kind: 'ready', status: 'ready' });
  });

  it('maps a 503 Problem to an unavailable result with the server reason token and correlation id', async () => {
    respond = () =>
      jsonResponse(
        {
          type: 'urn:crm:problem:database-schema-not-ready',
          title: 'Database schema is not ready',
          status: 503,
          reasonToken: 'DATABASE_SCHEMA_NOT_READY',
          correlationId: '0HNE5Q1J2K3L4'
        },
        { status: 503, headers: { 'x-correlation-id': '0HNE5Q1J2K3L4' } }
      );
    const client = createHealthClient();

    await expect(client.checkReady()).resolves.toEqual({
      kind: 'unavailable',
      httpStatus: 503,
      reasonToken: 'DATABASE_SCHEMA_NOT_READY',
      correlationId: '0HNE5Q1J2K3L4'
    });
  });

  it('does not trust a malformed server correlation id', async () => {
    respond = () =>
      jsonResponse(
        {
          type: 'urn:crm:problem:database-unavailable',
          title: 'Database is unavailable',
          status: 503,
          reasonToken: 'DATABASE_UNAVAILABLE',
          correlationId: 'not a valid correlation id'
        },
        { status: 503, headers: { 'x-correlation-id': 'not a valid correlation id' } }
      );
    const client = createHealthClient();

    const result = await client.checkReady();

    expect(result.kind).toBe('unavailable');
    if (result.kind !== 'unavailable') {
      throw new Error('expected an unavailable result');
    }
    expect(result.correlationId).toBeNull();
    expect(result.reasonToken).toBe('DATABASE_UNAVAILABLE');
  });

  it('falls back to a local classification when the server sends no reason token', async () => {
    respond = () => jsonResponse({ status: 503 }, { status: 503 });
    const client = createHealthClient();

    const result = await client.checkLive();

    expect(result.kind).toBe('unavailable');
    if (result.kind !== 'unavailable') {
      throw new Error('expected an unavailable result');
    }
    expect(result.reasonToken).toMatch(/^WEB_[A-Z0-9_]+$/);
    expect(result.correlationId).toBeNull();
  });

  it('propagates the abort signal to the request', async () => {
    const controller = new AbortController();
    const client = createHealthClient();

    const pending = client.checkLive(controller.signal);
    await vi.waitFor(() => expect(captured).toHaveLength(1));
    const request = captured[0]?.request;
    expect(request?.signal.aborted).toBe(false);

    controller.abort();
    await pending.catch(() => undefined);

    expect(request?.signal.aborted).toBe(true);
  });

  it('persists no health, token or contact data in browser storage', async () => {
    const localStorageWrite = vi.fn();
    const sessionStorageWrite = vi.fn();
    vi.stubGlobal('localStorage', { setItem: localStorageWrite, getItem: vi.fn(), removeItem: vi.fn() });
    vi.stubGlobal('sessionStorage', { setItem: sessionStorageWrite, getItem: vi.fn(), removeItem: vi.fn() });

    const client = createHealthClient();
    await client.checkLive();
    await client.checkReady();

    expect(localStorageWrite).not.toHaveBeenCalled();
    expect(sessionStorageWrite).not.toHaveBeenCalled();
  });
});
