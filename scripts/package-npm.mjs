import { mkdtemp, mkdir, copyFile, writeFile, readFile, chmod } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { version } from './version.mjs';
const root = fileURLToPath(new URL('../', import.meta.url));
const options = new Map();
for (let i=2;i<process.argv.length;i++) {
  const key=process.argv[i];
  if (key==='--allow-partial') options.set(key,true);
  else { if (!['--windows','--linux','--output'].includes(key) || !process.argv[i+1] || options.has(key)) throw new Error('Invalid/duplicate packaging option'); options.set(key,process.argv[++i]); }
}
if ((!options.has('--windows') || !options.has('--linux')) && !options.has('--allow-partial')) throw new Error('Both native binaries are required for a publishable package');
if (!options.has('--windows') && !options.has('--linux')) throw new Error('No native binaries supplied');
const output = path.resolve(options.get('--output') || path.join(root,'artifacts/npm'));
await mkdir(output,{recursive:true});
const stage=await mkdtemp(path.join(output,'stage-'));
await mkdir(path.join(stage,'bin'));
const template=JSON.parse(await readFile(path.join(root,'packaging/npm/package.json'),'utf8'));
template.version=version(); template.private=false;
await writeFile(path.join(stage,'package.json'),JSON.stringify(template,null,2)+'\n');
await copyFile(path.join(root,'packaging/npm/bin/sharper.cjs'),path.join(stage,'bin/sharper.cjs'));
await chmod(path.join(stage,'bin/sharper.cjs'),0o755);
for (const [key,platform,name] of [['--windows','win-x64','sharper.exe'],['--linux','linux-x64','sharper']]) {
 if (!options.has(key)) continue;
 const target=path.join(stage,'native',platform); await mkdir(target,{recursive:true});
 await copyFile(path.resolve(options.get(key)),path.join(target,name)); await chmod(path.join(target,name),0o755);
}
await copyFile(path.join(root,'README.md'),path.join(stage,'README.md'));
await copyFile(path.join(root,'LICENSE'),path.join(stage,'LICENSE'));
const npmCli=process.env.npm_execpath || (process.platform==='win32' ? path.join(path.dirname(process.execPath),'node_modules/npm/bin/npm-cli.js') : null);
const packed=spawnSync(npmCli ? process.execPath : 'npm',[...(npmCli ? [npmCli] : []),'pack','--ignore-scripts','--json','--pack-destination',output],{cwd:stage,encoding:'utf8',windowsHide:true});
if(packed.status!==0)throw new Error(packed.stderr||packed.error?.message||'npm pack failed');
const file=JSON.parse(packed.stdout)[0].filename;
console.log(path.join(output,file));
