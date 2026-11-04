import { readFileSync } from 'node:fs';
export function version() {
  const value = readFileSync(new URL('../Cargo.toml', import.meta.url), 'utf8').match(/^version\s*=\s*"([^"]+)"\s*$/m)?.[1];
  if (!value || !/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(value)) throw new Error('Invalid Cargo package version');
  return value;
}
