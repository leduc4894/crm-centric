import { createServer } from 'node:net';
import { describe, expect, it } from 'vitest';
import { listenerOwnedByProcessGroup, privateOperation } from './realOidcFixture.ts';
import * as fixtureModule from './realOidcFixture.ts';

describe('isolated browser fixture safeguards', () => {
  it('uses the CI owner for cleanup while retaining the isolated project owner locally', () => {
    const resolve = (fixtureModule as unknown as { fixtureResourceOwner?: (owner: string, ci?: string) => string }).fixtureResourceOwner;
    expect(resolve).toBeTypeOf('function');
    expect(resolve!('crm-e2e-project')).toBe('crm-e2e-project');
    expect(resolve!('crm-e2e-project', 'a'.repeat(32))).toBe('a'.repeat(32));
    expect(() => resolve!('crm-e2e-project', 'invalid')).toThrow('E2E_OWNER_INVALID');
  });
  it('redacts the underlying error and cause from credential operations', async () => {
    const sentinel = 'SYNTHETIC_PASSWORD_SECRET_8c0b';
    let captured: unknown;
    try { await privateOperation(async () => { throw new Error(`fill(${sentinel})`); }); } catch (error) { captured = error; }
    expect(captured).toBeInstanceOf(Error);
    const error = captured as Error;
    expect(error.message).toBe('E2E_PRIVATE_OPERATION_FAILED');
    expect(error.stack).not.toContain(sentinel);
    expect(error.cause).toBeUndefined();
  });
  it('recognizes only a listener held by the specified process group', async () => {
    const server = createServer();
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    try {
      const address = server.address(); if (!address || typeof address === 'string') throw new Error();
      const stat = await import('node:fs/promises').then(({ readFile }) => readFile('/proc/self/stat', 'utf8'));
      const group = Number(stat.slice(stat.lastIndexOf(')') + 2).split(' ')[2]);
      expect(await listenerOwnedByProcessGroup(address.port, group)).toBe(true);
      expect(await listenerOwnedByProcessGroup(address.port, 0)).toBe(false);
      expect(await listenerOwnedByProcessGroup(address.port, group + 1_000_000)).toBe(false);
    } finally { await new Promise<void>((resolve) => server.close(() => resolve())); }
  });
});
