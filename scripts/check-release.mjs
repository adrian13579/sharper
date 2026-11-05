import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { version } from './version.mjs';
import { validateReadiness } from './readiness.mjs';
const current=version();const tag=process.env.RELEASE_TAG || process.argv[2];
if(tag!==`v${current}`)throw new Error('Release tag must match Cargo.toml');
const changelog=readFileSync('website/content/docs/changelog.md','utf8');
if(!changelog.split(/\r?\n/).some(line=>line.trim()===`## ${current}`))throw new Error('Version entry missing from website changelog');
if(!current.includes('-')) {
 if(!process.env.READINESS_PATH)throw new Error('Stable releases require an evidence file supplied through READINESS_PATH');
 const evidence=JSON.parse(readFileSync(process.env.READINESS_PATH,'utf8'));
 const commit=execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim();
 validateReadiness(evidence,current,commit);
}
console.log(`Release metadata validated: ${tag} (${current.includes('-')?'prerelease':'stable'})`);
