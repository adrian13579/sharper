import { readFileSync, existsSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { version } from './version.mjs';
import { validateReadiness } from './readiness.mjs';
const current=version();const tag=process.env.RELEASE_TAG || process.argv[2];
if(tag!==`v${current}`)throw new Error('Release tag must match Cargo.toml');
if(!existsSync(`docs/release-notes/${tag}.md`))throw new Error('Release notes missing');
if(!current.includes('-')) {
 const evidence=JSON.parse(readFileSync(process.env.READINESS_PATH || 'maintenance/release-readiness.json','utf8'));
 const commit=execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim();
 validateReadiness(evidence,current,commit);
}
console.log(`Release metadata validated: ${tag} (${current.includes('-')?'prerelease':'stable'})`);
