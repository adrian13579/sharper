use anyhow::Result;
use serde_json::{Value, json};
use sharper::{
    config::Config,
    engine::{Args, Engine},
    framing::{read_lsp, write_lsp},
    normalize, setup,
};
use std::path::{Path, PathBuf};
use tempfile::TempDir;
use tokio::io::BufReader;
fn fixture() -> PathBuf {
    let path = std::env::current_exe().unwrap();
    path.parent()
        .unwrap()
        .parent()
        .unwrap()
        .join("examples")
        .join(if cfg!(windows) {
            "fake-lsp.exe"
        } else {
            "fake-lsp"
        })
}
struct World {
    _temp: TempDir,
    root: PathBuf,
    config: PathBuf,
    audit: PathBuf,
}
impl World {
    fn new(mode: &str, count: usize) -> Self {
        let temp = tempfile::tempdir().unwrap();
        let root = temp.path().join("work");
        std::fs::create_dir(&root).unwrap();
        let root = sharper::workspace::clean(std::fs::canonicalize(root).unwrap());
        let audit = temp.path().join("audit.jsonl");
        let config = temp.path().join("servers.json");
        let servers:Vec<_>=(0..count).map(|i|json!({"id":format!("rules{i}"),"command":fixture(),"arguments":["--mode",mode,"--audit",audit,"space & $ literal"],"languages":{".orbit":"planet"},"initializationOptions":{"marker":"agnostic"},"settings":{"rules":{"enabled":true}},"readyNotification":"test/ready"})).collect();
        std::fs::write(
            &config,
            serde_json::to_vec(&json!({"servers":servers})).unwrap(),
        )
        .unwrap();
        std::fs::write(root.join("example.orbit"), "violation").unwrap();
        Self {
            _temp: temp,
            root,
            config,
            audit,
        }
    }
    fn engine(&self) -> Engine {
        Engine::new(Some(Config::load(&self.config).unwrap()), None)
    }
    fn args(&self) -> Args {
        Args {
            workspace: Some(self.root.to_string_lossy().into_owned()),
            path: Some("example.orbit".into()),
        }
    }
    fn messages(&self) -> Vec<Value> {
        std::fs::read_to_string(&self.audit)
            .unwrap()
            .lines()
            .map(|s| serde_json::from_str(s).unwrap())
            .collect()
    }
}
async fn stable(engine: &mut Engine, args: Args) -> Value {
    let mut value = Value::Null;
    for _ in 0..4 {
        value = engine.call("sharper_check", args.clone()).await.unwrap();
        if value["status"] != "incomplete" {
            break;
        }
    }
    value
}
fn git(root: &Path, args: &[&str]) -> Result<()> {
    let output = std::process::Command::new("git")
        .current_dir(root)
        .args([
            "-c",
            "user.name=Sharper tests",
            "-c",
            "user.email=tests@sharper.invalid",
            "-c",
            "commit.gpgsign=false",
            "-c",
        ])
        .arg(format!(
            "core.hooksPath={}",
            root.join(".git/no-hooks").display()
        ))
        .args(args)
        .env_remove("GIT_DIR")
        .env_remove("GIT_WORK_TREE")
        .env_remove("GIT_INDEX_FILE")
        .env_remove("GIT_COMMON_DIR")
        .output()?;
    assert!(
        output.status.success(),
        "git {args:?}: {}",
        String::from_utf8_lossy(&output.stderr)
    );
    Ok(())
}
#[tokio::test]
async fn linked_worktrees_keep_feedback_processes_and_working_directories_separate() -> Result<()> {
    let w = World::new("normal", 1);
    git(&w.root, &["init", "--quiet"])?;
    git(&w.root, &["add", "example.orbit"])?;
    git(
        &w.root,
        &["commit", "--quiet", "-m", "Create test checkout"],
    )?;
    let agent = w.root.parent().unwrap().join("agent checkout");
    git(
        &w.root,
        &[
            "worktree",
            "add",
            "--quiet",
            "-b",
            "agent",
            agent.to_str().unwrap(),
        ],
    )?;
    assert!(agent.join(".git").is_file());
    std::fs::write(agent.join("example.orbit"), "fixed")?;
    // The fixture reads its name from the checkout to prove its actual cwd.
    std::fs::write(w.root.join("server-name.txt"), "main checkout")?;
    std::fs::write(agent.join("server-name.txt"), "agent checkout")?;
    let config = Config::load(&w.config)?;
    let mut engine = Engine::new(Some(config), Some(w.root.clone()));
    let agent_args = Args {
        path: Some(agent.join("example.orbit").to_string_lossy().into_owned()),
        ..Default::default()
    };
    let main_result = stable(&mut engine, w.args()).await;
    let agent_result = stable(&mut engine, agent_args.clone()).await;
    assert_eq!(main_result["complete"], true, "{main_result}");
    assert_eq!(main_result["diagnosticCount"], 1);
    assert_eq!(agent_result["complete"], true, "{agent_result}");
    assert_eq!(agent_result["diagnosticCount"], 0);
    assert_eq!(
        agent_result["requestedPath"],
        json!(agent.join("example.orbit"))
    );
    let main_status = engine.call("sharper_status", w.args()).await?;
    let agent_status = engine.call("sharper_status", agent_args.clone()).await?;
    assert_eq!(main_status["workspace"], json!(w.root));
    assert_eq!(agent_status["workspace"], json!(agent));
    assert_ne!(main_status["processId"], agent_status["processId"]);
    assert_eq!(main_status["serverName"], "main checkout");
    assert_eq!(agent_status["serverName"], "agent checkout");
    std::fs::write(agent.join("example.orbit"), "violation")?;
    let changed = stable(&mut engine, agent_args.clone()).await;
    assert_eq!(changed["complete"], true, "{changed}");
    assert_eq!(
        changed["diagnostics"][0]["filePath"],
        json!(agent.join("example.orbit"))
    );
    std::fs::write(w.root.join("example.orbit"), "fixed")?;
    let changed = stable(&mut engine, w.args()).await;
    assert_eq!(changed["complete"], true, "{changed}");
    assert_eq!(changed["diagnosticCount"], 0);
    for (args, original) in [(w.args(), &main_status), (agent_args, &agent_status)] {
        let current = engine.call("sharper_status", args).await?;
        assert_eq!(current["processId"], original["processId"]);
    }
    engine.close().await;
    let messages = w.messages();
    let initializations: Vec<_> = messages
        .iter()
        .filter(|m| m["method"] == "initialize")
        .collect();
    assert_eq!(initializations.len(), 2);
    for root in [&w.root, &agent] {
        let uri = normalize::uri(root)?;
        let initialization = initializations
            .iter()
            .find(|m| m["params"]["rootUri"] == uri)
            .unwrap();
        assert_eq!(initialization["params"]["workspaceFolders"][0]["uri"], uri);
        for removed in ["definition", "implementation", "references"] {
            assert!(
                initialization["params"]["capabilities"]["textDocument"]
                    .get(removed)
                    .is_none()
            );
        }
        assert_eq!(
            initialization["params"]["workspaceFolders"]
                .as_array()
                .unwrap()
                .len(),
            1
        );
    }
    Ok(())
}
#[tokio::test]
async fn agnostic_feedback_preserves_metadata_and_reuses_warm_sessions() -> Result<()> {
    let w = World::new("handshake", 1);
    let mut engine = w.engine();
    let baseline = stable(&mut engine, w.args()).await;
    assert_eq!(baseline["complete"], true, "{baseline}");
    let d = &baseline["diagnostics"][0];
    assert_eq!(d["code"], "RULE001");
    assert_eq!(d["source"], "planet-enforcer");
    assert_eq!(d["severity"], "error");
    assert_eq!(d["filePath"], json!(w.root.join("example.orbit")));
    assert_eq!(d["data"]["opaque"][1], "kept");
    assert_eq!(d["tags"], json!([1, 2]));
    assert_eq!(d["relatedInformation"][0]["startColumn"], 0);
    let before = engine.call("sharper_status", w.args()).await?;
    std::fs::write(w.root.join("example.orbit"), "fixed")?;
    let fixed = stable(&mut engine, w.args()).await;
    assert_eq!(fixed["complete"], true, "{fixed}");
    assert_eq!(fixed["diagnosticCount"], 0);
    let after = engine.call("sharper_status", w.args()).await?;
    assert_eq!(before["processId"], after["processId"]);
    engine.close().await;
    let messages = w.messages();
    assert!(
        messages
            .iter()
            .any(|m| m["method"] == "workspace/didChangeConfiguration")
    );
    assert!(
        messages
            .iter()
            .any(|m| m["method"] == "textDocument/didChange")
    );
    assert_eq!(
        messages
            .iter()
            .filter(|m| m["method"] == "initialize")
            .count(),
        1
    );
    Ok(())
}
#[tokio::test]
async fn multiple_servers_contribute_without_erasing_provenance() -> Result<()> {
    let w = World::new("normal", 2);
    let mut e = w.engine();
    let r = stable(&mut e, w.args()).await;
    assert_eq!(r["complete"], true, "{r}");
    assert_eq!(r["diagnosticCount"], 2);
    assert_eq!(r["backend"], "mixed");
    assert_eq!(r["backendResults"].as_array().unwrap().len(), 2);
    for backend in r["backendResults"].as_array().unwrap() {
        assert_eq!(backend["status"], "completed");
        assert_eq!(backend["diagnosticCount"], 1);
        assert!(backend.get("diagnostics").is_none());
        assert!(backend.get("limitations").is_none());
    }
    assert!(r.get("errors").is_none());
    assert_ne!(
        r["diagnostics"][0]["backend"],
        r["diagnostics"][1]["backend"]
    );
    let status = e.call("sharper_status", w.args()).await?;
    assert_eq!(status["available"], true);
    assert!(status.get("navigationCapabilities").is_none());
    assert_eq!(status["sessions"].as_array().unwrap().len(), 2);
    for session in status["sessions"].as_array().unwrap() {
        assert_eq!(session["available"], true);
        assert!(session["processId"].is_number());
        for omitted in [
            "limitations",
            "workspace",
            "solution",
            "diagnosticProviders",
            "lastError",
            "navigationCapabilities",
        ] {
            assert!(session.get(omitted).is_none());
        }
    }
    e.close().await;
    Ok(())
}
#[tokio::test]
async fn native_names_are_not_required_for_configured_languages() -> Result<()> {
    let w = World::new("normal", 1);
    let mut e = w.engine();
    let r = e.call("sharper_check", w.args()).await?;
    assert_eq!(r["complete"], true, "{r}");
    assert!(r.get("limitations").is_none());
    assert!(r.get("backendResults").is_none());
    assert!(r.get("errors").is_none());
    assert_eq!(r["freshness"], "verified");
    assert_eq!(r["diagnostics"][0]["code"], "RULE001");
    e.close().await;
    Ok(())
}
#[tokio::test]
async fn failed_servers_prevent_combined_verification() -> Result<()> {
    let w = World::new("normal", 2);
    let mut cfg: Value = serde_json::from_slice(&std::fs::read(&w.config)?)?;
    cfg["servers"][1]["command"] = json!("this-server-does-not-exist-29301");
    std::fs::write(&w.config, cfg.to_string())?;
    let mut e = w.engine();
    let r = e.call("sharper_check", w.args()).await?;
    assert_eq!(r["complete"], false);
    assert_eq!(r["freshness"], "uncertain");
    assert_eq!(r["diagnosticCount"], 1);
    assert!(!r["errors"].as_array().unwrap().is_empty());
    assert!(
        r["backendResults"]
            .as_array()
            .unwrap()
            .iter()
            .any(|server| server["status"] == "failed" && server["diagnosticCount"] == 0)
    );
    e.close().await;
    Ok(())
}
#[tokio::test]
async fn uncertain_or_malformed_results_do_not_verify() -> Result<()> {
    for (mode, status) in [
        ("push", "incomplete"),
        ("malformed", "failed"),
        ("bad-unchanged", "failed"),
        ("race", "incomplete"),
        ("refresh-always", "incomplete"),
        ("capability-race", "incomplete"),
        ("crash", "failed"),
        ("no-sync", "failed"),
    ] {
        let w = World::new(mode, 1);
        let mut e = w.engine();
        let r = e.call("sharper_check", w.args()).await?;
        assert_eq!(r["status"], status, "{mode}: {r}");
        assert_eq!(r["complete"], false);
        assert_eq!(r["freshness"], "uncertain");
        e.close().await;
    }
    Ok(())
}
#[tokio::test]
async fn initialization_encoding_and_process_failures_are_explicit() -> Result<()> {
    for mode in ["utf8", "crash-start"] {
        let w = World::new(mode, 1);
        let mut e = w.engine();
        let r = e.call("sharper_check", w.args()).await?;
        assert_eq!(r["complete"], false, "{r}");
        assert!(!r["errors"].as_array().unwrap().is_empty());
        e.close().await;
    }
    Ok(())
}
#[tokio::test]
async fn refresh_and_cached_unchanged_reports_remain_supported() -> Result<()> {
    for mode in [
        "refresh",
        "unchanged",
        "full",
        "unknown-severity",
        "dynamic",
    ] {
        let w = World::new(mode, 1);
        let mut e = w.engine();
        for _ in 0..2 {
            let r = stable(&mut e, w.args()).await;
            assert_eq!(r["complete"], true, "{mode}: {r}");
            assert_eq!(r["diagnosticCount"], 1);
            if mode == "unknown-severity" {
                assert_eq!(r["diagnostics"][0]["severity"], "9");
            }
        }
        e.close().await;
    }
    Ok(())
}
#[tokio::test]
async fn dynamic_pattern_selectors_do_not_claim_supported_analysis() -> Result<()> {
    let w = World::new("dynamic-pattern", 1);
    let mut e = w.engine();
    let r = e.call("sharper_check", w.args()).await?;
    assert_eq!(r["complete"], false);
    e.close().await;
    Ok(())
}
#[tokio::test]
async fn diagnostics_preserve_zero_based_utf16_ranges() -> Result<()> {
    let w = World::new("utf16-ranges", 1);
    std::fs::write(w.root.join("example.orbit"), "\u{1f600}violation\r\nworld")?;
    let mut engine = w.engine();
    let result = stable(&mut engine, w.args()).await;
    assert_eq!(result["complete"], true, "{result}");
    let diagnostic = &result["diagnostics"][0];
    assert_eq!(diagnostic["startLine"], 0);
    assert_eq!(diagnostic["startColumn"], 2);
    assert_eq!(diagnostic["endLine"], 0);
    assert_eq!(diagnostic["endColumn"], 11);
    assert_eq!(diagnostic["relatedInformation"][0]["startColumn"], 2);
    assert_eq!(diagnostic["relatedInformation"][0]["endColumn"], 11);
    engine.close().await;
    Ok(())
}
#[tokio::test]
async fn configuration_and_membership_changes_reload_but_source_edits_stay_warm() -> Result<()> {
    let w = World::new("normal", 1);
    let mut e = w.engine();
    assert_eq!(stable(&mut e, w.args()).await["complete"], true);
    let first = e.call("sharper_status", w.args()).await?;
    std::fs::write(w.root.join("project.config"), "new rules")?;
    assert_eq!(stable(&mut e, w.args()).await["complete"], true);
    let second = e.call("sharper_status", w.args()).await?;
    assert_ne!(first["processId"], second["processId"]);
    std::fs::write(w.root.join("second.orbit"), "fixed")?;
    assert_eq!(stable(&mut e, w.args()).await["complete"], true);
    let third = e.call("sharper_status", w.args()).await?;
    assert_ne!(second["processId"], third["processId"]);
    e.close().await;
    Ok(())
}
#[tokio::test]
async fn bounded_pool_evicts_old_workspaces_and_disposes_them() -> Result<()> {
    let w = World::new("normal", 1);
    let mut e = w.engine();
    let mut first = Value::Null;
    let mut dirs = Vec::new();
    for i in 0..5 {
        let temp = tempfile::tempdir()?;
        std::fs::write(temp.path().join("sample.orbit"), "fixed")?;
        let args = Args {
            workspace: Some(temp.path().to_string_lossy().into_owned()),
            ..Default::default()
        };
        assert_eq!(stable(&mut e, args.clone()).await["complete"], true);
        let status = e.call("sharper_status", args).await?;
        if i == 0 {
            first = status;
        }
        dirs.push(temp);
    }
    let args = Args {
        workspace: Some(dirs[0].path().to_string_lossy().into_owned()),
        ..Default::default()
    };
    let again = e.call("sharper_status", args).await?;
    assert_ne!(first["processId"], again["processId"]);
    e.close().await;
    Ok(())
}
#[tokio::test]
async fn ignored_dependency_trees_and_scope_escape_are_explicit() -> Result<()> {
    let w = World::new("normal", 1);
    std::fs::create_dir(w.root.join("node_modules"))?;
    std::fs::write(w.root.join("node_modules/ignore.orbit"), "violation")?;
    let mut e = w.engine();
    let mut args = w.args();
    args.path = None;
    let r = stable(&mut e, args).await;
    assert_eq!(r["diagnosticCount"], 1);
    let mut args = w.args();
    args.path = Some("../servers.json".into());
    assert!(e.call("sharper_check", args).await.is_err());
    e.close().await;
    Ok(())
}
#[tokio::test]
async fn optional_adapter_uses_only_closed_diagnostic_commands() -> Result<()> {
    let w = World::new("typescript", 1);
    let mut cfg: Value = serde_json::from_slice(&std::fs::read(&w.config)?)?;
    cfg["servers"][0]["diagnostics"] = json!("typeScript");
    std::fs::write(&w.config, cfg.to_string())?;
    let mut e = w.engine();
    let r = stable(&mut e, w.args()).await;
    assert_eq!(r["complete"], true, "{r}");
    assert_eq!(r["diagnostics"][0]["code"], "2322");
    e.close().await;
    let commands: Vec<_> = w
        .messages()
        .into_iter()
        .filter(|v| v["method"] == "workspace/executeCommand")
        .collect();
    assert_eq!(commands.len(), 2);
    Ok(())
}
#[test]
fn configuration_validation_is_language_agnostic_and_strict() {
    let t = tempfile::tempdir().unwrap();
    let p = t.path().join("config.json");
    for bad in [
        json!({}),
        json!({"servers":[]}),
        json!({"command":"server.cmd","languages":{".x":"anything"}}),
        json!({"command":"ok","languages":{"x":"bad"}}),
        json!({"command":"ok","languages":{}}),
        json!({"command":"ok","languages":{".x":"x",".X":"x"}}),
        json!({"command":"ok","languages":{".x":"x"},"unknown":1}),
        json!({"command":"ok","languages":{".x":"x"},"navigation":true}),
        json!({"command":"ok","languages":{".x":"x"},"navigation":false}),
    ] {
        std::fs::write(&p, bad.to_string()).unwrap();
        assert!(Config::load(&p).is_err(), "{bad}");
    }
    std::fs::write(
        &p,
        r#"{"command":"./enforcer","languages":{".orbit":"planet"},"arguments":["literal & $"]}"#,
    )
    .unwrap();
    let c = Config::load(&p).unwrap();
    assert!(c.servers[0].supports(Path::new("FILE.ORBIT")));
    assert!(Path::new(&c.servers[0].command).is_absolute());
}
#[test]
fn jsonc_setup_preserves_unrelated_comments_and_values() -> Result<()> {
    let input = "// header\n{\"model\":\"keep\",/* keep */\"mcp\":{\"other\":{\"command\":\"keep\"},\"sharper\":{\"old\":true},},}\n";
    let value = json!({"type":"local","command":["sharper","mcp"]});
    let updated = setup::edit_collection(input, "mcp", "sharper", &value)?;
    assert!(updated.contains("// header"));
    assert!(updated.contains("/* keep */"));
    let parsed: Value = serde_json::from_str(&setup::strip_jsonc(&updated)?)?;
    assert_eq!(parsed["model"], "keep");
    assert_eq!(parsed["mcp"]["other"]["command"], "keep");
    assert_eq!(
        updated,
        setup::edit_collection(&updated, "mcp", "sharper", &value)?
    );
    for source in ["{}", "{\"keep\":1,}", "{\"mcp\":{}}"] {
        let edited = setup::edit_collection(source, "mcp", "sharper", &value)?;
        let _: Value = serde_json::from_str(&setup::strip_jsonc(&edited)?)?;
    }
    Ok(())
}
#[tokio::test]
async fn framing_limits_truncation_and_unicode_roundtrip() -> Result<()> {
    let value = json!({"method":"unicode","params":{"text":"\u{1f600}"}});
    let mut bytes = Vec::new();
    write_lsp(&mut bytes, &value).await?;
    assert_eq!(
        read_lsp(&mut BufReader::new(bytes.as_slice())).await?,
        Some(value)
    );
    for bad in [
        b"Content-Length: -1\r\n\r\n".as_slice(),
        b"Content-Length: 1\r\nContent-Length: 1\r\n\r\n{}",
        b"Content-Length: 999999999\r\n\r\n",
        b"Content-Length: 5\r\n\r\n{}",
    ] {
        assert!(read_lsp(&mut BufReader::new(bad)).await.is_err());
    }
    Ok(())
}
#[test]
fn malformed_diagnostics_do_not_normalize_as_success() {
    for item in [
        json!({"message":"x","range":{"start":{"line":-1,"character":0},"end":{"line":0,"character":1}}}),
        json!({"message":3,"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}}}),
    ] {
        assert!(normalize::diagnostics(&json!([item]), "file:///tmp/x.orbit").is_err());
    }
}

#[test]
fn owned_runtime_updates_keep_user_environment_comments() -> Result<()> {
    let source = r#"{"mcpServers":{"sharper":{"env":{/* user env */"KEY":"keep"},"command":"old",/* keep separator */"url":"old"}}}"#;
    let value = json!({"env":{"KEY":"keep"},"command":"new","args":["mcp"],"type":"stdio"});
    let result = setup::edit_collection(source, "mcpServers", "sharper", &value)?;
    assert!(result.contains("/* user env */"));
    assert!(result.contains("/* keep separator */"));
    let parsed: Value = serde_json::from_str(&setup::strip_jsonc(&result)?)?;
    assert!(parsed["mcpServers"]["sharper"].get("url").is_none());
    assert_eq!(
        result,
        setup::edit_collection(&result, "mcpServers", "sharper", &value)?
    );
    Ok(())
}
#[tokio::test]
async fn required_document_recognition_ids_are_profile_specific() -> Result<()> {
    let w = World::new("normal", 1);
    let mut cfg: Value = serde_json::from_slice(&std::fs::read(&w.config)?)?;
    cfg["servers"][0]["requireResultId"] = json!(true);
    std::fs::write(&w.config, cfg.to_string())?;
    let mut e = w.engine();
    let r = e.call("sharper_check", w.args()).await?;
    assert_eq!(r["status"], "incomplete");
    assert_eq!(r["diagnosticCount"], 1);
    e.close().await;
    Ok(())
}
#[cfg(unix)]
#[tokio::test]
async fn symlink_inputs_cannot_escape_verification() -> Result<()> {
    let w = World::new("normal", 1);
    std::os::unix::fs::symlink(&w.config, w.root.join("linked.config"))?;
    let mut e = w.engine();
    assert!(e.call("sharper_check", w.args()).await.is_err());
    e.close().await;
    Ok(())
}
