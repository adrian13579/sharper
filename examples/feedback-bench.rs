use anyhow::{Context, Result, ensure};
use serde_json::{Value, json};
use sharper::{
    config::Config,
    engine::{Args, Engine},
};
use std::{
    path::PathBuf,
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};
#[tokio::main(worker_threads = 2)]
async fn main() -> Result<()> {
    let args: Vec<_> = std::env::args().skip(1).collect();
    ensure!(
        args.len() >= 2,
        "Usage: feedback-bench PROFILE ABSOLUTE_WORKSPACE [PATH] [WARM_SAMPLES]"
    );
    let config = Config::load(&PathBuf::from(&args[0]))?;
    let workspace = args[1].clone();
    let path = args.get(2).cloned();
    let count = args
        .get(3)
        .map(|v| v.parse::<usize>())
        .transpose()?
        .unwrap_or(20);
    ensure!(
        count > 0 && count <= 1000,
        "Sample count must be between 1 and 1000"
    );
    let parameters = Args {
        workspace: Some(workspace.clone()),
        path,
        ..Default::default()
    };
    let mut engine = Engine::new(Some(config.clone()), None);
    let start = Instant::now();
    let cold = tokio::time::timeout(
        Duration::from_secs(120),
        engine.call("sharper_check", parameters.clone()),
    )
    .await
    .context("Cold sample timed out")??;
    let cold = json!({"latencyMs":start.elapsed().as_secs_f64()*1000.0,"status":cold["status"],"complete":cold["complete"],"diagnosticCount":cold["diagnosticCount"]});
    let mut warm = Vec::new();
    let mut times = Vec::new();
    for _ in 0..count {
        let start = Instant::now();
        let result = tokio::time::timeout(
            Duration::from_secs(120),
            engine.call("sharper_check", parameters.clone()),
        )
        .await
        .context("Warm sample timed out")??;
        let elapsed = start.elapsed().as_secs_f64() * 1000.0;
        times.push(elapsed);
        warm.push(json!({"latencyMs":elapsed,"status":result["status"],"complete":result["complete"],"diagnosticCount":result["diagnosticCount"]}));
    }
    engine.close().await;
    times.sort_by(f64::total_cmp);
    let report: Value = json!({"version":env!("CARGO_PKG_VERSION"),"timestampUnix":SystemTime::now().duration_since(UNIX_EPOCH)?.as_secs(),"os":std::env::consts::OS,"arch":std::env::consts::ARCH,"hardwareContext":std::env::var("SHARPER_BENCH_CONTEXT").unwrap_or_else(|_|"not recorded".into()),"workspace":workspace,"coldSampleCount":1,"cold":cold,"warmSampleCount":count,"warmMedianMs":if count.is_multiple_of(2){(times[count/2-1]+times[count/2])/2.0}else{times[count/2]},"warmP95Ms":times[((count as f64*0.95).ceil()as usize).saturating_sub(1)],"warm":warm,"limitations":"One cold sample is not a cold latency distribution. Results are profile/workspace specific and do not establish production readiness."});
    println!("{}", serde_json::to_string_pretty(&report)?);
    Ok(())
}
