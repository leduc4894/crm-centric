import { spawnSync } from 'node:child_process';
import { describe, expect, it } from 'vitest';

describe('picomatch compatibility', () => {
  it('matches repeated extglobs using upstream semantics', () => {
    const result = spawnSync(process.execPath, ['-e', `const p = require('picomatch');
      if (!p.isMatch('foobar', '+(foo|foobar)') || !p.isMatch('abc', '+(*)')) process.exit(1);`],
    { encoding: 'utf8', timeout: 2000 });
    expect(result.error).toBeUndefined();
    expect(result.status).toBe(0);
  });
});
