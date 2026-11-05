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
    /// Agent to connect. Omit this to walk through setup.
    pub agent: Option<Agent>,
    /// Claude Code or OpenCode configuration file to update.
    #[arg(long, value_name = "CLIENT_CONFIG")]
    pub config: Option<PathBuf>,
    /// GitHub Copilot destination.
    #[arg(long, default_value = "both", value_parser = ["cli", "vscode", "both"])]
    pub client: String,
    /// Override GitHub Copilot CLI configuration.
    #[arg(long, value_name = "FILE")]
    pub copilot_config: Option<PathBuf>,
    /// Override VS Code MCP configuration.
    #[arg(long, value_name = "FILE")]
    pub vscode_config: Option<PathBuf>,
    /// Codex CLI executable used for native registration.
    #[arg(long, default_value = "codex", value_name = "EXECUTABLE")]
    pub codex_path: PathBuf,
    /// Override the Codex profile directory.
    #[arg(long, value_name = "DIRECTORY")]
    pub codex_home: Option<PathBuf>,
}
impl Agent {
    fn label(&self) -> &'static str {
        match self {
            Self::Codex => "Codex",
            Self::Claude => "Claude Code",
            Self::Opencode => "OpenCode",
            Self::Copilot => "GitHub Copilot",
        }
    }
}
fn home() -> Result<PathBuf> {
    std::env::var_os(if cfg!(windows) { "USERPROFILE" } else { "HOME" })
        .map(PathBuf::from)
        .context("Cannot locate home directory; supply explicit configuration paths")
}
fn config_home() -> Result<PathBuf> {
    match std::env::var_os("XDG_CONFIG_HOME") {
        Some(path) => Ok(PathBuf::from(path)),
        None => Ok(home()?.join(".config")),
    }
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
    let ui = Ui::new(no_color);
    let agent_name = options.agent.as_ref().unwrap().label();
    let profile=profile.context("Choose a language-service profile with --lsp-config <SERVERS_JSON>, or run sharper setup for guided configuration")?;
    let profile = absolute(&profile)?;
    Config::load(&profile)?;
    ui.hint(&format!("Connecting {agent_name}..."));
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
        if no_color {
            command.env("NO_COLOR", "1");
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
        ui.success("Connected Codex");
        ui.next_steps();
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
            let path = match options
                .config
                .or_else(|| std::env::var_os("OPENCODE_CONFIG").map(PathBuf::from))
            {
                Some(path) => path,
                None => config_home()?.join("opencode/opencode.json"),
            };
            plans.push(plan(&path, "mcp", true, &executable, &launch)?);
        }
        Agent::Copilot => {
            ensure!(
                ["cli", "vscode", "both"].contains(&options.client.as_str()),
                "client must be cli, vscode or both"
            );
            if options.client != "vscode" {
                let path = match options.copilot_config {
                    Some(path) => path,
                    None => {
                        let directory = match std::env::var_os("COPILOT_HOME") {
                            Some(path) => PathBuf::from(path),
                            None => home()?.join(".copilot"),
                        };
                        directory.join("mcp-config.json")
                    }
                };
                plans.push(plan(&path, "mcpServers", false, &executable, &launch)?);
            }
            if options.client != "cli" {
                let path = match options.vscode_config {
                    Some(path) => path,
                    None if cfg!(target_os = "macos") => {
                        home()?.join("Library/Application Support/Code/User/mcp.json")
                    }
                    None => appdata()?.join("Code/User/mcp.json"),
                };
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
    ui.success(&format!("Connected {agent_name}"));
    ui.next_steps();
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
#[derive(Debug)]
pub struct Cancelled;
impl std::fmt::Display for Cancelled {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("Setup cancelled")
    }
}
impl std::error::Error for Cancelled {}

struct Ui {
    color: bool,
}
impl Ui {
    fn new(no_color: bool) -> Self {
        Self {
            color: !no_color
                && io::stdout().is_terminal()
                && std::env::var_os("NO_COLOR").is_none(),
        }
    }
    fn paint(&self, text: &str, code: &str) -> String {
        if self.color {
            format!("\x1b[{code}m{text}\x1b[0m")
        } else {
            text.into()
        }
    }
    fn step(&self, number: u8, title: &str) {
        println!("\n{}  {title}", self.paint(&format!("{number}/3"), "1;36"));
    }
    fn hint(&self, text: &str) {
        println!("  {}", self.paint(text, "2"));
    }
    fn retry(&self, error: &anyhow::Error) {
        println!("  {} {error:#}", self.paint("!", "33"));
    }
    fn success(&self, text: &str) {
        println!("\n{} {text}", self.paint("[ok]", "32"));
    }
    fn next_steps(&self) {
        println!(
            "\nNext: restart your agent to load Sharper.\nAfter editing files, ask your agent to check the affected code."
        );
    }
    fn read<T>(
        &self,
        label: &str,
        default: Option<&str>,
        mut parse: impl FnMut(&str) -> Result<T>,
    ) -> Result<T> {
        loop {
            let suffix = default
                .map(|value| format!(" [{value}]"))
                .unwrap_or_default();
            print!("  {label}{suffix} {} ", self.paint(">", "36"));
            io::stdout().flush()?;
            let mut line = String::new();
            if io::stdin().read_line(&mut line)? == 0 {
                return Err(Cancelled.into());
            }
            let value = line.trim();
            if value.eq_ignore_ascii_case("q") {
                return Err(Cancelled.into());
            }
            let value = if value.is_empty() {
                default.unwrap_or("")
            } else {
                value
            };
            match parse(value) {
                Ok(value) => return Ok(value),
                Err(error) => self.retry(&error),
            }
        }
    }
    fn confirm(&self, label: &str) -> Result<bool> {
        self.read(label, Some("n"), |value| {
            match value.to_ascii_lowercase().as_str() {
                "y" | "yes" => Ok(true),
                "n" | "no" => Ok(false),
                _ => bail!("Enter y or n, or q to cancel"),
            }
        })
    }
}
fn input_path(value: &str) -> Result<PathBuf> {
    let value = value
        .strip_prefix('"')
        .and_then(|value| value.strip_suffix('"'))
        .unwrap_or(value);
    ensure!(!value.trim().is_empty(), "Enter a file path");
    absolute(Path::new(value))
}
fn mappings(value: &str) -> Result<std::collections::BTreeMap<String, String>> {
    let data = if value.starts_with('{') {
        format!("{{\"command\":\"server\",\"languages\":{value}}}")
    } else {
        let mut languages = std::collections::BTreeMap::new();
        for pair in value.split(',') {
            let (extension, language) = pair
                .split_once('=')
                .context("Use .ext=language mappings, for example .py=python, .js=javascript")?;
            let extension = extension.trim().to_ascii_lowercase();
            let extension = if extension.starts_with('.') {
                extension
            } else {
                format!(".{extension}")
            };
            ensure!(
                languages
                    .insert(extension.clone(), language.trim())
                    .is_none(),
                "Duplicate extension {extension}"
            );
        }
        json!({"command":"server","languages":languages}).to_string()
    };
    Ok(Config::parse(data.as_bytes(), &std::env::current_dir()?)?
        .servers
        .remove(0)
        .languages)
}
fn summary(ui: &Ui, path: &Path, config: &Config) {
    println!("  Profile: {}", path.display());
    for server in &config.servers {
        println!("  {}: {}", server.id, server.command);
        ui.hint(
            &server
                .languages
                .iter()
                .map(|(extension, language)| format!("{extension} -> {language}"))
                .collect::<Vec<_>>()
                .join(", "),
        );
    }
}
async fn wizard(mut options: Options, profile: Option<PathBuf>, no_color: bool) -> Result<()> {
    let ui = Ui::new(no_color);
    println!(
        "\n{} {}",
        ui.paint("Sharper", "1;36"),
        env!("CARGO_PKG_VERSION")
    );
    println!("Rapid rule feedback for your coding agent.");
    ui.hint("Choose your language services, then connect your agent. Type q to cancel.");
    ui.step(1, "Choose a language-service profile");
    let mut pending = None;
    let path = if let Some(path) = profile {
        let path = absolute(&path)?;
        Config::load(&path)?;
        ui.hint("Using the supplied profile.");
        path
    } else {
        ui.hint("Enter an existing servers.json path, or choose new to create one.");
        let selected = ui.read("Profile path or new", None, |value| {
            if value.eq_ignore_ascii_case("new") {
                return Ok(None);
            }
            let path = input_path(value)?;
            Config::load(&path)?;
            Ok(Some(path))
        })?;
        if let Some(path) = selected {
            path
        } else {
            let suggested = config_home().ok().map(|path| {
                path.join("sharper/servers.json")
                    .to_string_lossy()
                    .into_owned()
            });
            let path=ui.read("Save profile to",suggested.as_deref(),|value|{
                let path=input_path(value)?;
                ensure!(!path.exists(),"This file already exists. Choose another path, or use it as an existing profile");
                Ok(path)
            })?;
            let command = ui.read("Server executable or runtime", None, |value| {
                let value = value
                    .strip_prefix('"')
                    .and_then(|value| value.strip_suffix('"'))
                    .unwrap_or(value);
                Config::parse(
                    json!({"command":value,"languages":{".example":"example"}})
                        .to_string()
                        .as_bytes(),
                    path.parent().unwrap(),
                )?;
                Ok(value.to_owned())
            })?;
            ui.hint("Arguments are a JSON array, such as [\"--stdio\"] or [\"/path/server.js\", \"--stdio\"].");
            let arguments: Vec<String> = ui.read("Server arguments", Some("[]"), |value| {
                let parsed: Vec<String> = serde_json::from_str(value)
                    .context("Enter a JSON array of strings, for example [\"--stdio\"]")?;
                ensure!(
                    !parsed.iter().any(|value| value.contains('\0')),
                    "Arguments cannot contain NUL"
                );
                Ok(parsed)
            })?;
            ui.hint("Map file extensions to LSP language IDs. Example: .py=python, .js=javascript");
            let languages = ui.read("File mappings", None, mappings)?;
            let data = serde_json::to_string_pretty(
                &json!({"servers":[{"id":"rules","command":command,"arguments":arguments,"languages":languages}]}),
            )?;
            pending = Some(data);
            path
        }
    };
    ui.step(2, "Review your configuration");
    if let Some(data) = pending {
        let config = Config::parse(
            data.as_bytes(),
            path.parent().context("Profile has no parent")?,
        )?;
        summary(&ui, &path, &config);
        println!("\n{data}\n");
        if !ui.confirm("Save this profile? (y/n)")? {
            return Err(Cancelled.into());
        }
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent)?;
        }
        let mut file = std::fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&path)
            .context("Could not create the profile; choose a new destination")?;
        file.write_all(data.as_bytes())?;
        file.sync_all()?;
        ui.success("Profile saved");
    } else {
        summary(&ui, &path, &Config::load(&path)?);
    }
    ui.step(3, "Connect your agent");
    println!(
        "  1  Codex\n  2  Claude Code\n  3  OpenCode\n  4  GitHub Copilot\n  0  Finish without registration"
    );
    let agent = ui.read("Agent number or name", Some("0"), |value| {
        match value.to_ascii_lowercase().as_str() {
            "1" | "codex" => Ok(Some(Agent::Codex)),
            "2" | "claude" | "claude code" => Ok(Some(Agent::Claude)),
            "3" | "opencode" | "open code" => Ok(Some(Agent::Opencode)),
            "4" | "copilot" | "github copilot" => Ok(Some(Agent::Copilot)),
            "0" | "none" | "skip" => Ok(None),
            _ => bail!("Choose 1-4, enter an agent name, or choose 0 to finish"),
        }
    })?;
    let Some(agent) = agent else {
        ui.success("Configuration ready");
        ui.hint(&format!("Profile: {}", path.display()));
        ui.hint("Connect an agent later with sharper setup <agent> --lsp-config <profile>");
        return Ok(());
    };
    if matches!(agent, Agent::Copilot) {
        println!("  1  Copilot CLI\n  2  VS Code\n  3  Both");
        options.client = ui.read(
            "Copilot destination",
            Some(&options.client),
            |value| match value.to_ascii_lowercase().as_str() {
                "1" | "cli" => Ok("cli".to_owned()),
                "2" | "vscode" | "vs code" => Ok("vscode".to_owned()),
                "3" | "both" => Ok("both".to_owned()),
                _ => bail!("Choose cli, vscode or both (or 1, 2, 3)"),
            },
        )?;
    }
    println!(
        "\n  Agent: {}\n  Profile: {}",
        agent.label(),
        path.display()
    );
    if matches!(agent, Agent::Copilot) {
        ui.hint(&format!("Destination: {}", options.client));
    }
    if !ui.confirm("Register Sharper in this agent's user configuration? (y/n)")? {
        ui.success("Configuration ready");
        ui.hint("Your profile is available when you want to connect an agent.");
        return Ok(());
    }
    options.agent = Some(agent);
    // Box breaks the finite run -> wizard -> run asynchronous recursion.
    Box::pin(run(options, Some(path), no_color)).await
}
