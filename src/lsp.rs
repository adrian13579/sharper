use crate::{
    config::{DiagnosticMode, Server},
    normalize,
    rpc::Rpc,
    workspace::{Boundary, Snapshot, contained},
};
use anyhow::{Context, Result, anyhow, ensure};
use serde_json::{Value, json};
use std::{
    collections::BTreeMap,
    path::{Path, PathBuf},
};
#[derive(Debug)]
pub struct Incomplete(pub String);
impl std::fmt::Display for Incomplete {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{}", self.0)
    }
}
impl std::error::Error for Incomplete {}
pub fn uncertain(message: impl Into<String>) -> anyhow::Error {
    Incomplete(message.into()).into()
}
#[derive(Clone, Default)]
struct Document {
    text: String,
    version: i64,
    result_id: Option<String>,
    diagnostics: Vec<Value>,
}
pub struct Session {
    pub profile: Server,
    pub boundary: Boundary,
    pub rpc: Rpc,
    previous: Option<Snapshot>,
    documents: BTreeMap<PathBuf, Document>,
    pub starts: u64,
}
impl Session {
    pub async fn start(profile: Server, root: PathBuf, starts: u64) -> Result<Self> {
        let boundary = Boundary::new(root.clone())?;
        let rpc = Self::connect(&profile, &root).await?;
        Ok(Self {
            profile,
            boundary,
            rpc,
            previous: None,
            documents: BTreeMap::new(),
            starts,
        })
    }
    async fn connect(profile: &Server, root: &Path) -> Result<Rpc> {
        let rpc = Rpc::spawn(profile, root)?;
        let result=rpc.request("initialize",json!({"processId":std::process::id(),"clientInfo":{"name":"Sharper","version":env!("CARGO_PKG_VERSION")},"rootUri":normalize::uri(root)?,"workspaceFolders":[{"uri":normalize::uri(root)?,"name":root.file_name().unwrap_or_default().to_string_lossy()}],"initializationOptions":profile.initialization_options,
            "capabilities":{"general":{"positionEncodings":["utf-16"]},"workspace":{"configuration":true,"workspaceFolders":true,"diagnostics":{"refreshSupport":true},"didChangeWatchedFiles":{"dynamicRegistration":true}},"window":{"workDoneProgress":true},"textDocument":{"synchronization":{"dynamicRegistration":false},"diagnostic":{"dynamicRegistration":true,"relatedDocumentSupport":false},"publishDiagnostics":{"versionSupport":true,"relatedInformation":true}}}})).await?;
        let caps = result
            .get("capabilities")
            .filter(|v| v.is_object())
            .context("Missing LSP capabilities")?;
        ensure!(
            caps["positionEncoding"].is_null() || caps["positionEncoding"] == "utf-16",
            "Only UTF-16 positions are supported"
        );
        {
            let mut meta = rpc.meta.lock().unwrap();
            meta.capabilities = caps.clone();
            meta.server_name = result["serverInfo"]["name"].as_str().map(str::to_owned);
        }
        rpc.notify("initialized", json!({})).await?;
        if !profile.settings.is_null() {
            rpc.notify(
                "workspace/didChangeConfiguration",
                json!({"settings":profile.settings}),
            )
            .await?;
        }
        if profile.ready_notification.is_some() {
            loop {
                let changed = rpc.changed.notified();
                let ready = {
                    let m = rpc.meta.lock().unwrap();
                    if let Some(error) = &m.error {
                        return Err(anyhow!("{error}"));
                    }
                    m.ready
                };
                if ready {
                    break;
                }
                changed.await;
            }
        } else {
            rpc.meta.lock().unwrap().ready = true;
        }
        Ok(rpc)
    }
    pub async fn prepare(&mut self) -> Result<(Snapshot, u64)> {
        ensure!(
            self.rpc.available(),
            "LSP session disconnected; retry to start a new session"
        );
        let mut epoch = self.boundary.revision();
        let mut before = self.boundary.snapshot(&self.profile).await?;
        if self
            .previous
            .as_ref()
            .is_some_and(|old| before.reloads(old, &self.profile))
        {
            self.rpc.close().await;
            self.documents.clear();
            self.previous = None;
            self.rpc = Self::connect(&self.profile, &self.boundary.root).await?;
            self.starts += 1;
            epoch = self.boundary.revision();
            before = self.boundary.snapshot(&self.profile).await?;
        }
        let (open, sync) = self.rpc.meta.lock().unwrap().sync();
        ensure!(
            open && (sync == 1 || sync == 2),
            "Server must support open/close and full or incremental document changes"
        );
        let watched = self
            .rpc
            .meta
            .lock()
            .unwrap()
            .registrations
            .values()
            .any(|r| r["method"] == "workspace/didChangeWatchedFiles");
        if watched && let Some(old) = &self.previous {
            let mut changes = Vec::new();
            for (p, f) in &before.0 {
                if old.0.get(p).is_none_or(|v| v.hash != f.hash) {
                    changes.push(
                        json!({"uri":normalize::uri(p)?,"type":if old.0.contains_key(p){2}else{1}}),
                    );
                }
            }
            for p in old.0.keys().filter(|p| !before.0.contains_key(*p)) {
                changes.push(json!({"uri":normalize::uri(p)?,"type":3}));
            }
            if !changes.is_empty() {
                self.rpc
                    .notify(
                        "workspace/didChangeWatchedFiles",
                        json!({"changes":changes}),
                    )
                    .await?;
            }
        }
        for (path, input) in &before.0 {
            let Some(text) = &input.text else { continue };
            let uri = normalize::uri(path)?;
            if let Some(document) = self.documents.get_mut(path) {
                if &document.text != text {
                    document.version = document
                        .version
                        .checked_add(1)
                        .context("Document version overflow")?;
                    let change = if sync == 1 {
                        json!({"text":text})
                    } else {
                        json!({"range":{"start":{"line":0,"character":0},"end":end(&document.text)},"text":text})
                    };
                    self.rpc.notify("textDocument/didChange",json!({"textDocument":{"uri":uri,"version":document.version},"contentChanges":[change]})).await?;
                    document.text = text.clone();
                }
            } else {
                self.rpc.notify("textDocument/didOpen",json!({"textDocument":{"uri":uri,"languageId":self.profile.language(path),"version":1,"text":text}})).await?;
                self.documents.insert(
                    path.clone(),
                    Document {
                        text: text.clone(),
                        version: 1,
                        ..Default::default()
                    },
                );
            }
        }
        self.previous = Some(before.clone());
        Ok((before, epoch))
    }
    pub async fn stable(&self, before: &Snapshot, epoch: u64) -> Result<()> {
        let after = self.boundary.snapshot(&self.profile).await?;
        if before != &after || epoch != self.boundary.revision() {
            return Err(uncertain(
                "Workspace inputs changed during analysis; run the request again",
            ));
        }
        ensure!(
            self.rpc.available(),
            "Language server disconnected during analysis"
        );
        if let Some(error) = &self.rpc.meta.lock().unwrap().error {
            return Err(uncertain(error.clone()));
        }
        Ok(())
    }
    pub async fn check(&mut self, requested: &Path, findings: &mut Vec<Value>) -> Result<()> {
        let (before, epoch) = self.prepare().await?;
        let selected: Vec<PathBuf> = before
            .0
            .keys()
            .filter(|p| self.profile.supports(p) && contained(requested, p))
            .cloned()
            .collect();
        ensure!(!selected.is_empty(), "No configured source files in scope");
        let revision = self.rpc.meta.lock().unwrap().revision;
        if self.profile.diagnostics == DiagnosticMode::Pull {
            if selected.iter().any(|p| {
                !self
                    .rpc
                    .meta
                    .lock()
                    .unwrap()
                    .diagnostics(self.profile.language(p).unwrap())
            }) {
                return Err(uncertain(
                    "Server does not advertise document-pull diagnostics for every selected language; push-only feedback cannot verify",
                ));
            }
        } else {
            ensure!(
                self.rpc.meta.lock().unwrap().capabilities["executeCommandProvider"]["commands"]
                    .as_array()
                    .is_some_and(|a| a.iter().any(|v| v == "typescript.tsserverRequest")),
                "Server does not support the configured synchronous TypeScript adapter"
            );
        }
        let mut stable_refresh = false;
        for _ in 0..2 {
            let refresh = self.rpc.meta.lock().unwrap().refresh;
            findings.clear();
            for path in &selected {
                let uri = normalize::uri(path)?;
                if self.profile.diagnostics == DiagnosticMode::TypeScript {
                    findings.extend(self.typescript(path).await?);
                    continue;
                }
                let document = self
                    .documents
                    .get_mut(path)
                    .context("Document was not synchronized")?;
                let report = self
                    .rpc
                    .request(
                        "textDocument/diagnostic",
                        json!({"textDocument":{"uri":uri},"previousResultId":document.result_id}),
                    )
                    .await?;
                let id = report
                    .get("resultId")
                    .filter(|v| !v.is_null())
                    .map(|v| v.as_str().context("Invalid resultId"))
                    .transpose()?
                    .map(str::to_owned);
                match report["kind"].as_str() {
                    Some("full") => {
                        document.diagnostics = normalize::diagnostics(&report["items"], &uri)?
                    }
                    Some("unchanged") => ensure!(
                        id.is_some() && id == document.result_id,
                        "Invalid or uncached unchanged report"
                    ),
                    _ => anyhow::bail!("Invalid diagnostic report kind"),
                };
                document.result_id = id;
                findings.extend(document.diagnostics.clone());
                if self.profile.require_result_id && document.result_id.is_none() {
                    return Err(uncertain(
                        "Server did not identify a recognized document report; check project membership and loading",
                    ));
                }
            }
            if refresh == self.rpc.meta.lock().unwrap().refresh {
                stable_refresh = true;
                break;
            }
        }
        self.stable(&before, epoch).await?;
        if !stable_refresh {
            return Err(uncertain(
                "Server requested repeated diagnostic refresh; recheck",
            ));
        }
        if revision != self.rpc.meta.lock().unwrap().revision {
            return Err(uncertain("Capabilities changed during analysis; recheck"));
        }
        Ok(())
    }
    async fn typescript(&self, path: &Path) -> Result<Vec<Value>> {
        let mut findings = Vec::new();
        for command in ["syntacticDiagnosticsSync", "semanticDiagnosticsSync"] {
            let result=self.rpc.request("workspace/executeCommand",json!({"command":"typescript.tsserverRequest","arguments":[command,{"file":normalize::uri(path)?,"includeLinePosition":true},{"executionTarget":0,"expectsResult":true,"isAsync":false,"lowPriority":false}]})).await?;
            ensure!(
                result["success"] == true,
                "Synchronous diagnostic adapter failed"
            );
            for item in result["body"]
                .as_array()
                .context("Invalid adapter diagnostics")?
            {
                fn pos(value: &Value) -> Result<Value> {
                    let line = value["line"].as_u64().context("Invalid adapter line")?;
                    let offset = value["offset"].as_u64().context("Invalid adapter offset")?;
                    ensure!(line > 0 && offset > 0, "Invalid one-based adapter position");
                    Ok(json!({"line":line-1,"character":offset-1}))
                }
                let severity = match item["category"].as_str() {
                    Some("error") => 1,
                    Some("warning") => 2,
                    Some("message") => 3,
                    Some("suggestion") => 4,
                    _ => anyhow::bail!("Unknown adapter severity"),
                };
                let mut converted = json!({"code":item["code"],"message":item["message"],"severity":severity,"range":{"start":pos(&item["startLocation"])?,"end":pos(&item["endLocation"])?}});
                let mut tags = Vec::new();
                if item.get("reportsUnnecessary").is_some() {
                    tags.push(1)
                }
                if item.get("reportsDeprecated").is_some() {
                    tags.push(2)
                }
                if !tags.is_empty() {
                    converted["tags"] = json!(tags);
                }
                let mut values =
                    normalize::diagnostics(&json!([converted]), &normalize::uri(path)?)?;
                if let Some(related) = item["relatedInformation"].as_array() {
                    let mut all = Vec::new();
                    for info in related {
                        let mut r = json!({"message":info["message"],"filePath":null,"startLine":null,"startColumn":null,"endLine":null,"endColumn":null});
                        if let Some(span) = info.get("span") {
                            let start = pos(&span["start"])?;
                            let end = pos(&span["end"])?;
                            let file = span["file"].as_str().context("Invalid related file")?;
                            r["filePath"] = json!(crate::workspace::clean(
                                std::fs::canonicalize(file).unwrap_or_else(|_| PathBuf::from(file))
                            ));
                            r["startLine"] = start["line"].clone();
                            r["startColumn"] = start["character"].clone();
                            r["endLine"] = end["line"].clone();
                            r["endColumn"] = end["character"].clone();
                        }
                        all.push(r);
                    }
                    values[0]["relatedInformation"] = json!(all);
                }
                findings.extend(values);
            }
        }
        Ok(normalize::ordered(findings))
    }
    pub fn status(&self) -> Value {
        let m = self.rpc.meta.lock().unwrap();
        let languages: Vec<_> = self.profile.languages.values().collect();
        let diagnostics = if self.profile.diagnostics == DiagnosticMode::TypeScript {
            m.capabilities["executeCommandProvider"]["commands"]
                .as_array()
                .is_some_and(|a| a.iter().any(|v| v == "typescript.tsserverRequest"))
        } else {
            languages.iter().all(|lang| m.diagnostics(lang))
        };
        json!({"backend":self.profile.id,"available":self.rpc.available(),"workspaceState":if self.rpc.available()&&m.ready{"ready"}else{"failed"},"workspace":self.boundary.root,"diagnosticsAvailable":diagnostics,"lastError":m.error,"sessionStarts":self.starts,"processId":self.rpc.pid(),"serverName":m.server_name,"workspaceDiagnosticCapability":m.capabilities["diagnosticProvider"]["workspaceDiagnostics"]==true})
    }
    pub async fn close(&mut self) {
        let _ = tokio::time::timeout(std::time::Duration::from_millis(500), async {
            for path in self.documents.keys() {
                if let Ok(uri) = normalize::uri(path) {
                    let _ = self
                        .rpc
                        .notify("textDocument/didClose", json!({"textDocument":{"uri":uri}}))
                        .await;
                }
            }
        })
        .await;
        self.rpc.close().await;
    }
}
fn end(text: &str) -> Value {
    let text = text.replace("\r\n", "\n").replace('\r', "\n");
    let mut lines = text.split('\n');
    let last = lines.next_back().unwrap_or("");
    json!({"line":lines.count(),"character":last.encode_utf16().count()})
}
