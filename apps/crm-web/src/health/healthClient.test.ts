import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { HEALTH_DEADLINE_MS, LOCAL_REASON_TOKEN, checkHealth } from './healthClient.ts';
import {
  hungBodyResponse,
  installFetchStub,
  jsonResponse,
  problemResponse,
  type FetchStub
} from './testing/fetchStub.ts';

const ORIGINAL_FETCH = globalThis.fetch;

let stub: FetchStub;

beforeEach(() => {
  stub = installFetchStub();
  vi.stubGlobal('location', { origin: 'http://localhost:5173' });
});

afterEach(() => {
  globalThis.fetch = ORIGINAL_FETCH;
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('checkHealth', () => {
  it('bounds the whole check by the five second deadline of the contract', () => {
    expect(HEALTH_DEADLINE_MS).toBe(5_000);
  });

  it('calls the readiness endpoint on the browser origin and never the API host directly', async () => {
    await checkHealth(new AbortController().signal);

    expect(stub.requests).toHaveLength(1);
    expect(stub.requests[0]?.url).toBe('http://localhost:5173/health/ready');
    expect(stub.requests[0]?.method).toBe('GET');
  });

  it('adds no authorization, cookie or tenant header and omits credentials', async () => {
    await checkHealth(new AbortController().signal);

    const request = stub.requests[0];
    expect(request?.headers.get('authorization')).toBeNull();
    expect(request?.headers.get('cookie')).toBeNull();
    expect(request?.headers.get('x-tenant-id')).toBeNull();
    expect(request?.headers.get('x-correlation-id')).toBeNull();
    expect(request?.credentials).toBe('omit');
  });

  it('reports readiness for the documented readiness payload', async () => {
    stub.respondWith(() => jsonResponse({ status: 'ready' }));

    await expect(checkHealth(new AbortController().signal)).resolves.toEqual({ kind: 'ready' });
  });

  it('maps a 503 Problem to an unavailable result with the server reason token and correlation id', async () => {
    stub.respondWith(() =>
      problemResponse(
        {
          type: 'urn:crm:problem:database-schema-not-ready',
          title: 'Dependency unavailable',
          status: 503,
          reasonToken: 'DATABASE_SCHEMA_NOT_READY',
          correlationId: '0HNE5Q1J2K3L4'
        },
        { status: 503, headers: { 'x-correlation-id': '0HNE5Q1J2K3L4' } }
      )
    );

    await expect(checkHealth(new AbortController().signal)).resolves.toEqual({
      kind: 'unavailable',
      httpStatus: 503,
      reasonToken: 'DATABASE_SCHEMA_NOT_READY',
      serverCorrelationId: '0HNE5Q1J2K3L4',
      clientCorrelationId: expect.stringMatching(/^web-[0-9a-f]{12}$/)
    });
  });

  it('does not trust a malformed server correlation id', async () => {
    stub.respondWith(() =>
      problemResponse(
        {
          type: 'urn:crm:problem:database-unavailable',
          title: 'Dependency unavailable',
          status: 503,
          reasonToken: 'DATABASE_UNAVAILABLE',
          correlationId: 'not a valid correlation id'
        },
        { status: 503, headers: { 'x-correlation-id': 'not a valid correlation id' } }
      )
    );

    const result = await checkHealth(new AbortController().signal);

    expect(result.kind).toBe('unavailable');
    if (result.kind !== 'unavailable') {
      throw new Error('expected an unavailable result');
    }
    expect(result.serverCorrelationId).toBeNull();
    expect(result.reasonToken).toBe('DATABASE_UNAVAILABLE');
    expect(result.clientCorrelationId).toMatch(/^web-[0-9a-f]{12}$/);
  });

  it('classifies a Problem without a valid reason token as an invalid contract', async () => {
    stub.respondWith(() => problemResponse({ status: 503 }, { status: 503 }));

    const result = await checkHealth(new AbortController().signal);

    expect(result.kind).toBe('invalid');
    if (result.kind !== 'invalid') {
      throw new Error('expected an invalid result');
    }
    expect(result.reasonToken).toBe(LOCAL_REASON_TOKEN.invalid);
    expect(result.serverCorrelationId).toBeNull();
    expect(result.clientCorrelationId).toMatch(/^web-[0-9a-f]{12}$/);
  });

  it('classifies a success payload that is not the documented shape as an invalid contract', async () => {
    stub.respondWith(() => jsonResponse({ status: 'degraded' }));

    const result = await checkHealth(new AbortController().signal);

    expect(result.kind).toBe('invalid');
    if (result.kind !== 'invalid') {
      throw new Error('expected an invalid result');
    }
    expect(result.reasonToken).toBe(LOCAL_REASON_TOKEN.invalid);
  });

  it('classifies a success body that is not JSON as an invalid contract', async () => {
    stub.respondWith(
      () =>
        new Response('<html>not json</html>', {
          status: 200,
          headers: { 'content-type': 'application/json' }
        })
    );

    const result = await checkHealth(new AbortController().signal);

    expect(result.kind).toBe('invalid');
    if (result.kind !== 'invalid') {
      throw new Error('expected an invalid result');
    }
    expect(result.reasonToken).toBe(LOCAL_REASON_TOKEN.invalid);
  });

  it('classifies a transport failure as a network error without a server correlation', async () => {
    stub.respondWith(() => {
      throw new TypeError('Failed to fetch');
    });

    const result = await checkHealth(new AbortController().signal);

    expect(result.kind).toBe('network');
    if (result.kind !== 'network') {
      throw new Error('expected a network result');
    }
    expect(result.reasonToken).toBe(LOCAL_REASON_TOKEN.network);
    expect(result.serverCorrelationId).toBeNull();
    expect(result.clientCorrelationId).toMatch(/^web-[0-9a-f]{12}$/);
  });

  it('aborts a pending body when the deadline expires', async () => {
    vi.useFakeTimers();
    stub.respondWith((request) => hungBodyResponse(request.signal));

    const pending = checkHealth(new AbortController().signal);
    await vi.advanceTimersByTimeAsync(HEALTH_DEADLINE_MS);
    const result = await pending;

    expect(result.kind).toBe('timeout');
    if (result.kind !== 'timeout') {
      throw new Error('expected a timeout result');
    }
    expect(result.reasonToken).toBe(LOCAL_REASON_TOKEN.timeout);
    expect(result.serverCorrelationId).toBeNull();
    expect(stub.requests[0]?.signal?.aborted).toBe(true);
  });

  it('rejects when the caller cancels the request', async () => {
    stub.respondWith((request) => hungBodyResponse(request.signal));

    const controller = new AbortController();
    const pending = checkHealth(controller.signal);
    controller.abort();

    await expect(pending).rejects.toBeDefined();
    expect(stub.requests[0]?.signal?.aborted).toBe(true);
  });

  it('persists no health, token or contact data in browser storage', async () => {
    const localStorageWrite = vi.fn();
    const sessionStorageWrite = vi.fn();
    vi.stubGlobal('localStorage', { setItem: localStorageWrite, getItem: vi.fn(), removeItem: vi.fn() });
    vi.stubGlobal('sessionStorage', { setItem: sessionStorageWrite, getItem: vi.fn(), removeItem: vi.fn() });

    await checkHealth(new AbortController().signal);

    expect(localStorageWrite).not.toHaveBeenCalled();
    expect(sessionStorageWrite).not.toHaveBeenCalled();
  });
});
