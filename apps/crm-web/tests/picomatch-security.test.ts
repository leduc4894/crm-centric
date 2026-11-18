import { spawnSync } from 'node:child_process';
import { describe, expect, it } from 'vitest';

describe('picomatch repeated wildcard backtracking', () => {
  for (const pattern of ['+(*)', '+(*a)', '*(*a)', '+(**a)', '+(*{a,b})',
    '+(*a|b)', '+(**a|b)', '+(a*|b*)', '+(a|**a)', '+(*a|z*)',
    '+(a|?(a))', '+({a,aa})', '+(@(a|aa))', '+(ab|abab)']) {
    it(`bounds matching ${pattern}`, () => {
      const result = spawnSync(process.execPath, ['-e',
        'require("picomatch")(process.argv[1])((process.argv[1] === "+(ab|abab)" ? "ab" : "a").repeat(30)+"/")', pattern],
      { encoding: 'utf8', timeout: 2000 });
      expect(result.error, `pattern ${pattern} timed out`).toBeUndefined();
      expect(result.status).toBe(0);
    });
  }

  it('keeps the safeguard enabled when legacy recursion checks are disabled', () => {
    const result = spawnSync(process.execPath, ['-e',
      'require("picomatch")("+(*a|b)", {maxExtglobRecursion:false})("a".repeat(30)+"/")'],
    { encoding: 'utf8', timeout: 2000 });
    expect(result.error).toBeUndefined();
    expect(result.status).toBe(0);
  });
});
