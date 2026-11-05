import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';
import { mkdir, mkdtemp, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
const root=fileURLToPath(new URL('../../',import.meta.url));
await mkdir(path.join(root,'artifacts'),{recursive:true});
const directory=await mkdtemp(path.join(root,'artifacts/mcp-sdk-'));
const workspace=path.join(directory,'work');await mkdir(workspace);await writeFile(path.join(workspace,'example.orbit'),'violation');
const profile=path.join(directory,'lsp.json');
const app=process.platform==='win32'?'sharper.exe':'sharper';
const fixture=path.join(root,'target/debug/examples',process.platform==='win32'?'fake-lsp.exe':'fake-lsp');
await writeFile(profile,JSON.stringify({servers:[{id:'unlisted-language',command:fixture,arguments:['--mode','normal'],languages:{'.orbit':'planet'},readyNotification:'test/ready'}]}));
const client=new Client({name:'sharper-sdk-qualification',version:'1'},{capabilities:{}});
try {
 await client.connect(new StdioClientTransport({command:process.env.SHARPER_TEST_HOST||path.join(root,'target/debug',app),args:['mcp','--lsp-config',profile],stderr:'pipe'}));
 const {tools}=await client.listTools();assert.deepEqual(tools.map(tool=>tool.name),['sharper_check','sharper_status']);
 const check=await client.callTool({name:'sharper_check',arguments:{workspace,path:'example.orbit'}});
 assert.equal(check.isError,false);assert.equal(check.structuredContent.complete,true);assert.equal(check.structuredContent.diagnostics[0].code,'RULE001');
 const status=await client.callTool({name:'sharper_status',arguments:{workspace}});
 assert.equal(status.isError,false);assert.equal(status.structuredContent.available,true);assert.equal('navigationCapabilities' in status.structuredContent,false);
 console.log('Official MCP SDK client: Rust handshake, two tools, structured feedback and language-server health passed.');
} finally { await client.close(); }
