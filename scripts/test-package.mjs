import { spawn, spawnSync } from 'node:child_process';
import { mkdir, writeFile, mkdtemp } from 'node:fs/promises';
import { createInterface } from 'node:readline';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { version } from './version.mjs';
const root=fileURLToPath(new URL('../',import.meta.url));
await mkdir(path.join(root,'artifacts'),{recursive:true});
const install=await mkdtemp(path.join(root,'artifacts/package-test-'));
const tarball=path.join(root,'artifacts/npm',`adrian13579-sharper-${version()}.tgz`);
const npmCli=process.env.npm_execpath || (process.platform==='win32'?path.join(path.dirname(process.execPath),'node_modules/npm/bin/npm-cli.js'):null);
const installed=spawnSync(npmCli?process.execPath:'npm',[...(npmCli?[npmCli]:[]),'install','--prefix',install,'--ignore-scripts','--no-audit','--no-fund',tarball],{encoding:'utf8',windowsHide:true});
if(installed.status!==0)throw new Error(installed.stderr||installed.error?.message||'npm install failed');
const launcher=path.join(install,'node_modules/@adrian13579/sharper/bin/sharper.cjs');
const check=spawnSync(process.execPath,[launcher,'--version'],{encoding:'utf8',windowsHide:true});
if(check.status!==0 || !check.stdout.includes(version()))throw new Error(`Installed version failed: ${check.stderr}`);
const workspace=path.join(install,'work');await mkdir(workspace);await writeFile(path.join(workspace,'sample.orbit'),'violation');
const profile=path.join(install,'lsp.json');
const fake=path.join(root,'target/debug/examples',process.platform==='win32'?'fake-lsp.exe':'fake-lsp');
await writeFile(profile,JSON.stringify({command:fake,arguments:['--mode','normal'],languages:{'.orbit':'planet'},readyNotification:'test/ready'}));
const child=spawn(process.execPath,[launcher,'mcp','--lsp-config',profile],{stdio:['pipe','pipe','pipe'],windowsHide:true});
const waiting=new Map();let sequence=0;let stderr='';
child.stderr.on('data',chunk=>{stderr=(stderr+chunk).slice(-8192);});
const lines=createInterface({input:child.stdout});
lines.on('line',line=>{
 let value;try{value=JSON.parse(line);}catch{child.kill();throw new Error('Non-protocol MCP stdout');}
 const pending=waiting.get(value.id);if(pending){waiting.delete(value.id);clearTimeout(pending.timer);pending.resolve(value);}
});
child.on('error',error=>{for(const pending of waiting.values())pending.reject(error);});
const request=(method,params)=>new Promise((resolve,reject)=>{const id=++sequence;const timer=setTimeout(()=>{child.kill();reject(new Error(`MCP timeout: ${stderr}`));},20000);waiting.set(id,{resolve,reject,timer});child.stdin.write(JSON.stringify({jsonrpc:'2.0',id,method,params})+'\n');});
try {
 const initialized=await request('initialize',{protocolVersion:'2025-11-25',capabilities:{},clientInfo:{name:'package-smoke',version:'1'}});
 if(initialized.result?.serverInfo?.version!==version())throw new Error('MCP version mismatch');
 child.stdin.write(JSON.stringify({jsonrpc:'2.0',method:'notifications/initialized'})+'\n');
 const result=await request('tools/call',{name:'sharper_check',arguments:{workspace,path:'sample.orbit'}});
 if(result.result?.structuredContent?.complete!==true || result.result?.structuredContent?.diagnosticCount!==1)throw new Error(`Installed MCP check failed: ${JSON.stringify(result)}`);
 child.stdin.end();
 await new Promise((resolve,reject)=>{const timer=setTimeout(()=>{child.kill();reject(new Error('Installed server did not stop'));},10000);child.on('close',code=>{clearTimeout(timer);code===0?resolve():reject(new Error(`Installed server exited ${code}: ${stderr}`));});});
 console.log('Installed npm Rust executable: version, MCP handshake, arbitrary-language feedback and shutdown passed.');
} finally { lines.close();if(child.exitCode===null)child.kill();for(const pending of waiting.values())clearTimeout(pending.timer); }
