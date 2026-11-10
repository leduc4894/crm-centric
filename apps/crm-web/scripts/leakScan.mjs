import { createServer as createHttpServer } from 'node:http';
import { readFile, readdir, rm } from 'node:fs/promises';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';
import { build, createServer } from 'vite';

/**
 * Leak scan of the built bundle, the source maps, the browser output and the process logs.
 *
 * The scan drives the real application against a stub API that answers with synthetic sentinel
 * values. The sentinels must not appear in the build artifacts, in the browser console, in the
 * rendered document or in the process output of the development server. A finding is reported by
 * name only; the payload is never printed.
 */

const applicationRoot = fileURLToPath(new URL('..', import.meta.url));
const repositoryRoot = fileURLToPath(new URL('../..', import.meta.url));

const SENTINELS = {
  syntheticToken: 'SENTINEL_TOKEN_2f8a41c7',
  syntheticEmail: 'sentinel.operator@example.invalid',
  syntheticLocalPath: '/srv/.local/stack/sentinel',
  syntheticStack: 'SENTINEL_STACK_5b1e9f02'
};

const MARKERS = [
  'process.env.LOCAL',
  'LOCAL_API_PORT',
  'LOCAL_API_HOST',
  'POSTGRES_PASSWORD',
  'PRIVATE KEY',
  'Bearer eyJ',
  '.local/stack'
];

const TEXT_EXTENSIONS = ['.js', '.mjs', '.cjs', '.ts', '.tsx', '.css', '.html', '.json', '.map'];

const findings = [];

function scanText(text, label) {
  for (const [name, value] of Object.entries(SENTINELS)) {
    if (text.includes(value)) {
      findings.push({ label, marker: name });
    }
  }
  for (const marker of MARKERS) {
    if (text.includes(marker)) {
      findings.push({ label, marker });
    }
  }
  if (text.includes(repositoryRoot)) {
    findings.push({ label, marker: 'absolute-checkout-path' });
  }
}

async function scanDirectory(directory) {
  let scanned = 0;
  let entries;
  try {
    entries = await readdir(directory, { withFileTypes: true, recursive: true });
  } catch (error) {
    if (error.code === 'ENOENT') {
      return 0;
    }
    throw error;
  }
  for (const entry of entries) {
    if (!entry.isFile()) {
      continue;
    }
    const path = join(entry.parentPath ?? directory, entry.name);
    if (!TEXT_EXTENSIONS.some((extension) => entry.name.endsWith(extension))) {
      continue;
    }
    const content = await readFile(path, 'utf8');
    scanText(content, relative(directory, path));
    scanned += 1;
  }
  return scanned;
}

function startStubApi() {
  const server = createHttpServer((request, response) => {
    if (request.url === '/health/ready') {
      response.writeHead(503, {
        'content-type': 'application/problem+json',
        'x-correlation-id': '0HNE5Q1J2K3L4',
        'cache-control': 'no-store'
      });
      response.end(
        JSON.stringify({
          type: 'urn:crm:problem:database-unavailable',
          title: `Dependency unavailable ${SENTINELS.syntheticToken}`,
          status: 503,
          reasonToken: 'DATABASE_UNAVAILABLE',
          correlationId: '0HNE5Q1J2K3L4',
          detail: `${SENTINELS.syntheticEmail} ${SENTINELS.syntheticLocalPath} ${SENTINELS.syntheticStack}`
        })
      );
      return;
    }
    response.writeHead(404, { 'content-type': 'application/json' });
    response.end('{}');
  });
  return new Promise((resolve) => {
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      resolve({ server, port: address.port });
    });
  });
}

function captureProcessOutput() {
  const captured = [];
  const originalOut = process.stdout.write.bind(process.stdout);
  const originalErr = process.stderr.write.bind(process.stderr);
  process.stdout.write = (chunk, ...rest) => {
    captured.push(String(chunk));
    return originalOut(chunk, ...rest);
  };
  process.stderr.write = (chunk, ...rest) => {
    captured.push(String(chunk));
    return originalErr(chunk, ...rest);
  };
  return {
    captured,
    restore() {
      process.stdout.write = originalOut;
      process.stderr.write = originalErr;
    }
  };
}

async function scanBrowserAndProcess() {
  const stub = await startStubApi();
  process.env['LOCAL_API_PORT'] = String(stub.port);

  const output = captureProcessOutput();
  const server = await createServer({
    configFile: join(applicationRoot, 'vite.config.ts'),
    logLevel: 'warn',
    server: { host: '127.0.0.1', port: 0, strictPort: false }
  });
  await server.listen();

  const address = server.httpServer?.address();
  if (address === null || address === undefined || typeof address === 'string') {
    throw new Error('the scan dev server did not bind to a TCP port');
  }
  const origin = `http://127.0.0.1:${address.port}`;

  const consoleMessages = [];
  let browser = null;
  try {
    browser = await chromium.launch();
    const page = await browser.newPage();
    page.on('console', (message) => {
      consoleMessages.push(message.text());
    });
    page.on('pageerror', (error) => {
      consoleMessages.push(error.message);
    });
    await page.goto(`${origin}/`, { waitUntil: 'networkidle' });
    const documentText = await page.locator('body').innerText();
    scanText(documentText, 'browser:document');
    for (const message of consoleMessages) {
      scanText(message, 'browser:console');
    }
  } finally {
    await browser?.close();
    await server.close();
    await new Promise((resolve) => {
      stub.server.close(() => resolve());
    });
    output.restore();
  }

  scanText(output.captured.join('\n'), 'process:log');
  return { consoleMessages: consoleMessages.length };
}

async function scanBuildArtifacts() {
  const output = join(applicationRoot, '.leak-scan-build');
  await rm(output, { recursive: true, force: true });
  try {
    await build({
      root: applicationRoot,
      configFile: join(applicationRoot, 'vite.config.ts'),
      logLevel: 'warn',
      build: { outDir: output, sourcemap: true, emptyOutDir: true }
    });
    return await scanDirectory(output);
  } finally {
    await rm(output, { recursive: true, force: true });
  }
}

const scannedFiles = await scanBuildArtifacts();
const shippedFiles = await scanDirectory(join(applicationRoot, 'dist'));
const browserSummary = await scanBrowserAndProcess();

if (findings.length > 0) {
  console.log(
    `leak scan: ${findings.length} finding(s) in ${scannedFiles + shippedFiles} scanned artifact file(s)`
  );
  for (const finding of findings) {
    console.log(`  - ${finding.label}: ${finding.marker}`);
  }
  process.exit(1);
}

console.log(
  `leak scan: no sentinel, secret, credential or local metadata leak in ${scannedFiles + shippedFiles} ` +
    `artifact file(s), ${browserSummary.consoleMessages} browser console message(s) or the process log`
);
