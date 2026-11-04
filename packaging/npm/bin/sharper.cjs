#!/usr/bin/env node
'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

function binaryPath(platform = process.platform, arch = process.arch, root = path.join(__dirname, '..')) {
  if (arch !== 'x64' || !['win32', 'linux'].includes(platform)) {
    throw new Error(`Sharper supports Windows x64 and Linux x64 (glibc); this host is ${platform}/${arch}.`);
  }
  return path.join(root, 'native', platform === 'win32' ? 'win-x64' : 'linux-x64', platform === 'win32' ? 'sharper.exe' : 'sharper');
}

function main(args) {
  try {
    const executable = binaryPath();
    if (process.platform === 'linux' && !process.report.getReport().header.glibcVersionRuntime) {
      throw new Error('Sharper requires glibc on Linux; musl/Alpine is not supported.');
    }
    if (!fs.existsSync(executable)) {
      throw new Error('The Sharper native executable is missing. Reinstall @adrian13579/sharper for this platform.');
    }
    // No shell, argument rewriting, downloads or protocol output from the launcher.
    const result = spawnSync(executable, args, { stdio: 'inherit', windowsHide: true });
    if (result.error) throw result.error;
    if (result.signal) {
      process.kill(process.pid, result.signal);
      return;
    }
    process.exitCode = result.status ?? 1;
  } catch (error) {
    process.stderr.write(`sharper: ${error.message}\n`);
    process.exitCode = 1;
  }
}

module.exports = { binaryPath, main };
if (require.main === module) main(process.argv.slice(2));
