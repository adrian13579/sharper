use crate::{
    config::{Config, Server},
    lsp::{Incomplete, Session},
    normalize,
    workspace::{Boundary, contained, resolve},
};
use anyhow::{Result, ensure};
use serde::Deserialize;
use serde_json::{Value, json};
use std::{
    collections::BTreeMap,
    path::{Path, PathBuf},
    time::Instant,
};
#[derive(Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Args {
    pub workspace: Option<String>,
    pub path: Option<String>,
}
struct Entry {
    session: Session,
    used: u64,
}
pub struct Engine {
    pub profiles: Vec<Server>,
    fallback: Option<PathBuf>,
    entries: BTreeMap<String, Entry>,
    sequence: u64,
    starts: u64,
}
impl Engine {
    pub fn new(config: Option<Config>, fallback: Option<PathBuf>) -> Self {
        Self {
            profiles: config.map(|c| c.servers).unwrap_or_default(),
            fallback,
            entries: BTreeMap::new(),
            sequence: 0,
            starts: 0,
        }
    }
    async fn session(&mut self, profile: &Server, root: &Path) -> Result<&mut Session> {
        let key = format!("{}\0{}", root.display(), profile.id);
        if self
            .entries
            .get(&key)
            .is_some_and(|e| !e.session.rpc.available())
            && let Some(mut entry) = self.entries.remove(&key)
        {
            entry.session.close().await;
        }
        if !self.entries.contains_key(&key) {
            if self.entries.len() == 4 {
                let oldest = self
                    .entries
                    .iter()
                    .min_by_key(|(_, e)| e.used)
                    .map(|(k, _)| k.clone())
                    .unwrap();
                if let Some(mut e) = self.entries.remove(&oldest) {
                    e.session.close().await;
                }
            }
            self.starts += 1;
            let session = Session::start(profile.clone(), root.to_owned(), self.starts).await?;
            self.entries.insert(key.clone(), Entry { session, used: 0 });
        }
        self.sequence += 1;
        let entry = self.entries.get_mut(&key).unwrap();
        entry.used = self.sequence;
        Ok(&mut entry.session)
    }
    pub async fn close(&mut self) {
        for (_, mut e) in std::mem::take(&mut self.entries) {
            e.session.close().await;
        }
    }
    pub async fn call(&mut self, name: &str, args: Args) -> Result<Value> {
        ensure!(
            !self.profiles.is_empty(),
            "Configure language servers with --lsp-config /absolute/path/servers.json; see the custom LSP guide"
        );
        let (root, requested) = resolve(
            args.workspace.as_deref(),
            args.path.as_deref(),
            self.fallback.as_deref(),
        )?;
        let result = match name {
            "sharper_check" => self.check(&root, &requested).await,
            "sharper_status" => self.status(&root).await,
            _ => anyhow::bail!("Unknown tool {name}"),
        }?;
        Ok(compact_result(result))
    }
    async fn check(&mut self, root: &Path, requested: &Path) -> Result<Value> {
        let start = Instant::now();
        let scope = if requested.is_file() {
            "file"
        } else {
            "documents"
        };
        let boundary = Boundary::new(root.to_owned())?;
        let discovery = boundary.snapshot(&self.profiles[0]).await?;
        let profiles: Vec<_> = self
            .profiles
            .iter()
            .filter(|s| {
                discovery
                    .0
                    .keys()
                    .any(|p| contained(requested, p) && s.supports(p))
            })
            .cloned()
            .collect();
        ensure!(
            !profiles.is_empty(),
            "No configured source files in requested scope. Add extension mappings to your LSP configuration"
        );
        // Initialize first so server-owned startup outputs do not become the first baseline.
        let mut startup = BTreeMap::new();
        for profile in &profiles {
            if let Err(error) = self.session(profile, root).await {
                startup.insert(profile.id.clone(), error.to_string());
            }
        }
        let epoch = boundary.revision();
        let before = boundary.snapshot(&self.profiles[0]).await?;
        let mut results = Vec::new();
        for profile in &profiles {
            let mut diagnostics = Vec::new();
            let outcome = if let Some(error) = startup.get(&profile.id) {
                Err(anyhow::anyhow!("{error}"))
            } else {
                match self.session(profile, root).await {
                    Ok(session) => session.check(requested, &mut diagnostics).await,
                    Err(e) => Err(e),
                }
            };
            let (status, errors) = match outcome {
                Ok(()) => ("completed", vec![]),
                Err(error) => (
                    if error.downcast_ref::<Incomplete>().is_some() {
                        "incomplete"
                    } else {
                        "failed"
                    },
                    vec![format!("{error:#}")],
                ),
            };
            results.push(json!({"backend":profile.id,"status":status,"complete":status=="completed","freshness":if status=="completed"{"verified"}else{"uncertain"},"diagnostics":normalize::ordered(diagnostics),"errors":errors}));
        }
        let mut errors = Vec::new();
        let after = boundary.snapshot(&self.profiles[0]).await?;
        if before != after || epoch != boundary.revision() {
            errors.push("Workspace inputs changed during combined analysis; recheck".to_owned());
        }
        let complete = errors.is_empty() && results.iter().all(|r| r["complete"] == true);
        let status = if complete {
            "completed"
        } else if results.len() == 1 && errors.is_empty() {
            results[0]["status"].as_str().unwrap_or("failed")
        } else {
            "incomplete"
        };
        let mut diagnostics = Vec::new();
        for result in &results {
            for value in result["diagnostics"].as_array().unwrap() {
                let mut value = value.clone();
                value["backend"] = result["backend"].clone();
                diagnostics.push(value);
            }
            for error in result["errors"].as_array().unwrap() {
                errors.push(format!(
                    "{}: {}",
                    result["backend"].as_str().unwrap(),
                    error.as_str().unwrap()
                ));
            }
        }
        let diagnostics = normalize::ordered(diagnostics);
        let backend = if results.len() == 1 {
            profiles[0].id.as_str()
        } else {
            "mixed"
        };
        Ok(
            json!({"status":status,"scope":scope,"requestedPath":requested,"complete":complete,"freshness":if complete{"verified"}else{"uncertain"},"diagnosticCount":diagnostics.len(),"diagnostics":diagnostics,"errors":errors,"durationMs":start.elapsed().as_secs_f64()*1000.0,"backend":backend,"backendResults":results}),
        )
    }
    async fn status(&mut self, root: &Path) -> Result<Value> {
        let boundary = Boundary::new(root.to_owned())?;
        let snapshot = boundary.snapshot(&self.profiles[0]).await?;
        let mut profiles: Vec<_> = self
            .profiles
            .iter()
            .filter(|s| snapshot.0.keys().any(|p| s.supports(p)))
            .cloned()
            .collect();
        if profiles.is_empty() {
            profiles = self.profiles.clone();
        }
        let mut states = Vec::new();
        for profile in profiles {
            states.push(match self.session(&profile,root).await {Ok(session)=>session.status(),Err(error)=>json!({"backend":profile.id,"available":false,"workspaceState":"failed","workspace":root,"diagnosticsAvailable":false,"lastError":error.to_string(),"sessionStarts":0,"processId":null,"serverName":null,"workspaceDiagnosticCapability":false})});
        }
        if states.len() == 1 {
            return Ok(states.remove(0));
        }
        let available = states.iter().all(|s| s["available"] == true);
        let diagnostics = states.iter().all(|s| s["diagnosticsAvailable"] == true);
        let errors: Vec<_> = states
            .iter()
            .filter_map(|s| s["lastError"].as_str())
            .collect();
        Ok(
            json!({"available":available,"workspaceState":if available{"ready"}else{"failed"},"workspace":root,"diagnosticsAvailable":diagnostics,"lastError":if errors.is_empty(){Value::Null}else{json!(errors.join("; "))},"sessionStarts":self.starts,"processId":null,"serverName":null,"workspaceDiagnosticCapability":states.iter().any(|s|s["workspaceDiagnosticCapability"]==true),"backend":"mixed","sessions":states}),
        )
    }
}

fn compact_result(mut value: Value) -> Value {
    let Some(result) = value.as_object_mut() else {
        return value;
    };
    for key in ["lastError", "serverName", "processId"] {
        if result.get(key).is_some_and(Value::is_null) {
            result.remove(key);
        }
    }
    if result
        .get("errors")
        .and_then(Value::as_array)
        .is_some_and(Vec::is_empty)
    {
        result.remove("errors");
    }
    if let Some(Value::Array(backends)) = result.remove("backendResults")
        && backends.len() > 1
    {
        let summaries: Vec<_> = backends.into_iter().map(|backend| {
            json!({"backend":backend["backend"],"status":backend["status"],"diagnosticCount":backend["diagnostics"].as_array().map_or(0, Vec::len)})
        }).collect();
        result.insert("backendResults".into(), json!(summaries));
    }
    if let Some(Value::Array(sessions)) = result.get_mut("sessions") {
        for session in sessions {
            *session = compact_result(std::mem::take(session));
            if let Some(state) = session.as_object_mut() {
                state.remove("workspace");
            }
        }
    }
    value
}
