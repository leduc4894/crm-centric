import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, realpathSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * The compose service name of the API in `compose.yaml`.
 */
const API_SERVICE = 'crm-api';

export interface StackApiContainer {
  readonly name: string;
  readonly ports: string;
}

/**
 * Resolves the checkout root the same way the local stack resolves it.
 *
 * `scripts/local/compose.py` uses the resolved repository root, so the project name derived from
 * it has to be derived from the resolved path as well. The root is the closest ancestor that
 * holds both the compose file and the workspace manifest.
 */
export function repositoryRoot(): string {
  let directory = dirname(fileURLToPath(import.meta.url));
  for (;;) {
    if (
      existsSync(join(directory, 'compose.yaml')) &&
      existsSync(join(directory, 'package.json'))
    ) {
      return realpathSync(directory);
    }
    const parent = dirname(directory);
    if (parent === directory) {
      throw new Error('the checkout root of the local stack could not be resolved');
    }
    directory = parent;
  }
}

/**
 * Resolves the compose project name of the local stack.
 *
 * The name is the explicit `LOCAL_STACK_PROJECT` when it is set and otherwise the default the
 * local stack computes from the checkout path.
 */
export function resolveStackProject(root: string, environment: NodeJS.ProcessEnv = process.env): string {
  const explicit = environment['LOCAL_STACK_PROJECT'];
  if (explicit !== undefined && explicit !== '') {
    return explicit;
  }
  return `crm-local-${createHash('sha256').update(root).digest('hex').slice(0, 12)}`;
}

/**
 * Parses the `{{.Names}}\t{{.Ports}}` output of `docker ps`.
 */
export function parseContainerList(output: string): StackApiContainer[] {
  const containers: StackApiContainer[] = [];
  for (const line of output.split('\n')) {
    const trimmed = line.trim();
    if (trimmed === '') {
      continue;
    }
    const separator = trimmed.indexOf('\t');
    const name = separator === -1 ? trimmed : trimmed.slice(0, separator);
    const ports = separator === -1 ? '' : trimmed.slice(separator + 1);
    containers.push({ name, ports });
  }
  return containers;
}

/**
 * Selects the API container of the given compose project.
 *
 * A container that only publishes the API port is not the stack: the container of the stack is
 * named `<project>-<service>-<index>` by compose. A foreign container that publishes the same
 * port therefore never satisfies this check.
 */
export function selectStackApiContainer(
  containers: readonly StackApiContainer[],
  project: string
): StackApiContainer | null {
  return containers.find((container) => container.name.startsWith(`${project}-${API_SERVICE}-`)) ?? null;
}

/**
 * Reads the API container of the local stack from the Docker daemon.
 *
 * The query is filtered by the compose labels of the stack, so only the containers of the project
 * are considered; the selection then requires the compose container name of the API service.
 */
export function readStackApiContainer(project: string): StackApiContainer | null {
  let output: string;
  try {
    output = execFileSync(
      'docker',
      [
        'ps',
        '--filter',
        `label=com.docker.compose.project=${project}`,
        '--filter',
        `label=com.docker.compose.service=${API_SERVICE}`,
        '--format',
        '{{.Names}}\t{{.Ports}}'
      ],
      { encoding: 'utf8' }
    );
  } catch (error) {
    throw new Error('the local stack must be running in Docker; the docker command failed', {
      cause: error
    });
  }
  return selectStackApiContainer(parseContainerList(output), project);
}
