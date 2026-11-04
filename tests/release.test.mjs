import test from 'node:test';
import assert from 'node:assert/strict';
import { validateReadiness } from '../scripts/readiness.mjs';
const commit='a'.repeat(40);
const passing=()=>({version:'1.0.0',commit,windows:{status:'passed',report:'windows.trx',realServerTests:3,skipped:0},linux:{status:'passed',report:'linux.trx',realServerTests:3,skipped:0},clients:{status:'passed',report:'clients.json'},coverage:{status:'passed',report:'coverage.json'},mutation:{status:'passed',report:'mutations.json',score:90},stability:{status:'passed',report:'stability.json',durationMinutes:60,documentsPerWorkspace:1000},benchmark:{status:'passed',report:'benchmark.json',samplesPerWarmScenario:20}});
test('stable readiness accepts complete commit-bound evidence',()=>assert.doesNotThrow(()=>validateReadiness(passing(),'1.0.0',commit)));
test('stable readiness rejects stale, missing, skipped and below-threshold evidence',()=>{
 const changes=[e=>{e.commit='wrong';},e=>{e.version='wrong';},e=>{e.mutation.status='stopped';},e=>{e.mutation.score=89.99;},e=>{e.windows.skipped=1;},e=>{e.linux.realServerTests=0;},e=>{e.stability.durationMinutes=59;},e=>{e.stability.documentsPerWorkspace=999;},e=>{e.benchmark.samplesPerWarmScenario=19;},e=>{e.clients.report=null;}];
 for(const change of changes){const evidence=passing();change(evidence);assert.throws(()=>validateReadiness(evidence,'1.0.0',commit));}
});
