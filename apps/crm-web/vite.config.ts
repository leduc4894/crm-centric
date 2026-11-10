import { defineConfig } from 'vite';
import { createWebConfig } from './config/devServer.ts';

export default defineConfig(() => createWebConfig());
