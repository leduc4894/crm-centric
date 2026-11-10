import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { workspaceAliases } from './config/aliases.ts';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: workspaceAliases
  },
  test: {
    environment: 'node',
    include: [
      'src/**/*.test.ts',
      'src/**/*.test.tsx',
      'config/**/*.test.ts',
      'tests/**/*.test.ts'
    ],
    testTimeout: 30_000,
    hookTimeout: 30_000
  }
});
