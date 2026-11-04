use crate::config::{Config, absolute};
use anyhow::{Context, Result, bail, ensure};
use clap::{Args, ValueEnum};
use serde_json::{Value, json};
use std::{
    io::{self, IsTerminal, Write},
    path::{Path, PathBuf},
    process::Stdio,
    time::Duration,
};
#[derive(Clone, Debug, ValueEnum)]
pub enum Agent {
    Codex,
    Claude,
    Opencode,
    Copilot,
}
#[derive(Clone, Debug, Args)]
pub struct Options {
    pub agent: Option<Agent>,
    #[arg(long)]
    pub config: Option<PathBuf>,
    #[arg(long, default_value = "both")]
    pub client: String,
    #[arg(long)]
    pub copilot_config: Option<PathBuf>,
    #[arg(long)]
    pub vscode_config: Option<PathBuf>,
    #[arg(long, default_value = "codex")]
    pub codex_path: PathBuf,
    #[arg(long)]
    pub codex_home: Option<PathBuf>,
}
fn home() -> Result<PathBuf> {
    std::env::var_os(if cfg!(windows) { "USERPROFILE" } else { "HOME" })
        .map(PathBuf::from)
        .context("Cannot locate home directory; supply explicit configuration paths")
}
fn config_home() -> Result<PathBuf> {
    Ok(std::env::var_os("XDG_CONFIG_HOME")
        .map(PathBuf::from)
        .unwrap_or(home()?.join(".config")))
}
fn appdata() -> Result<PathBuf> {
    if cfg!(windows) {
        std::env::var_os("APPDATA")
            .map(PathBuf::from)
            .context("APPDATA is unavailable")
    } else {
        config_home()
    }
}
pub async fn run(options: Options, profile: Option<PathBuf>, no_color: bool) -> Result<()> {
    if options.agent.is_none() {
        return wizard(options, profile, no_color).await;
    }
    let profile=profile.context("Provide --lsp-config with your language-server configuration; run sharper setup for guidance")?;
    let profile = absolute(&profile)?;
    Config::load(&profile)?;
    let executable = std::env::current_exe()?;
    let launch = vec![
        "mcp".to_owned(),
        "--lsp-config".into(),
        profile.to_string_lossy().into_owned(),
    ];
    if matches!(options.agent, Some(Agent::Codex)) {
        let mut command = tokio::process::Command::new(&options.codex_path);
        command
            .args(["mcp", "add", "sharper", "--"])
            .arg(&executable)
            .args(&launch)
            .stdin(Stdio::null())
            .kill_on_drop(true);
        #[cfg(windows)]
        command.creation_flags(0x08000000);
        if let Some(path) = &options.codex_home {
            command.env("CODEX_HOME", path);
        }
        let mut process = command
            .spawn()
            .context("Cannot launch Codex CLI; use --codex-path")?;
        let status = match tokio::time::timeout(Duration::from_secs(30), process.wait()).await {
            Ok(r) => r?,
            Err(_) => {
                let _ = process.kill().await;
                bail!("Codex registration timed out")
            }
        };
        ensure!(status.success(), "Codex registration failed ({status})");
        println!("Configured Codex. Restart the MCP connection.");
        return Ok(());
    }
    let mut plans = Vec::new();
    match options.agent.unwrap() {
        Agent::Claude => {
            let path = if let Some(path) = options.config {
                path
            } else if let Some(dir) = std::env::var_os("CLAUDE_CONFIG_DIR") {
                PathBuf::from(dir).join(".claude.json")
            } else {
                home()?.join(".claude.json")
            };
            plans.push(plan(&path, "mcpServers", false, &executable, &launch)?);
        }
        Agent::Opencode => {
            let path = options
                .config
                .or_else(|| std::env::var_os("OPENCODE_CONFIG").map(PathBuf::from))
                .unwrap_or(config_home()?.join("opencode/opencode.json"));
            plans.push(plan(&path, "mcp", true, &executable, &launch)?);
        }
        Agent::Copilot => {
            ensure!(
                ["cli", "vscode", "both"].contains(&options.client.as_str()),
                "client must be cli, vscode or both"
            );
            if options.client != "vscode" {
                let path = options.copilot_config.unwrap_or(
                    std::env::var_os("COPILOT_HOME")
                        .map(PathBuf::from)
                        .unwrap_or(home()?.join(".copilot"))
                        .join("mcp-config.json"),
                );
                plans.push(plan(&path, "mcpServers", false, &executable, &launch)?);
            }
            if options.client != "cli" {
                let path = options
                    .vscode_config
                    .unwrap_or(if cfg!(target_os = "macos") {
                        home()?.join("Library/Application Support/Code/User/mcp.json")
                    } else {
                        appdata()?.join("Code/User/mcp.json")
                    });
                plans.push(plan(&path, "servers", false, &executable, &launch)?);
            }
        }
        Agent::Codex => unreachable!(),
    }
    for plan in &plans {
        plan.check_unchanged()?;
    }
    for plan in plans {
        plan.write()?;
    }
    println!(
        "Sharper is registered without a fixed workspace. Restart the client's MCP connection."
    );
    Ok(())
}
struct Plan {
    path: PathBuf,
    original: Option<String>,
    updated: String,
}
impl Plan {
    fn check_unchanged(&self) -> Result<()> {
        ensure!(
            std::fs::read_to_string(&self.path).ok() == self.original,
            "Client configuration changed during setup; retry"
        );
        Ok(())
    }
    fn write(self) -> Result<()> {
        self.check_unchanged()?;
        let parent = self.path.parent().context("Configuration has no parent")?;
        std::fs::create_dir_all(parent)?;
        if self.original.as_deref() == Some(&self.updated) {
            println!("Already configured: {}", self.path.display());
            return Ok(());
        }
        let temp = parent.join(format!(
            ".sharper-{}-{}.tmp",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)?
                .as_nanos()
        ));
        let result = (|| -> Result<()> {
            let mut file = std::fs::OpenOptions::new()
                .write(true)
                .create_new(true)
                .open(&temp)?;
            file.write_all(self.updated.as_bytes())?;
            file.sync_all()?;
            if let Ok(metadata) = std::fs::metadata(&self.path) {
                std::fs::set_permissions(&temp, metadata.permissions())?;
            }
            self.check_unchanged()?;
            std::fs::rename(&temp, &self.path)?;
            Ok(())
        })();
        if result.is_err() {
            let _ = std::fs::remove_file(&temp);
        }
        result?;
        println!("Configured: {}", self.path.display());
        Ok(())
    }
}
fn plan(
    path: &Path,
    collection: &str,
    open: bool,
    executable: &Path,
    args: &[String],
) -> Result<Plan> {
    let path = absolute(path)?;
    let original = match std::fs::read_to_string(&path) {
        Ok(s) => Some(s),
        Err(e) if e.kind() == io::ErrorKind::NotFound => None,
        Err(e) => return Err(e.into()),
    };
    let source = original.as_deref().unwrap_or("{}");
    let clean = strip_jsonc(source)?;
    let parsed: Value =
        serde_json::from_str(&clean).context("Client configuration is not valid JSON/JSONC")?;
    ensure!(parsed.is_object(), "Client configuration must be an object");
    let mut entry = parsed[collection]["sharper"]
        .as_object()
        .cloned()
        .unwrap_or_default();
    for key in ["url", "cwd", "tools", "args", "command"] {
        entry.remove(key);
    }
    if open {
        entry.insert("type".into(), json!("local"));
        entry.insert("enabled".into(), json!(true));
        let mut launch = vec![executable.to_string_lossy().into_owned()];
        launch.extend_from_slice(args);
        entry.insert("command".into(), json!(launch));
    } else {
        entry.insert("type".into(), json!("stdio"));
        entry.insert("command".into(), json!(executable));
        entry.insert("args".into(), json!(args));
    }
    let updated = edit_collection(source, collection, "sharper", &Value::Object(entry))?;
    Ok(Plan {
        path,
        original,
        updated,
    })
}
// Replace one owned JSONC value, preserving every byte outside the owned range.
pub fn edit_collection(source: &str, collection: &str, key: &str, value: &Value) -> Result<String> {
    let clean = strip_jsonc(source)?;
    let parsed: Value = serde_json::from_str(&clean)?;
    ensure!(parsed.is_object(), "Configuration must be an object");
    let root = clean.find('{').context("Missing root object")?;
    let entries = members(source, root)?;
    ensure!(
        entries.iter().filter(|e| e.0 == collection).count() <= 1,
        "Duplicate configuration collection"
    );
    if let Some((_, start, end)) = entries.iter().find(|e| e.0 == collection) {
        ensure!(
            parsed[collection].is_object(),
            "Client server collection must be an object"
        );
        let entries = members(source, *start)?;
        ensure!(
            entries.iter().filter(|e| e.0 == key).count() <= 1,
            "Duplicate owned server entry"
        );
        if let Some((_, a, b)) = entries.iter().find(|e| e.0 == key) {
            if let (Some(before), Some(after)) =
                (parsed[collection][key].as_object(), value.as_object())
            {
                return patch_object(source, *a, before, after);
            }
            return Ok(format!(
                "{}{}{}",
                &source[..*a],
                serde_json::to_string_pretty(value)?,
                &source[*b..]
            ));
        }
        let result = insert(source, *start, *end, key, value)?;
        return Ok(result);
    }
    insert(
        source,
        root,
        scan_value(source, root)?,
        collection,
        &json!({key:value}),
    )
}
fn insert(source: &str, start: usize, end: usize, key: &str, value: &Value) -> Result<String> {
    let entries = members(source, start)?;
    let trailing = entries
        .last()
        .map(|entry| skip(source, entry.2).map(|index| source.as_bytes().get(index) == Some(&b',')))
        .transpose()?
        .unwrap_or(false);
    let comma = if entries.is_empty() || trailing {
        ""
    } else {
        ","
    };
    let updated = format!(
        "{}{comma}\n  {}: {}\n{}",
        &source[..end - 1],
        serde_json::to_string(key)?,
        serde_json::to_string_pretty(value)?,
        &source[end - 1..]
    );
    let _: Value = serde_json::from_str(&strip_jsonc(&updated)?)?;
    Ok(updated)
}
fn skip(source: &str, mut i: usize) -> Result<usize> {
    let b = source.as_bytes();
    loop {
        while i < b.len() && b[i].is_ascii_whitespace() {
            i += 1
        }
        if b.get(i..i + 2) == Some(b"//") {
            while i < b.len() && b[i] != b'\n' {
                i += 1
            }
        } else if b.get(i..i + 2) == Some(b"/*") {
            i += 2;
            while i + 1 < b.len() && &b[i..i + 2] != b"*/" {
                i += 1
            }
            ensure!(i + 1 < b.len(), "Unterminated JSONC comment");
            i += 2
        } else {
            return Ok(i);
        }
    }
}
fn string_end(source: &str, start: usize) -> Result<usize> {
    let b = source.as_bytes();
    let mut i = start + 1;
    while i < b.len() {
        if b[i] == b'\\' {
            i += 2
        } else if b[i] == b'"' {
            return Ok(i + 1);
        } else {
            i += 1
        }
    }
    bail!("Unterminated JSON string")
}
fn scan_value(source: &str, start: usize) -> Result<usize> {
    let b = source.as_bytes();
    let start = skip(source, start)?;
    if b.get(start) == Some(&b'"') {
        return string_end(source, start);
    }
    if matches!(b.get(start), Some(b'{') | Some(b'[')) {
        let mut depth = 0;
        let mut i = start;
        while i < b.len() {
            i = skip(source, i)?;
            match b.get(i) {
                Some(b'"') => {
                    i = string_end(source, i)?;
                    continue;
                }
                Some(b'{') | Some(b'[') => depth += 1,
                Some(b'}') | Some(b']') => {
                    depth -= 1;
                    if depth == 0 {
                        return Ok(i + 1);
                    }
                }
                None => break,
                _ => {}
            }
            i += 1
        }
        bail!("Unterminated JSON container")
    }
    let mut i = start;
    while i < b.len() && !b[i].is_ascii_whitespace() && !b",}]".contains(&b[i]) {
        i += 1
    }
    ensure!(i > start, "Missing JSON value");
    Ok(i)
}
fn details(source: &str, start: usize) -> Result<Vec<(String, usize, usize, usize)>> {
    let mut result = Vec::new();
    let mut i = start + 1;
    loop {
        i = skip(source, i)?;
        if source.as_bytes().get(i) == Some(&b'}') {
            break;
        }
        ensure!(
            source.as_bytes().get(i) == Some(&b'"'),
            "Expected JSON object key"
        );
        let key_start = i;
        let end = string_end(source, i)?;
        let key: String = serde_json::from_str(&source[i..end])?;
        i = skip(source, end)?;
        ensure!(source.as_bytes().get(i) == Some(&b':'), "Expected colon");
        let value = skip(source, i + 1)?;
        let end = scan_value(source, value)?;
        result.push((key, key_start, value, end));
        i = skip(source, end)?;
        if source.as_bytes().get(i) == Some(&b',') {
            i += 1
        } else {
            ensure!(
                source.as_bytes().get(i) == Some(&b'}'),
                "Expected comma or closing brace"
            );
        }
    }
    Ok(result)
}
fn members(source: &str, start: usize) -> Result<Vec<(String, usize, usize)>> {
    Ok(details(source, start)?
        .into_iter()
        .map(|(key, _, a, b)| (key, a, b))
        .collect())
}
fn patch_object(
    source: &str,
    start: usize,
    before: &serde_json::Map<String, Value>,
    after: &serde_json::Map<String, Value>,
) -> Result<String> {
    let mut text = source.to_owned();
    for key in before.keys().filter(|key| !after.contains_key(*key)) {
        let entries = details(&text, start)?;
        let index = entries
            .iter()
            .position(|e| &e.0 == key)
            .context("Missing owned key")?;
        let entry = &entries[index];
        let next = skip(&text, entry.3)?;
        let mut spans = vec![(entry.1, entry.3)];
        if text.as_bytes().get(next) == Some(&b',') {
            spans.push((next, next + 1));
        } else if index > 0 {
            let comma = skip(&text, entries[index - 1].3)?;
            ensure!(
                text.as_bytes().get(comma) == Some(&b','),
                "Missing member separator"
            );
            spans.push((comma, comma + 1));
        }
        spans.sort_unstable_by_key(|entry| std::cmp::Reverse(entry.0));
        for (a, b) in spans {
            text.replace_range(a..b, "");
        }
    }
    for (key, value) in after {
        if before.get(key) == Some(value) {
            continue;
        }
        if let Some((_, _, a, b)) = details(&text, start)?.into_iter().find(|e| &e.0 == key) {
            text.replace_range(a..b, &serde_json::to_string_pretty(value)?);
        } else {
            text = insert(&text, start, scan_value(&text, start)?, key, value)?;
        }
    }
    let _: Value = serde_json::from_str(&strip_jsonc(&text)?)?;
    Ok(text)
}
pub fn strip_jsonc(source: &str) -> Result<String> {
    let mut b = source.as_bytes().to_vec();
    if b.starts_with(&[0xef, 0xbb, 0xbf]) {
        b[..3].fill(b' ');
    }
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'"' {
            i = string_end(source, i)?;
            continue;
        }
        if b.get(i..i + 2) == Some(b"//") || b.get(i..i + 2) == Some(b"/*") {
            let end = skip(source, i)?;
            for byte in &mut b[i..end] {
                if *byte != b'\n' && *byte != b'\r' {
                    *byte = b' ';
                }
            }
            i = end
        } else {
            i += 1
        }
    }
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'"' {
            let s = std::str::from_utf8(&b)?;
            i = string_end(s, i)?;
            continue;
        }
        if b[i] == b',' {
            let mut j = i + 1;
            while j < b.len() && b[j].is_ascii_whitespace() {
                j += 1
            }
            if matches!(b.get(j), Some(b'}') | Some(b']')) {
                b[i] = b' ';
            }
        }
        i += 1
    }
    Ok(String::from_utf8(b)?)
}
fn prompt(message: &str) -> Result<String> {
    print!("{message} ");
    io::stdout().flush()?;
    let mut line = String::new();
    ensure!(
        io::stdin().read_line(&mut line)? > 0,
        "Setup cancelled (EOF)"
    );
    let value = line.trim().to_owned();
    ensure!(value != "q", "Setup cancelled");
    Ok(value)
}
async fn wizard(mut options: Options, profile: Option<PathBuf>, no_color: bool) -> Result<()> {
    let color = !no_color && io::stdout().is_terminal() && std::env::var_os("NO_COLOR").is_none();
    println!(
        "{}SHARPER — connect your language servers and agents{}",
        if color { "\x1b[35m" } else { "" },
        if color { "\x1b[0m" } else { "" }
    );
    println!(
        "Language-agnostic setup. Type q to cancel. Server installation follows its own instructions; Sharper never installs project dependencies."
    );
    let path = if let Some(path) = profile {
        Config::load(&path)?;
        absolute(&path)?
    } else {
        let input = prompt("Path to your LSP JSON configuration (or 'new' to create one):")?;
        if input == "new" {
            let command = prompt("Server executable or runtime command:")?;
            let arguments: Vec<String> = serde_json::from_str(&prompt(
                "Arguments as a JSON string array, e.g. [\"--stdio\"]:",
            )?)?;
            let languages: serde_json::Map<String, Value> = serde_json::from_str(&prompt(
                "Extensions and language IDs as JSON, e.g. {\".py\":\"python\"}:",
            )?)?;
            let path = absolute(Path::new(&prompt(
                "Where should the LSP configuration be saved?",
            )?))?;
            let data = serde_json::to_string_pretty(
                &json!({"servers":[{"id":"rules","command":command,"arguments":arguments,"languages":languages}]}),
            )?;
            Config::parse(
                data.as_bytes(),
                path.parent().context("Profile has no parent")?,
            )?;
            println!("{data}");
            ensure!(
                prompt(&format!("Write {}? [y/N]", path.display()))?.eq_ignore_ascii_case("y"),
                "Setup cancelled"
            );
            ensure!(
                !path.exists(),
                "Configuration exists; choose a new file or edit it manually"
            );
            if let Some(parent) = path.parent() {
                std::fs::create_dir_all(parent)?;
            }
            let mut created = std::fs::OpenOptions::new()
                .write(true)
                .create_new(true)
                .open(&path)?;
            created.write_all(data.as_bytes())?;
            created.sync_all()?;
            path
        } else {
            let path = absolute(Path::new(&input))?;
            Config::load(&path)?;
            path
        }
    };
    let agent = prompt("Client: codex, claude, opencode, copilot (or 'none' to finish):")?;
    if agent == "none" {
        println!(
            "Configuration ready. MCP launch: sharper mcp --lsp-config {}",
            path.display()
        );
        return Ok(());
    }
    options.agent = Some(match agent.as_str() {
        "codex" => Agent::Codex,
        "claude" => Agent::Claude,
        "opencode" => Agent::Opencode,
        "copilot" => Agent::Copilot,
        _ => bail!("Unknown client"),
    });
    ensure!(
        prompt("Register Sharper in the selected client's user configuration? [y/N]")?
            .eq_ignore_ascii_case("y"),
        "Setup cancelled"
    );
    // Box breaks the finite run -> wizard -> run asynchronous recursion.
    Box::pin(run(options, Some(path), no_color)).await
}
