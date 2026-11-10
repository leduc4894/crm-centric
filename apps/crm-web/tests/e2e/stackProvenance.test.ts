import { describe, expect, it } from 'vitest';
import {
  parseContainerList,
  resolveStackProject,
  selectStackApiContainer
} from './stackProvenance.ts';

const PROJECT = 'crm-local-91859983f1e0';

describe('resolveStackProject', () => {
  it('uses the explicit project of the environment', () => {
    expect(resolveStackProject('/srv/crm', { LOCAL_STACK_PROJECT: 'custom-project' })).toBe(
      'custom-project'
    );
  });

  it('derives the default project from the checkout path', () => {
    expect(resolveStackProject('/srv/crm', {})).toBe('crm-local-f8dc5829e518');
  });
});

describe('selectStackApiContainer', () => {
  it('rejects a foreign container that publishes the API port', () => {
    const containers = parseContainerList(
      'crm-foreign-impostor\t127.0.0.1:5080->5432/tcp\n'
    );

    expect(selectStackApiContainer(containers, PROJECT)).toBeNull();
  });

  it('rejects the API container of another compose project', () => {
    const containers = parseContainerList(
      'other-project-crm-api-1\t127.0.0.1:5080->8080/tcp\n'
    );

    expect(selectStackApiContainer(containers, PROJECT)).toBeNull();
  });

  it('rejects a foreign container even when the stack container is listed as well', () => {
    const containers = parseContainerList(
      'crm-foreign-impostor\t127.0.0.1:5080->5432/tcp\n' +
        `crm-local-91859983f1e0-crm-api-1\t127.0.0.1:5080->8080/tcp\n`
    );

    expect(selectStackApiContainer(containers, PROJECT)?.name).toBe(
      'crm-local-91859983f1e0-crm-api-1'
    );
  });

  it('selects the API container of the stack', () => {
    const containers = parseContainerList(
      `crm-local-91859983f1e0-crm-api-1\t127.0.0.1:5080->8080/tcp\n` +
        'crm-local-91859983f1e0-crm-db-1\t5432/tcp\n'
    );

    expect(selectStackApiContainer(containers, PROJECT)).toEqual({
      name: 'crm-local-91859983f1e0-crm-api-1',
      ports: '127.0.0.1:5080->8080/tcp'
    });
  });

  it('rejects an empty container list', () => {
    expect(selectStackApiContainer([], PROJECT)).toBeNull();
  });
});
