'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const path = require('node:path');
const { binaryPath } = require('../bin/sharper.cjs');

test('selects Windows native executable under the package root', () => {
  assert.equal(binaryPath('win32', 'x64', '/package with spaces'), path.join('/package with spaces', 'native/win-x64/sharper.exe'));
});
test('selects Linux native executable under the package root', () => {
  assert.equal(binaryPath('linux', 'x64', '/package'), path.join('/package', 'native/linux-x64/sharper'));
});
test('unsupported operating systems and architectures fail explicitly', () => {
  for (const [os, arch] of [['darwin','arm64'], ['linux','arm64'], ['win32','ia32']]) {
    assert.throws(() => binaryPath(os, arch), /Sharper supports Windows x64 and Linux x64/);
  }
});
test('missing binary produces an error on stderr and keeps MCP stdout clean', () => {
  const result = spawnSync(process.execPath, [path.join(__dirname, '../bin/sharper.cjs'), '--version'], { encoding: 'utf8' });
  assert.equal(result.status, 1);
  assert.equal(result.stdout, '');
  assert.match(result.stderr, /native executable is missing/);
});
