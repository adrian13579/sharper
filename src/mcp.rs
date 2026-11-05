use crate::{
    engine::{Args, Engine},
    framing::MAX_MESSAGE,
};
use anyhow::{Context, Result, ensure};
use base64::{Engine as _, engine::general_purpose::STANDARD};
use serde_json::{Value, json};
use std::{
    collections::HashMap,
    sync::{
        Arc, Mutex as StdMutex,
        atomic::{AtomicU64, Ordering},
    },
    time::Duration,
};
use tokio::{
    io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader},
    sync::{Mutex, oneshot, watch},
    task::JoinSet,
    time::{Instant, timeout_at},
};
type Output = Arc<Mutex<tokio::io::Stdout>>;
type Roots = Arc<StdMutex<HashMap<String, oneshot::Sender<Value>>>>;
async fn send(output: &Output, value: &Value) -> Result<()> {
    let mut bytes = serde_json::to_vec(value)?;
    bytes.push(b'\n');
    let mut out = output.lock().await;
    out.write_all(&bytes).await?;
    out.flush().await?;
    Ok(())
}
fn error(id: Value, code: i64, message: impl Into<String>) -> Value {
    json!({"jsonrpc":"2.0","id":id,"error":{"code":code,"message":message.into()}})
}
fn reply(id: Value, result: Value) -> Value {
    json!({"jsonrpc":"2.0","id":id,"result":result})
}
fn tool_result(value: Value) -> Value {
    json!({"content":[{"type":"text","text":value.to_string()}],"structuredContent":value,"isError":false})
}
fn tool_error(message: String) -> Value {
    json!({"content":[{"type":"text","text":message}],"isError":true})
}
fn stopped(status: &str) -> Value {
    tool_result(
        json!({"status":status,"complete":false,"freshness":"uncertain","errors":[format!("Request {status}; no result is verified")]}),
    )
}
fn server_info() -> Value {
    json!({"name":"sharper","title":"Sharper","version":env!("CARGO_PKG_VERSION"),"icons":[{"src":format!("data:image/png;base64,{}",STANDARD.encode(include_bytes!("../assets/mcp-icon-light.png"))),"mimeType":"image/png","sizes":["128x128"],"theme":"light"},{"src":format!("data:image/png;base64,{}",STANDARD.encode(include_bytes!("../assets/mcp-icon-dark.png"))),"mimeType":"image/png","sizes":["128x128"],"theme":"dark"}]})
}
pub fn tools() -> Value {
    let mut tools = Vec::new();
    for (name, description) in [
        (
            "sharper_check",
            "Get compiler and rule findings from configured LSP services. Pass workspace and a file or directory path; omit path for mapped workspace documents. Inspect complete, freshness and diagnostics.",
        ),
        (
            "sharper_status",
            "Inspect configured language-server availability and capabilities. Health is not diagnostic verification.",
        ),
        (
            "sharper_definition",
            "Find definitions through a configured server advertising this LSP capability. Positions are zero-based UTF-16. Read-only; external/generated targets may be returned.",
        ),
        (
            "sharper_implementation",
            "Find implementations through a configured LSP server. Positions are zero-based UTF-16. Read-only.",
        ),
        (
            "sharper_references",
            "Find references through a configured LSP server, optionally including declarations. Positions are zero-based UTF-16. Read-only.",
        ),
    ] {
        let mut properties = json!({"workspace":{"type":"string","description":"Absolute repository directory; required unless absolute source path, a single client root, or fallback workspace selects it."}});
        let mut required = Vec::new();
        if name != "sharper_status" {
            properties["path"] = json!({"type":"string","description":"Source file or directory, absolute or relative to workspace."});
        }
        if !matches!(name, "sharper_check" | "sharper_status") {
            required.extend(["path", "line", "character"]);
            for key in ["line", "character"] {
                properties[key] = json!({"type":"integer","minimum":0,"maximum":2147483647,"description":"Zero-based UTF-16 position."});
            }
        }
        if name == "sharper_references" {
            properties["includeDeclaration"] = json!({"type":"boolean","default":false});
        }
        tools.push(json!({"name":name,"description":description,"inputSchema":{"type":"object","properties":properties,"required":required,"additionalProperties":false},"outputSchema":{"type":"object"},"annotations":{"readOnlyHint":true,"destructiveHint":false,"idempotentHint":true,"openWorldHint":false}}));
    }
    json!({"tools":tools})
}
struct RootRequest {
    id: String,
    pending: Roots,
}
impl Drop for RootRequest {
    fn drop(&mut self) {
        self.pending.lock().unwrap().remove(&self.id);
    }
}
async fn roots_args(
    mut args: Args,
    enabled: bool,
    output: &Output,
    pending: &Roots,
    sequence: &AtomicU64,
) -> Result<Args> {
    if args.workspace.is_some()
        || args
            .path
            .as_deref()
            .is_some_and(|p| std::path::Path::new(p).is_absolute())
        || !enabled
    {
        return Ok(args);
    }
    let id = format!("sharper-roots-{}", sequence.fetch_add(1, Ordering::SeqCst));
    let (tx, rx) = oneshot::channel();
    pending.lock().unwrap().insert(id.clone(), tx);
    let _cleanup = RootRequest {
        id: id.clone(),
        pending: pending.clone(),
    };
    send(
        output,
        &json!({"jsonrpc":"2.0","id":id,"method":"roots/list"}),
    )
    .await?;
    let result = tokio::time::timeout(Duration::from_secs(5), rx).await;
    pending.lock().unwrap().remove(&id);
    let message =
        result.context("Client workspace roots timed out; pass workspace explicitly")??;
    ensure!(
        message.get("error").is_none(),
        "Client could not provide roots; pass workspace explicitly"
    );
    let roots = message["result"]["roots"]
        .as_array()
        .context("Malformed client roots")?;
    let mut paths = Vec::new();
    for root in roots {
        let uri = root["uri"].as_str().context("Malformed client root URI")?;
        if let Some(path) = crate::normalize::file(uri)
            && !paths.contains(&path)
        {
            paths.push(path);
        }
    }
    ensure!(
        paths.len() <= 1,
        "Multiple workspace roots; pass workspace explicitly"
    );
    if let Some(path) = paths.pop() {
        args.workspace = Some(path.to_string_lossy().into_owned());
    }
    Ok(args)
}
struct ClientConnection {
    roots_enabled: bool,
    output: Output,
    roots: Roots,
    sequence: Arc<AtomicU64>,
}
async fn call(
    engine: Arc<Mutex<Engine>>,
    name: String,
    args: Args,
    mut cancel: watch::Receiver<bool>,
    deadline: Instant,
    connection: ClientConnection,
) -> Value {
    let ClientConnection {
        roots_enabled,
        output,
        roots,
        sequence,
    } = connection;
    let args = tokio::select! {_=cancel.changed()=>return stopped("cancelled"),result=timeout_at(deadline,roots_args(args,roots_enabled,&output,&roots,&sequence))=>match result{Ok(Ok(args))=>args,Ok(Err(error))=>return tool_error(format!("{error:#}")),Err(_)=>return stopped("timedOut")}};
    let mut guard = tokio::select! {_=cancel.changed()=>return stopped("cancelled"),result=timeout_at(deadline,engine.lock())=>match result{Ok(g)=>g,Err(_)=>return stopped("timedOut")}};
    let status = tokio::select! {_=cancel.changed()=>"cancelled",r=timeout_at(deadline,guard.call(&name,args))=>match r{Ok(value)=>return match value{Ok(value)=>tool_result(value),Err(error)=>tool_error(format!("{error:#}"))},Err(_)=>"timedOut"}};
    guard.close().await;
    stopped(status)
}
pub async fn serve(engine: Engine, timeout: Duration) -> Result<()> {
    let engine = Arc::new(Mutex::new(engine));
    let output = Arc::new(Mutex::new(tokio::io::stdout()));
    let mut input = BufReader::new(tokio::io::stdin());
    let cancellations = Arc::new(StdMutex::new(HashMap::<String, watch::Sender<bool>>::new()));
    let roots: Roots = Arc::new(StdMutex::new(HashMap::new()));
    let sequence = Arc::new(AtomicU64::new(1));
    let mut jobs = JoinSet::new();
    let mut initialized = false;
    let mut roots_enabled = false;
    let shutdown = shutdown_signal();
    tokio::pin!(shutdown);
    loop {
        let mut bytes = Vec::new();
        let mut limited = (&mut input).take((MAX_MESSAGE + 1) as u64);
        let count =
            tokio::select! {_= &mut shutdown=>break,n=limited.read_until(b'\n',&mut bytes)=>n?};
        if count == 0 {
            break;
        }
        if count > MAX_MESSAGE {
            let _ = send(
                &output,
                &error(Value::Null, -32700, "MCP message exceeds limit"),
            )
            .await;
            break;
        }
        let message: Value = match serde_json::from_slice(&bytes) {
            Ok(v) => v,
            Err(_) => {
                send(&output, &error(Value::Null, -32700, "Invalid JSON")).await?;
                continue;
            }
        };
        if message["jsonrpc"] != "2.0" || !message.is_object() {
            send(
                &output,
                &error(Value::Null, -32600, "Invalid JSON-RPC request"),
            )
            .await?;
            continue;
        }
        let Some(method) = message["method"].as_str() else {
            if let Some(id) = message["id"].as_str()
                && let Some(tx) = roots.lock().unwrap().remove(id)
            {
                let _ = tx.send(message);
            }
            continue;
        };
        if method == "notifications/cancelled" {
            if let Some(tx) = cancellations
                .lock()
                .unwrap()
                .get(&message["params"]["requestId"].to_string())
            {
                let _ = tx.send(true);
            }
            continue;
        }
        let Some(id) = message.get("id").cloned() else {
            continue;
        };
        if !id.is_string() && !id.is_i64() && !id.is_u64() {
            send(&output, &error(Value::Null, -32600, "Invalid request id")).await?;
            continue;
        }
        let result = match method {
            "initialize" => {
                if initialized {
                    send(&output, &error(id, -32600, "Already initialized")).await?;
                    continue;
                }
                roots_enabled = message["params"]["capabilities"].get("roots").is_some();
                initialized = true;
                let requested = message["params"]["protocolVersion"].as_str().unwrap_or("");
                let version = if ["2025-03-26", "2025-06-18", "2025-11-25"].contains(&requested) {
                    requested
                } else {
                    "2025-11-25"
                };
                json!({"protocolVersion":version,"serverInfo":server_info(),"capabilities":{"tools":{"listChanged":false}},"instructions":"Use sharper_check before and after meaningful edits. Pass workspace and affected scope. Inspect findings separately from analysis completion/freshness. Run builds/tests separately. Language support is configured through LSP profiles."})
            }
            "ping" => json!({}),
            "tools/list" if initialized => tools(),
            "tools/call" if initialized => {
                let Some(name) = message["params"]["name"].as_str() else {
                    send(&output, &error(id, -32602, "Tool name is required")).await?;
                    continue;
                };
                if ![
                    "sharper_check",
                    "sharper_status",
                    "sharper_definition",
                    "sharper_implementation",
                    "sharper_references",
                ]
                .contains(&name)
                {
                    send(&output, &error(id, -32602, "Unknown tool")).await?;
                    continue;
                }
                let args: Args = match serde_json::from_value(
                    message["params"]
                        .get("arguments")
                        .cloned()
                        .unwrap_or_else(|| json!({})),
                ) {
                    Ok(args) => args,
                    Err(e) => {
                        send(&output, &error(id, -32602, e.to_string())).await?;
                        continue;
                    }
                };
                let key = id.to_string();
                let (tx, rx) = watch::channel(false);
                let accepted = {
                    let mut active = cancellations.lock().unwrap();
                    if active.len() >= 32 || active.contains_key(&key) {
                        false
                    } else {
                        active.insert(key.clone(), tx);
                        true
                    }
                };
                if !accepted {
                    send(
                        &output,
                        &error(id, -32600, "Too many active requests or duplicate id"),
                    )
                    .await?;
                    continue;
                }
                let pool = engine.clone();
                let out = output.clone();
                let active = cancellations.clone();
                let r = roots.clone();
                let seq = sequence.clone();
                let name = name.to_owned();
                let deadline = Instant::now() + timeout;
                jobs.spawn(async move {
                    let value = call(
                        pool,
                        name,
                        args,
                        rx,
                        deadline,
                        ClientConnection {
                            roots_enabled,
                            output: out.clone(),
                            roots: r,
                            sequence: seq,
                        },
                    )
                    .await;
                    active.lock().unwrap().remove(&key);
                    let _ = send(&out, &reply(id, value)).await;
                });
                while jobs.try_join_next().is_some() {}
                continue;
            }
            _ => {
                send(
                    &output,
                    &error(id, -32601, "Unsupported method or server not initialized"),
                )
                .await?;
                continue;
            }
        };
        send(&output, &reply(id, result)).await?;
    }
    for tx in cancellations.lock().unwrap().values() {
        let _ = tx.send(true);
    }
    for (_, tx) in roots.lock().unwrap().drain() {
        let _ = tx.send(json!({"error":{"message":"Client disconnected"}}));
    }
    while jobs.join_next().await.is_some() {}
    engine.lock().await.close().await;
    Ok(())
}

async fn shutdown_signal() {
    #[cfg(unix)]
    {
        if let Ok(mut terminate) =
            tokio::signal::unix::signal(tokio::signal::unix::SignalKind::terminate())
        {
            tokio::select! {_=tokio::signal::ctrl_c()=>{},_=terminate.recv()=>{}};
            return;
        }
    }
    let _ = tokio::signal::ctrl_c().await;
}
