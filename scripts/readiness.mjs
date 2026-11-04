export function validateReadiness(evidence, version, commit) {
 if(evidence.version!==version || evidence.commit!==commit)throw new Error('Readiness evidence must match version and commit');
 for(const key of ['windows','linux','clients','stability','benchmark','coverage','mutation']) {
  if(evidence[key]?.status!=='passed' || typeof evidence[key]?.report!=='string' || !evidence[key].report.trim())throw new Error(`Missing readiness evidence: ${key}`);
 }
 for(const key of ['windows','linux'])if(!(evidence[key].realServerTests>0) || evidence[key].skipped!==0)throw new Error(`Real-server tests must run without skips: ${key}`);
 if(!(evidence.mutation.score>=90))throw new Error('Mutation score must be at least 90%');
 if(!(evidence.stability.durationMinutes>=60) || !(evidence.stability.documentsPerWorkspace>=1000))throw new Error('Stability needs at least 60 minutes and 1000 documents/workspace');
 if(!(evidence.benchmark.samplesPerWarmScenario>=20))throw new Error('Benchmark needs at least 20 warm samples/scenario');
}
