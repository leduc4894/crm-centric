import { fileURLToPath } from 'node:url';

/**
 * The generated operations client is consumed from its generated TypeScript entry point.
 *
 * The published package entry point is the compiled `dist/` output, which is a build artifact and
 * is not committed. Resolving the package to its generated source keeps the frontend on the
 * generated public surface without writing build output into the byte-compared `sdks/` tree.
 */
export const operationsClientEntry = fileURLToPath(
  new URL('../../../sdks/typescript/operations/index.ts', import.meta.url)
);

export const workspaceAliases = [
  { find: /^@crm-centric\/operations-client$/, replacement: operationsClientEntry }
];
