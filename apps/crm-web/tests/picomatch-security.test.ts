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

  it('literalizes ambiguous patterns and preserves supported glob matching', () => {
    const code = `const p = require('picomatch');
      for (const options of [{}, { maxExtglobRecursion: false }]) {
        if (!p.isMatch('+(*)', '+(*)', options) || p.isMatch('abc', '+(*)', options) ||
            !p.isMatch('+(foo|foobar)', '+(foo|foobar)', options) ||
            p.isMatch('foobar', '+(foo|foobar)', options)) process.exit(1);
      }
      if (!p.isMatch('foobar', '+(foo|bar)') ||
          !p.isMatch('src/app/file.ts', 'src/**/*.ts') ||
          p.isMatch('src/app/file.js', 'src/**/*.ts')) process.exit(1);`;
    const result = spawnSync(process.execPath, ['-e', code], { encoding: 'utf8', timeout: 2000 });
    expect(result.error).toBeUndefined();
    expect(result.status).toBe(0);
  });
});
