import { useCallback, useEffect, useRef, useState } from 'react';
import { HEALTH_DEADLINE_MS, checkHealth, type HealthResult } from './healthClient.ts';

type HealthViewState =
  | { readonly status: 'loading' }
  | { readonly status: 'settled'; readonly result: HealthResult };

interface HealthDescription {
  /** The safe message of the current state. */
  readonly message: string;
  /** The correlation identifier of the current state, if the state owns one. */
  readonly correlation: string | null;
}

const LOADING_DESCRIPTION: HealthDescription = {
  message: 'Checking the platform connection…',
  correlation: null
};

/**
 * Describes a health result with a safe message.
 *
 * Only the validated reason token and the validated correlation identifier are rendered. The
 * response body, the problem title, the problem detail, the request URL and any token of the
 * payload are never rendered.
 */
function describe(result: HealthResult): HealthDescription {
  switch (result.kind) {
    case 'ready':
      return { message: 'The platform API is ready.', correlation: null };
    case 'unavailable':
      return {
        message: `The platform API is unavailable (${result.reasonToken}).`,
        correlation:
          result.serverCorrelationId === null
            ? null
            : `Server correlation: ${result.serverCorrelationId}`
      };
    case 'timeout':
      return {
        message: `The platform API did not answer within ${HEALTH_DEADLINE_MS / 1000} seconds (${result.reasonToken}).`,
        correlation: `Client correlation: ${result.clientCorrelationId}`
      };
    case 'network':
      return {
        message: `The platform API could not be reached (${result.reasonToken}).`,
        correlation: `Client correlation: ${result.clientCorrelationId}`
      };
    case 'invalid':
      return {
        message: `The platform API returned an unexpected response (${result.reasonToken}).`,
        correlation: `Client correlation: ${result.clientCorrelationId}`
      };
  }
}

/**
 * The operational connection status of the platform.
 *
 * The component performs one health check when it mounts and one more when the operator asks for
 * it. It never polls and never retries on its own, and it never persists the health result. The
 * current state is announced through an `aria-live` region and the check can be repeated with the
 * keyboard.
 */
export function HealthStatus() {
  const [state, setState] = useState<HealthViewState>({ status: 'loading' });
  const controllerRef = useRef<AbortController | null>(null);
  const sequenceRef = useRef(0);

  const beginCheck = useCallback(() => {
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    const sequence = ++sequenceRef.current;

    const check = async (): Promise<void> => {
      try {
        const result = await checkHealth(controller.signal);
        if (sequence !== sequenceRef.current) {
          return;
        }
        setState({ status: 'settled', result });
      } catch (error: unknown) {
        if (controller.signal.aborted) {
          return;
        }
        throw error;
      }
    };
    void check();
  }, []);

  const repeatCheck = useCallback(() => {
    setState({ status: 'loading' });
    beginCheck();
  }, [beginCheck]);

  useEffect(() => {
    beginCheck();
    return () => {
      controllerRef.current?.abort();
      controllerRef.current = null;
    };
  }, [beginCheck]);

  const description = state.status === 'loading' ? LOADING_DESCRIPTION : describe(state.result);

  return (
    <section aria-labelledby="platform-connection-heading">
      <h2 id="platform-connection-heading">Platform connection</h2>
      <div role="status" aria-live="polite" aria-atomic="true">
        <p>{description.message}</p>
        {description.correlation === null ? null : <p>{description.correlation}</p>}
      </div>
      <button type="button" onClick={repeatCheck}>
        Check again
      </button>
    </section>
  );
}
