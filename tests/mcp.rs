use anyhow::{Context, Result};
use serde_json::{Value, json};
use std::{path::PathBuf, process::Stdio, time::Duration};
use tempfile::TempDir;
use tokio::{
    io::{AsyncBufReadExt, AsyncWriteExt, BufReader},
    process::{Child, ChildStdin, ChildStdout},
    time::timeout,
};
struct Client {
    child: Child,
    input: ChildStdin,
    output: BufReader<ChildStdout>,
    roots: Vec<PathBuf>,
}
impl Client {
    async fn new(profile: &PathBuf, seconds: &str, roots: Vec<PathBuf>) -> Result<Self> {
        let host = std::env::var_os("SHARPER_TEST_HOST")
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from(env!("CARGO_BIN_EXE_sharper")));
        let mut child = tokio::process::Command::new(host)
            .args(["mcp", "--lsp-config"])
            .arg(profile)
            .args(["--timeout", seconds])
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true)
            .spawn()?;
        let input = child.stdin.take().unwrap();
        let output = BufReader::new(child.stdout.take().unwrap());
        let mut c = Self {
            child,
            input,
            output,
            roots,
        };
        c.send(json!({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"roots":{}},"clientInfo":{"name":"rust-test","version":"1"}}})).await?;
        let initialized = c.response(1).await?;
        assert_eq!(initialized["result"]["protocolVersion"], "2025-11-25");
        assert!(
            initialized["result"]["serverInfo"]["icons"][0]["src"]
                .as_str()
                .unwrap()
                .starts_with("data:image/png;base64,")
        );
        c.send(json!({"jsonrpc":"2.0","method":"notifications/initialized"}))
            .await?;
        Ok(c)
    }
    async fn send(&mut self, value: Value) -> Result<()> {
        let mut data = serde_json::to_vec(&value)?;
        data.push(b'\n');
        self.input.write_all(&data).await?;
        self.input.flush().await?;
        Ok(())
    }
    async fn response(&mut self, id: u64) -> Result<Value> {
        timeout(Duration::from_secs(30), async {
            loop {
                let mut line = String::new();
                assert_ne!(
                    self.output.read_line(&mut line).await?,
                    0,
                    "MCP process closed unexpectedly"
                );
                let value: Value = serde_json::from_str(&line)?;
                if value["method"] == "roots/list" {
                    let roots: Vec<_> = self
                        .roots
                        .iter()
                        .map(|p| json!({"uri":sharper::normalize::uri(p).unwrap(),"name":"test"}))
                        .collect();
                    self.send(json!({"jsonrpc":"2.0","id":value["id"],"result":{"roots":roots}}))
                        .await?;
                    continue;
                }
                if value["id"] == id {
                    return Ok(value);
                }
            }
        })
        .await
        .context("MCP response timed out")?
    }
    async fn call(&mut self, id: u64, name: &str, args: Value) -> Result<Value> {
        self.send(json!({"jsonrpc":"2.0","id":id,"method":"tools/call","params":{"name":name,"arguments":args}})).await?;
        self.response(id).await
    }
    async fn close(mut self) -> Result<()> {
        self.input.shutdown().await?;
        drop(self.input);
        let result = timeout(Duration::from_secs(10), self.child.wait()).await?;
        assert!(result?.success());
        Ok(())
    }
}
fn world(mode: &str) -> Result<(TempDir, PathBuf, PathBuf)> {
    let t = tempfile::tempdir()?;
    let root = t.path().join("work");
    std::fs::create_dir(&root)?;
    std::fs::write(root.join("test.orbit"), "violation")?;
    let exe = std::env::current_exe()?;
    let fake = exe
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join("examples")
        .join(if cfg!(windows) {
            "fake-lsp.exe"
        } else {
            "fake-lsp"
        });
    let profile = t.path().join("profile.json");
    std::fs::write(
        &profile,
        serde_json::to_vec(
            &json!({"command":fake,"arguments":["--mode",mode],"languages":{".orbit":"planet"},"readyNotification":"test/ready"}),
        )?,
    )?;
    Ok((t, root, profile))
}
fn git(root: &std::path::Path, args: &[&str]) -> Result<()> {
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
async fn client_root_selects_nested_worktree_and_keeps_parent_feedback_isolated() -> Result<()> {
    let (_t, root, profile) = world("normal")?;
    let root = sharper::workspace::clean(std::fs::canonicalize(root)?);
    git(&root, &["init", "--quiet"])?;
    git(&root, &["add", "test.orbit"])?;
    git(&root, &["commit", "--quiet", "-m", "Create test checkout"])?;
    let agent = root.join(".worktrees/agent");
    git(
        &root,
        &[
            "worktree",
            "add",
            "--quiet",
            "-b",
            "agent",
            agent.to_str().unwrap(),
        ],
    )?;
    let agent = sharper::workspace::clean(std::fs::canonicalize(agent)?);
    assert!(agent.join(".git").is_file());
    let mut client = Client::new(&profile, "20", vec![root.clone()]).await?;
    let parent = client.call(2, "sharper_check", json!({"path":"."})).await?;
    let result = &parent["result"]["structuredContent"];
    assert_eq!(parent["result"]["isError"], false, "{parent}");
    assert_eq!(result["complete"], true, "{parent}");
    assert_eq!(
        result["diagnosticCount"], 1,
        "Nested worktree leaked into parent: {parent}"
    );
    assert_eq!(
        result["diagnostics"][0]["filePath"],
        json!(root.join("test.orbit"))
    );
    let parent_status = client.call(3, "sharper_status", json!({})).await?;
    std::fs::write(agent.join("test.orbit"), "fixed")?;
    let nested = client
        .call(
            4,
            "sharper_check",
            json!({"path":".worktrees/agent/test.orbit"}),
        )
        .await?;
    let result = &nested["result"]["structuredContent"];
    assert_eq!(nested["result"]["isError"], false, "{nested}");
    assert_eq!(result["complete"], true, "{nested}");
    assert_eq!(result["diagnosticCount"], 0);
    assert_eq!(result["requestedPath"], json!(agent.join("test.orbit")));
    let agent_status = client
        .call(5, "sharper_status", json!({"workspace":agent}))
        .await?;
    let parent_state = &parent_status["result"]["structuredContent"];
    let agent_state = &agent_status["result"]["structuredContent"];
    assert_eq!(agent_state["workspace"], json!(agent));
    assert_ne!(parent_state["processId"], agent_state["processId"]);
    // Changes to the nested checkout's configuration must not restart the parent LSP.
    std::fs::write(agent.join("project.config"), "agent rules")?;
    let parent = client
        .call(6, "sharper_check", json!({"path":"test.orbit"}))
        .await?;
    assert_eq!(
        parent["result"]["structuredContent"]["complete"], true,
        "{parent}"
    );
    assert_eq!(parent["result"]["structuredContent"]["diagnosticCount"], 1);
    let parent_again = client.call(7, "sharper_status", json!({})).await?;
    assert_eq!(
        parent_again["result"]["structuredContent"]["processId"],
        parent_state["processId"]
    );
    client.close().await
}
#[tokio::test]
async fn actual_mcp_stdio_lists_tools_and_returns_structured_agnostic_feedback() -> Result<()> {
    let (_t, root, profile) = world("normal")?;
    let mut client = Client::new(&profile, "20", vec![root.clone()]).await?;
    client
        .send(json!({"jsonrpc":"2.0","id":2,"method":"tools/list"}))
        .await?;
    let tools = client.response(2).await?;
    assert_eq!(tools["result"]["tools"].as_array().unwrap().len(), 5);
    let schema = &tools["result"]["tools"][0]["inputSchema"];
    assert!(schema["properties"].get("backend").is_none());
    let response = client
        .call(3, "sharper_check", json!({"path":"test.orbit"}))
        .await?;
    assert_eq!(response["result"]["isError"], false, "{response}");
    assert_eq!(response["result"]["structuredContent"]["complete"], true);
    let compact = &response["result"]["structuredContent"];
    assert!(compact.get("limitations").is_none());
    assert!(compact.get("backendResults").is_none());
    assert!(compact.get("errors").is_none());
    assert_eq!(
        serde_json::from_str::<Value>(response["result"]["content"][0]["text"].as_str().unwrap())?,
        *compact
    );
    assert_eq!(
        response["result"]["structuredContent"]["diagnosticCount"],
        1
    );
    let status = client
        .call(4, "sharper_status", json!({"workspace":root}))
        .await?;
    assert_eq!(status["result"]["structuredContent"]["available"], true);
    let state = &status["result"]["structuredContent"];
    for omitted in [
        "limitations",
        "solution",
        "diagnosticProviders",
        "lastError",
    ] {
        assert!(state.get(omitted).is_none(), "{state}");
    }
    client.close().await
}
#[tokio::test]
async fn cancellation_stops_hung_analysis_and_session_can_recover() -> Result<()> {
    let (_t, root, profile) = world("hang")?;
    let mut c = Client::new(&profile, "20", vec![]).await?;
    let status = c
        .call(2, "sharper_status", json!({"workspace":root}))
        .await?;
    assert_eq!(status["result"]["structuredContent"]["available"], true);
    c.send(json!({"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"sharper_check","arguments":{"workspace":root,"path":"test.orbit"}}})).await?;
    tokio::time::sleep(Duration::from_millis(150)).await;
    c.send(json!({"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":3}}))
        .await?;
    let r = c.response(3).await?;
    assert_eq!(r["result"]["structuredContent"]["status"], "cancelled");
    assert_eq!(r["result"]["structuredContent"]["freshness"], "uncertain");
    for omitted in ["limitations", "diagnostics", "locations"] {
        assert!(r["result"]["structuredContent"].get(omitted).is_none());
    }
    let status = c
        .call(4, "sharper_status", json!({"workspace":root}))
        .await?;
    assert_eq!(status["result"]["structuredContent"]["available"], true);
    c.close().await
}
#[tokio::test]
async fn request_deadline_includes_a_hung_server() -> Result<()> {
    let (_t, root, profile) = world("hang")?;
    let mut c = Client::new(&profile, "1", vec![]).await?;
    let r = c
        .call(
            2,
            "sharper_check",
            json!({"workspace":root,"path":"test.orbit"}),
        )
        .await?;
    assert_eq!(r["result"]["structuredContent"]["status"], "timedOut");
    assert_eq!(r["result"]["structuredContent"]["complete"], false);
    c.close().await
}
#[tokio::test]
async fn cancellation_kills_the_server_process_tree() -> Result<()> {
    let (_t, root, profile) = world("tree")?;
    let mut c = Client::new(&profile, "20", vec![]).await?;
    c.call(2, "sharper_status", json!({"workspace":root}))
        .await?;
    timeout(Duration::from_secs(5), async {
        while !root.join("heartbeat").exists() {
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await?;
    c.send(json!({"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"sharper_check","arguments":{"workspace":root,"path":"test.orbit"}}})).await?;
    tokio::time::sleep(Duration::from_millis(100)).await;
    c.send(json!({"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":3}}))
        .await?;
    let r = c.response(3).await?;
    assert_eq!(r["result"]["structuredContent"]["complete"], false);
    let before = std::fs::read(root.join("heartbeat"))?;
    tokio::time::sleep(Duration::from_millis(150)).await;
    let after = std::fs::read(root.join("heartbeat"))?;
    assert_eq!(before, after, "Descendant process survived cancellation");
    c.close().await
}
#[tokio::test]
async fn client_scope_and_argument_errors_are_actionable() -> Result<()> {
    let (_t, root, profile) = world("normal")?;
    let mut c = Client::new(
        &profile,
        "20",
        vec![root.clone(), root.parent().unwrap().to_owned()],
    )
    .await?;
    let r = c
        .call(2, "sharper_check", json!({"path":"test.orbit"}))
        .await?;
    assert_eq!(r["result"]["isError"], true);
    let r = c
        .call(
            3,
            "sharper_check",
            json!({"workspace":root,"backend":"hardcoded"}),
        )
        .await?;
    assert_eq!(r["error"]["code"], -32602);
    let r = c
        .call(
            4,
            "sharper_check",
            json!({"workspace":root,"path":"../profile.json"}),
        )
        .await?;
    assert_eq!(r["result"]["isError"], true);
    c.close().await
}
#[tokio::test]
async fn cli_setup_preserves_jsonc_for_supported_agents_and_rejects_bad_profiles() -> Result<()> {
    let (t, _root, profile) = world("normal")?;
    for (agent, key) in [
        ("claude", "mcpServers"),
        ("opencode", "mcp"),
        ("copilot", "servers"),
    ] {
        let config = t.path().join(format!("{agent}.json"));
        std::fs::write(&config, "{ /* keep me */ \"unrelated\":true }")?;
        let mut command = tokio::process::Command::new(env!("CARGO_BIN_EXE_sharper"));
        command.args(["setup", agent, "--lsp-config"]).arg(&profile);
        if agent == "copilot" {
            command.args(["--client", "vscode", "--vscode-config"]);
        } else {
            command.arg("--config");
        }
        let out = command.arg(&config).output().await?;
        assert!(
            out.status.success(),
            "{}",
            String::from_utf8_lossy(&out.stderr)
        );
        let text = std::fs::read_to_string(&config)?;
        assert!(text.contains("/* keep me */"));
        let json: Value = serde_json::from_str(&sharper::setup::strip_jsonc(&text)?)?;
        assert_eq!(json["unrelated"], true);
        assert!(json[key]["sharper"].to_string().contains("--lsp-config"));
        assert!(
            json[key]["sharper"]
                .to_string()
                .contains(&profile.file_name().unwrap().to_string_lossy().to_string())
        );
    }
    Ok(())
}

#[tokio::test]
async fn codex_registration_delegates_without_a_fixed_workspace_and_propagates_errors() -> Result<()>
{
    let (t, _root, profile) = world("normal")?;
    let exe = std::env::current_exe()?;
    let fake = exe
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join("examples")
        .join(if cfg!(windows) {
            "fake-lsp.exe"
        } else {
            "fake-lsp"
        });
    for (code, success) in [(0, true), (17, false)] {
        let home = t.path().join(format!("codex-{code}"));
        let output = tokio::process::Command::new(env!("CARGO_BIN_EXE_sharper"))
            .args(["setup", "codex", "--lsp-config"])
            .arg(&profile)
            .arg("--codex-path")
            .arg(&fake)
            .arg("--codex-home")
            .arg(&home)
            .env("SHARPER_FAKE_CODEX_EXIT", code.to_string())
            .output()
            .await?;
        assert_eq!(output.status.success(), success);
        let registration: Vec<String> =
            serde_json::from_slice(&std::fs::read(home.join("registration.json"))?)?;
        assert!(registration.iter().any(|s| s == "--lsp-config"));
        assert!(!registration.iter().any(|s| s == "--workspace"));
    }
    Ok(())
}
#[tokio::test]
async fn invalid_setup_profiles_leave_client_configuration_unchanged() -> Result<()> {
    let (t, _root, profile) = world("normal")?;
    std::fs::write(&profile, "{}")?;
    let config = t.path().join("client.json");
    let original = "{ /* preserve */ \"keep\":true }";
    std::fs::write(&config, original)?;
    let output = tokio::process::Command::new(env!("CARGO_BIN_EXE_sharper"))
        .args(["setup", "claude", "--lsp-config"])
        .arg(profile)
        .arg("--config")
        .arg(&config)
        .output()
        .await?;
    assert!(!output.status.success());
    assert_eq!(std::fs::read_to_string(config)?, original);
    Ok(())
}
#[tokio::test]
async fn guided_setup_can_use_a_profile_without_installing_or_registering() -> Result<()> {
    let (_t, _root, profile) = world("normal")?;
    let mut child = tokio::process::Command::new(env!("CARGO_BIN_EXE_sharper"))
        .args(["setup", "--no-color", "--lsp-config"])
        .arg(profile)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()?;
    child.stdin.take().unwrap().write_all(b"none\n").await?;
    let output = timeout(Duration::from_secs(10), child.wait_with_output()).await??;
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    assert!(String::from_utf8_lossy(&output.stdout).contains("Configuration ready"));
    Ok(())
}

async fn wizard_output(
    profile: Option<&std::path::Path>,
    input: &str,
    extra: &[&str],
) -> Result<std::process::Output> {
    let mut command = tokio::process::Command::new(env!("CARGO_BIN_EXE_sharper"));
    command.args(["setup", "--no-color"]);
    if let Some(profile) = profile {
        command.arg("--lsp-config").arg(profile);
    }
    let mut child = command
        .args(extra)
        .kill_on_drop(true)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()?;
    child
        .stdin
        .take()
        .unwrap()
        .write_all(input.as_bytes())
        .await?;
    Ok(timeout(Duration::from_secs(10), child.wait_with_output()).await??)
}
#[tokio::test]
async fn cli_help_and_invalid_choices_have_clear_exit_codes() -> Result<()> {
    for args in [
        vec!["--no-color", "--help"],
        vec!["setup", "--no-color", "--help"],
    ] {
        let output = tokio::process::Command::new(env!("CARGO_BIN_EXE_sharper"))
            .args(args)
            .output()
            .await?;
        assert!(output.status.success());
        let text = String::from_utf8(output.stdout)?;
        for option in ["--lsp-config", "--no-color"] {
            assert!(text.contains(option), "{text}");
        }
        assert!(!text.contains('\x1b'));
    }
    let output = tokio::process::Command::new(env!("CARGO_BIN_EXE_sharper"))
        .args(["setup", "copilot", "--client", "typo", "--no-color"])
        .output()
        .await?;
    assert_eq!(output.status.code(), Some(2));
    let error = String::from_utf8(output.stderr)?;
    assert!(error.contains("vscode"), "{error}");
    assert!(!error.contains('\x1b'));
    let piped = tokio::process::Command::new(env!("CARGO_BIN_EXE_sharper"))
        .env_remove("SHARPER_LSP_CONFIG")
        .stdin(Stdio::null())
        .output()
        .await?;
    assert!(piped.status.success());
    assert!(
        piped.stdout.is_empty(),
        "Human output leaked into default MCP mode"
    );
    Ok(())
}
#[tokio::test]
async fn guided_setup_recovers_from_invalid_values_and_saves_reviewed_profile() -> Result<()> {
    let (temp, _, existing) = world("normal")?;
    let fixture: Value = serde_json::from_slice(&std::fs::read(existing)?)?;
    let destination = temp.path().join("profiles with spaces/servers.json");
    let input = format!(
        "new\n\"{}\"\n\"{}\"\nnot-an-array\n[]\nnot-a-mapping\norbit=planet\nmaybe\nyes\n0\n",
        destination.display(),
        fixture["command"].as_str().unwrap()
    );
    let output = wizard_output(None, &input, &[]).await?;
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let text = String::from_utf8(output.stdout)?;
    for step in ["1/3", "2/3", "3/3"] {
        assert!(text.contains(step), "{text}");
    }
    assert!(!text.contains('\x1b'));
    let config = sharper::config::Config::load(&destination)?;
    assert_eq!(
        config.servers[0]
            .languages
            .get(".orbit")
            .map(String::as_str),
        Some("planet")
    );
    assert!(config.servers[0].arguments.is_empty());
    Ok(())
}
#[tokio::test]
async fn guided_setup_reviews_agent_choice_before_writing_configuration() -> Result<()> {
    let (temp, _, profile) = world("normal")?;
    let client = temp.path().join("client.json");
    let original = "{ /* keep */ \"unrelated\": true }";
    std::fs::write(&client, original)?;
    let extra = ["--config", client.to_str().unwrap()];
    let declined = wizard_output(Some(&profile), "unknown\n2\nno\n", &extra).await?;
    assert!(declined.status.success());
    assert_eq!(std::fs::read_to_string(&client)?, original);
    let accepted = wizard_output(Some(&profile), "CLAUDE CODE\nyes\n", &extra).await?;
    assert!(
        accepted.status.success(),
        "{}",
        String::from_utf8_lossy(&accepted.stderr)
    );
    let text = std::fs::read_to_string(client)?;
    assert!(text.contains("/* keep */"));
    let data: Value = serde_json::from_str(&sharper::setup::strip_jsonc(&text)?)?;
    assert!(data["mcpServers"]["sharper"].is_object());
    assert_eq!(data["unrelated"], true);
    Ok(())
}
#[tokio::test]
async fn guided_setup_cancellation_and_eof_preserve_existing_files() -> Result<()> {
    let (_temp, _, profile) = world("normal")?;
    let original = std::fs::read(&profile)?;
    for input in [
        "q\n".to_owned(),
        String::new(),
        format!("new\n\"{}\"\nq\n", profile.display()),
    ] {
        let output = wizard_output(None, &input, &[]).await?;
        assert_eq!(
            output.status.code(),
            Some(130),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
        assert_eq!(std::fs::read(&profile)?, original);
        assert!(!String::from_utf8_lossy(&output.stderr).contains("error:"));
    }
    Ok(())
}

#[tokio::test]
async fn guided_setup_respects_the_selected_copilot_destination() -> Result<()> {
    let (temp, _, profile) = world("normal")?;
    let cli = temp.path().join("copilot.json");
    let vscode = temp.path().join("vscode.json");
    let extra = [
        "--copilot-config",
        cli.to_str().unwrap(),
        "--vscode-config",
        vscode.to_str().unwrap(),
    ];
    let output = wizard_output(Some(&profile), "4\n2\nyes\n", &extra).await?;
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    assert!(!cli.exists());
    let json: Value = serde_json::from_str(&std::fs::read_to_string(vscode)?)?;
    assert!(json["servers"]["sharper"].is_object());
    Ok(())
}

#[tokio::test]
async fn explicit_setup_paths_work_without_home_environment() -> Result<()> {
    let (temp, _, profile) = world("normal")?;
    for agent in ["opencode", "copilot"] {
        let config = temp.path().join(format!("{agent}.json"));
        let vscode = temp.path().join("vscode-override.json");
        let mut command = tokio::process::Command::new(env!("CARGO_BIN_EXE_sharper"));
        command
            .args(["setup", agent, "--lsp-config"])
            .arg(&profile)
            .arg("--no-color");
        if agent == "copilot" {
            command
                .arg("--copilot-config")
                .arg(&config)
                .arg("--vscode-config")
                .arg(&vscode);
        } else {
            command.arg("--config").arg(&config);
        }
        for variable in ["HOME", "USERPROFILE", "APPDATA", "XDG_CONFIG_HOME"] {
            command.env_remove(variable);
        }
        let output = command.output().await?;
        assert!(
            output.status.success(),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
        assert!(config.is_file());
        if agent == "copilot" {
            assert!(vscode.is_file());
        }
    }
    let directory = temp.path().join("xdg-config");
    let output = tokio::process::Command::new(env!("CARGO_BIN_EXE_sharper"))
        .args(["setup", "opencode", "--lsp-config"])
        .arg(profile)
        .env_remove("HOME")
        .env_remove("USERPROFILE")
        .env_remove("OPENCODE_CONFIG")
        .env("XDG_CONFIG_HOME", &directory)
        .output()
        .await?;
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    assert!(directory.join("opencode/opencode.json").is_file());
    Ok(())
}
