// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { HealthStatus } from './HealthStatus.tsx';
import {
  deferred,
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
  cleanup();
  globalThis.fetch = ORIGINAL_FETCH;
  vi.unstubAllGlobals();
});

function statusText(): string {
  return screen.getByRole('status').textContent ?? '';
}

describe('HealthStatus', () => {
  it('shows the loading state and then the ready state', async () => {
    stub.respondWith(() => jsonResponse({ status: 'ready' }));

    render(<HealthStatus />);

    expect(statusText()).toContain('Checking the platform connection');
    expect(await screen.findByText('The platform API is ready.')).toBeTruthy();
  });

  it('announces the state change in an aria-live region', async () => {
    stub.respondWith(() => jsonResponse({ status: 'ready' }));

    render(<HealthStatus />);

    const region = screen.getByRole('status');
    expect(region.getAttribute('aria-live')).toBe('polite');
    await screen.findByText('The platform API is ready.');
    expect(region.textContent).toContain('The platform API is ready.');
  });

  it('shows the server reason token and the server correlation of a 503 Problem', async () => {
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

    render(<HealthStatus />);

    expect(await screen.findByText(/DATABASE_SCHEMA_NOT_READY/)).toBeTruthy();
    expect(screen.getByText('Server correlation: 0HNE5Q1J2K3L4')).toBeTruthy();
  });

  it('does not show a forged server correlation id', async () => {
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

    render(<HealthStatus />);

    expect(await screen.findByText(/DATABASE_UNAVAILABLE/)).toBeTruthy();
    expect(screen.queryByText(/Server correlation:/)).toBeNull();
    expect(screen.getByText(/Client correlation: web-[0-9a-f]{12}/)).toBeTruthy();
  });

  it('shows a local network classification without a server correlation', async () => {
    stub.respondWith(() => {
      throw new TypeError('Failed to fetch');
    });

    render(<HealthStatus />);

    expect(await screen.findByText(/WEB_NETWORK_ERROR/)).toBeTruthy();
    expect(screen.queryByText(/Server correlation:/)).toBeNull();
    expect(screen.getByText(/Client correlation: web-[0-9a-f]{12}/)).toBeTruthy();
  });

  it('shows a local invalid classification for a response that violates the contract', async () => {
    stub.respondWith(() => jsonResponse({ status: 'degraded' }));

    render(<HealthStatus />);

    expect(await screen.findByText(/WEB_CONTRACT_INVALID/)).toBeTruthy();
    expect(screen.queryByText(/Server correlation:/)).toBeNull();
  });

  it('shows a local timeout classification when the body does not arrive in time', async () => {
    stub.respondWith((request) => hungBodyResponse(request.signal));

    render(<HealthStatus />);

    expect(await screen.findByText(/WEB_REQUEST_TIMEOUT/, {}, { timeout: 15_000 })).toBeTruthy();
    expect(screen.queryByText(/Server correlation:/)).toBeNull();
  });

  it('repeats the check when the retry action is used with the keyboard', async () => {
    const user = userEvent.setup();
    stub.respondWith(() => jsonResponse({ status: 'ready' }));

    render(<HealthStatus />);
    await screen.findByText('The platform API is ready.');

    await user.tab();
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Check again' }));
    await user.keyboard('{Enter}');

    await waitFor(() => expect(stub.requests).toHaveLength(2));
  });

  it('cancels the pending request when the component is unmounted', async () => {
    stub.respondWith((request) => hungBodyResponse(request.signal));

    const { unmount } = render(<HealthStatus />);
    await waitFor(() => expect(stub.requests).toHaveLength(1));
    expect(stub.requests[0]?.signal?.aborted).toBe(false);

    unmount();

    expect(stub.requests[0]?.signal?.aborted).toBe(true);
  });

  it('cancels the pending request when the check is repeated', async () => {
    const user = userEvent.setup();
    stub.respondWith((request) => hungBodyResponse(request.signal));

    render(<HealthStatus />);
    await waitFor(() => expect(stub.requests).toHaveLength(1));

    await user.click(screen.getByRole('button', { name: 'Check again' }));

    await waitFor(() => expect(stub.requests).toHaveLength(2));
    expect(stub.requests[0]?.signal?.aborted).toBe(true);
  });

  it('does not let a stale response overwrite the state of a newer check', async () => {
    const first = deferred<Response>();
    stub.respondWith(() => (stub.requests.length === 1 ? first.promise : jsonResponse({ status: 'ready' })));

    render(<HealthStatus />);
    await waitFor(() => expect(stub.requests).toHaveLength(1));

    await userEvent.setup().click(screen.getByRole('button', { name: 'Check again' }));
    expect(await screen.findByText('The platform API is ready.')).toBeTruthy();

    first.resolve(
      problemResponse(
        {
          type: 'urn:crm:problem:database-unavailable',
          title: 'Dependency unavailable',
          status: 503,
          reasonToken: 'DATABASE_UNAVAILABLE',
          correlationId: '0HNE5Q1J2K3L4'
        },
        { status: 503 }
      )
    );
    await act(async () => {
      await Promise.resolve();
    });

    expect(statusText()).toContain('The platform API is ready.');
    expect(statusText()).not.toContain('DATABASE_UNAVAILABLE');
  });

  it('never renders the raw response body, a stack trace, a token or the request URL', async () => {
    const sentinel = 'SENTINEL_TOKEN_4f2c9a';
    stub.respondWith(() =>
      problemResponse(
        {
          type: 'urn:crm:problem:database-unavailable',
          title: '<img src=x onerror="globalThis.__xss=1">',
          status: 503,
          reasonToken: 'DATABASE_UNAVAILABLE',
          correlationId: '0HNE5Q1J2K3L4',
          detail: `Bearer ${sentinel}\n    at Object.<anonymous> (/srv/app/index.js:1:1)`
        },
        { status: 503 }
      )
    );

    render(<HealthStatus />);
    await screen.findByText(/DATABASE_UNAVAILABLE/);

    const text = document.body.textContent ?? '';
    expect(text).not.toContain(sentinel);
    expect(text).not.toContain('<img');
    expect(text).not.toContain('onerror');
    expect(text).not.toContain('/srv/app/index.js');
    expect(text).not.toContain('/health/ready');
    expect(document.querySelector('img')).toBeNull();
    expect((globalThis as { __xss?: unknown }).__xss).toBeUndefined();
  });
});
