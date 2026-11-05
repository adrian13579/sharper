use crate::{
    config::Server,
    framing::{read_lsp, write_lsp},
};
use anyhow::{Context, Result, anyhow, ensure};
use process_wrap::tokio::*;
use serde_json::{Value, json};
use std::{
    collections::{BTreeMap, HashMap},
    path::Path,
    process::Stdio,
    sync::{
        Arc, Mutex as StdMutex,
        atomic::{AtomicBool, AtomicU64, Ordering},
    },
};
use tokio::{
    io::{AsyncReadExt, BufReader},
    process::ChildStdin,
    sync::{Mutex, Notify, oneshot},
    task::JoinHandle,
};
pub struct Meta {
    pub capabilities: Value,
    pub registrations: BTreeMap<String, Value>,
    pub refresh: u64,
    pub revision: u64,
    pub ready: bool,
    pub error: Option<String>,
    pub server_name: Option<String>,
}
impl Default for Meta {
    fn default() -> Self {
        Self {
            capabilities: json!({}),
            registrations: BTreeMap::new(),
            refresh: 0,
            revision: 0,
            ready: false,
            error: None,
            server_name: None,
        }
    }
}
impl Meta {
    pub fn diagnostics(&self, language: &str) -> bool {
        let value = &self.capabilities["diagnosticProvider"];
        if value == &Value::Bool(true) || value.is_object() {
            return true;
        }
        self.registrations.values().any(|reg| {
            reg["method"] == "textDocument/diagnostic"
                && selector(&reg["registerOptions"]["documentSelector"], language)
        })
    }
    pub fn sync(&self) -> (bool, u64) {
        let v = &self.capabilities["textDocumentSync"];
        if let Some(n) = v.as_u64() {
            (n != 0, n)
        } else {
            (v["openClose"] == true, v["change"].as_u64().unwrap_or(0))
        }
    }
}
fn selector(value: &Value, language: &str) -> bool {
    value.is_null()
        || value.as_array().is_some_and(|items| {
            items.iter().any(|s| {
                s.is_object()
                    && (s["language"].is_null() || s["language"] == language)
                    && (s["scheme"].is_null() || s["scheme"] == "file")
                    && s["pattern"].is_null()
            })
        })
}
type Pending = Arc<StdMutex<HashMap<u64, oneshot::Sender<std::result::Result<Value, String>>>>>;
pub struct Rpc {
    child: Box<dyn ChildWrapper>,
    writer: Arc<Mutex<ChildStdin>>,
    pending: Pending,
    next: AtomicU64,
    pub meta: Arc<StdMutex<Meta>>,
    pub changed: Arc<Notify>,
    pub alive: Arc<AtomicBool>,
    stderr: Arc<StdMutex<Vec<u8>>>,
    reader: JoinHandle<()>,
    stderr_reader: JoinHandle<()>,
}
impl Rpc {
    pub fn spawn(server: &Server, root: &Path) -> Result<Self> {
        let mut command = CommandWrap::with_new(&server.command, |cmd| {
            cmd.args(&server.arguments)
                .current_dir(root)
                .stdin(Stdio::piped())
                .stdout(Stdio::piped())
                .stderr(Stdio::piped());
        });
        command.wrap(KillOnDrop);
        #[cfg(unix)]
        command.wrap(ProcessGroup::leader());
        #[cfg(windows)]
        {
            command.wrap(CreationFlags(
                windows::Win32::System::Threading::CREATE_NO_WINDOW,
            ));
            command.wrap(JobObject);
        }
        let mut child = command.spawn().with_context(|| {
            format!("Cannot start LSP server {} ({})", server.id, server.command)
        })?;
        let writer = Arc::new(Mutex::new(
            child.stdin().take().context("Missing child stdin")?,
        ));
        let stdout = child.stdout().take().context("Missing child stdout")?;
        let mut stderr_pipe = child.stderr().take().context("Missing child stderr")?;
        let stderr = Arc::new(StdMutex::new(Vec::new()));
        let captured = stderr.clone();
        let stderr_reader = tokio::spawn(async move {
            let mut bytes = [0; 1024];
            while let Ok(n) = stderr_pipe.read(&mut bytes).await {
                if n == 0 {
                    break;
                }
                let mut log = captured.lock().unwrap();
                log.extend_from_slice(&bytes[..n]);
                let extra = log.len().saturating_sub(8192);
                log.drain(..extra);
            }
        });
        let pending: Pending = Arc::new(StdMutex::new(HashMap::new()));
        let waiting = pending.clone();
        let meta = Arc::new(StdMutex::new(Meta::default()));
        let state = meta.clone();
        let w = writer.clone();
        let alive = Arc::new(AtomicBool::new(true));
        let live = alive.clone();
        let changed = Arc::new(Notify::new());
        let notify = changed.clone();
        let profile = server.clone();
        let workspace = crate::normalize::uri(root)?;
        let name = root
            .file_name()
            .unwrap_or_default()
            .to_string_lossy()
            .into_owned();
        let reader = tokio::spawn(async move {
            let mut input = BufReader::new(stdout);
            let failure = loop {
                let msg = match read_lsp(&mut input).await {
                    Ok(Some(v)) => v,
                    Ok(None) => break "Language server disconnected".to_owned(),
                    Err(e) => break format!("Invalid LSP transport: {e}"),
                };
                if let Some(method) = msg["method"].as_str() {
                    let result =
                        handle(method, &msg["params"], &profile, &workspace, &name, &state);
                    notify.notify_waiters();
                    if let Some(id) = msg.get("id") {
                        let reply = match result {
                            Ok(value) => json!({"jsonrpc":"2.0","id":id,"result":value}),
                            Err((code, error)) => {
                                json!({"jsonrpc":"2.0","id":id,"error":{"code":code,"message":error}})
                            }
                        };
                        if write_lsp(&mut *w.lock().await, &reply).await.is_err() {
                            break "Cannot respond to language server".into();
                        }
                    }
                } else if let Some(id) = msg["id"].as_u64()
                    && let Some(sender) = waiting.lock().unwrap().remove(&id)
                {
                    let result = if let Some(error) = msg.get("error") {
                        Err(format!("LSP request failed: {error}"))
                    } else {
                        msg.get("result")
                            .cloned()
                            .ok_or_else(|| "Missing LSP result".into())
                    };
                    let _ = sender.send(result);
                }
            };
            live.store(false, Ordering::SeqCst);
            state.lock().unwrap().error.get_or_insert(failure.clone());
            notify.notify_waiters();
            for (_, sender) in waiting.lock().unwrap().drain() {
                let _ = sender.send(Err(failure.clone()));
            }
        });
        Ok(Self {
            child,
            writer,
            pending,
            next: AtomicU64::new(1),
            meta,
            changed,
            alive,
            stderr,
            reader,
            stderr_reader,
        })
    }
    pub fn pid(&self) -> Option<u32> {
        self.child.id()
    }
    pub fn available(&self) -> bool {
        self.alive.load(Ordering::SeqCst)
    }
    pub async fn request(&self, method: &str, params: Value) -> Result<Value> {
        ensure!(
            self.available(),
            "Language server disconnected: {}",
            self.stderr()
        );
        let id = self.next.fetch_add(1, Ordering::SeqCst);
        let (tx, rx) = oneshot::channel();
        self.pending.lock().unwrap().insert(id, tx);
        if let Err(e) = write_lsp(
            &mut *self.writer.lock().await,
            &json!({"jsonrpc":"2.0","id":id,"method":method,"params":params}),
        )
        .await
        {
            self.pending.lock().unwrap().remove(&id);
            return Err(e);
        }
        rx.await
            .context("LSP request channel closed")?
            .map_err(|e| anyhow!("{e}. {}", self.stderr()))
    }
    pub async fn notify(&self, method: &str, params: Value) -> Result<()> {
        write_lsp(
            &mut *self.writer.lock().await,
            &json!({"jsonrpc":"2.0","method":method,"params":params}),
        )
        .await
    }
    pub fn stderr(&self) -> String {
        String::from_utf8_lossy(&self.stderr.lock().unwrap()).into_owned()
    }
    pub async fn close(&mut self) {
        let _ = tokio::time::timeout(std::time::Duration::from_millis(500), async {
            let _ = self.request("shutdown", Value::Null).await;
            let _ = self.notify("exit", Value::Null).await;
        })
        .await;
        let _ = tokio::time::timeout(
            std::time::Duration::from_secs(1),
            std::pin::Pin::from(self.child.kill()),
        )
        .await;
        self.reader.abort();
        self.stderr_reader.abort();
    }
}
impl Drop for Rpc {
    fn drop(&mut self) {
        let _ = self.child.start_kill();
        self.reader.abort();
        self.stderr_reader.abort();
    }
}
fn handle(
    method: &str,
    p: &Value,
    server: &Server,
    workspace: &str,
    name: &str,
    state: &StdMutex<Meta>,
) -> std::result::Result<Value, (i64, String)> {
    let mut meta = state.lock().unwrap();
    if server.ready_notification.as_deref() == Some(method) {
        meta.ready = true;
        return Ok(Value::Null);
    }
    match method {
        "client/registerCapability" => {
            let Some(items) = p["registrations"].as_array() else {
                return Err((-32602, "Missing registrations".into()));
            };
            for item in items {
                let Some(id) = item["id"].as_str() else {
                    return Err((-32602, "Missing registration id".into()));
                };
                meta.registrations.insert(id.into(), item.clone());
            }
            meta.revision += 1;
        }
        "client/unregisterCapability" => {
            let Some(items) = p
                .get("unregisterations")
                .or_else(|| p.get("unregistrations"))
                .and_then(Value::as_array)
            else {
                return Err((-32602, "Missing unregistrations".into()));
            };
            for item in items {
                if let Some(id) = item["id"].as_str() {
                    meta.registrations.remove(id);
                }
            }
            meta.revision += 1;
        }
        "workspace/configuration" => {
            return Ok(Value::Array(
                p["items"]
                    .as_array()
                    .ok_or((-32602, "Missing configuration items".into()))?
                    .iter()
                    .map(|item| server.section(item["section"].as_str()))
                    .collect(),
            ));
        }
        "workspace/workspaceFolders" => return Ok(json!([{"uri":workspace,"name":name}])),
        "workspace/applyEdit" => {
            return Ok(json!({"applied":false,"failureReason":"Sharper is read-only"}));
        }
        "workspace/diagnostic/refresh" => meta.refresh += 1,
        "window/logMessage" | "window/showMessage" | "window/showMessageRequest" => {
            if p["type"] == 1 {
                meta.error = Some(
                    p["message"]
                        .as_str()
                        .unwrap_or("Language server reported an error")
                        .into(),
                );
            }
        }
        "eslint/noConfig" | "eslint/noLibrary" | "eslint/probeFailed" => {
            meta.error = Some(format!("Server cannot validate documents: {method}"))
        }
        "eslint/status" => {
            if p["state"] != 1 {
                meta.error = Some("ESLint validation is unavailable".into());
            }
        }
        "eslint/openDoc"
        | "window/workDoneProgress/create"
        | "$/progress"
        | "telemetry/event"
        | "textDocument/publishDiagnostics" => {}
        _ => return Err((-32601, format!("Unsupported client method: {method}"))),
    }
    Ok(Value::Null)
}
