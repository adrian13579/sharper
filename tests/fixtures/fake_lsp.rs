use anyhow::Result;
use serde_json::{Value, json};
use sharper::framing::{read_lsp, write_lsp};
use std::{collections::BTreeMap, path::PathBuf, time::Duration};
use tokio::io::BufReader;
#[tokio::main(flavor = "current_thread")]
async fn main() -> Result<()> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if args.first().map(String::as_str) == Some("mcp")
        && args.get(1).map(String::as_str) == Some("add")
    {
        let home = std::env::var_os("CODEX_HOME").expect("Isolate Codex test configuration");
        std::fs::create_dir_all(&home)?;
        std::fs::write(
            PathBuf::from(home).join("registration.json"),
            serde_json::to_vec(&args)?,
        )?;
        std::process::exit(
            std::env::var("SHARPER_FAKE_CODEX_EXIT")
                .unwrap_or_else(|_| "0".into())
                .parse()?,
        );
    }
    if args.first().map(String::as_str) == Some("--sleep-child") {
        loop {
            std::fs::write("heartbeat", format!("{:?}", std::time::Instant::now()))?;
            tokio::time::sleep(Duration::from_millis(15)).await;
        }
    }
    let mode = args
        .iter()
        .position(|v| v == "--mode")
        .and_then(|i| args.get(i + 1))
        .cloned()
        .unwrap_or_else(|| {
            std::fs::read_to_string("mode.txt")
                .unwrap_or_else(|_| "normal".into())
                .trim()
                .into()
        });
    let audit = args
        .iter()
        .position(|v| v == "--audit")
        .and_then(|i| args.get(i + 1))
        .map(PathBuf::from);
    let mut input = BufReader::new(tokio::io::stdin());
    let mut output = tokio::io::stdout();
    let mut documents = BTreeMap::<String, (String, i64)>::new();
    let mut pulls = 0;
    let mut handshake = 0;
    let mut pending: Option<Value> = None;
    let mut settings = false;
    while let Some(msg) = read_lsp(&mut input).await? {
        if let Some(path) = &audit {
            use std::io::Write;
            let mut log = std::fs::OpenOptions::new()
                .create(true)
                .append(true)
                .open(path)?;
            log.write_all(format!("{}\n", serde_json::to_string(&msg)?).as_bytes())?;
        }
        let id = msg["id"].clone();
        let p = &msg["params"];
        let method = msg["method"].as_str().unwrap_or("");
        if method.is_empty() {
            if id == "config" {
                assert_eq!(msg["result"][0], true);
                assert!(msg["result"][1].is_null());
                assert_eq!(msg["result"][2]["rules"]["enabled"], true);
                handshake += 1;
            }
            if id == "edit" {
                assert_eq!(msg["result"]["applied"], false);
                handshake += 1;
            }
            if handshake == 2
                && let Some(id) = pending.take()
            {
                report(&mut output, id, &documents, &mode, pulls).await?;
            }
            continue;
        }
        match method {
            "initialize" => {
                if mode == "crash-start" {
                    return Ok(());
                }
                if mode == "hang-start" {
                    continue;
                }
                if mode == "handshake" {
                    assert_eq!(p["initializationOptions"]["marker"], "agnostic");
                    assert!(args.iter().any(|v| v == "space & $ literal"));
                }
                let mut caps = json!({"textDocumentSync":{"openClose":true,"change":if mode=="full"{1}else{2}},"diagnosticProvider":true});
                if mode == "utf8" {
                    caps["positionEncoding"] = json!("utf-8");
                }
                if mode == "no-sync" {
                    caps["textDocumentSync"] = json!(0);
                }
                if mode == "push" || mode.starts_with("dynamic") {
                    caps.as_object_mut().unwrap().remove("diagnosticProvider");
                }
                if mode == "typescript" {
                    caps["executeCommandProvider"] =
                        json!({"commands":["typescript.tsserverRequest"]});
                    caps.as_object_mut().unwrap().remove("diagnosticProvider");
                }
                let server_name = std::fs::read_to_string("server-name.txt")
                    .unwrap_or_else(|_| "Unlisted language rules fixture".into());
                write_lsp(&mut output,&json!({"jsonrpc":"2.0","id":id,"result":{"capabilities":caps,"serverInfo":{"name":server_name}}})).await?;
            }
            "initialized" => {
                if mode == "tree" {
                    let _child = std::process::Command::new(std::env::current_exe()?)
                        .arg("--sleep-child")
                        .stdin(std::process::Stdio::null())
                        .stdout(std::process::Stdio::null())
                        .stderr(std::process::Stdio::null())
                        .spawn()?;
                }
                if mode.starts_with("dynamic") {
                    let mut selector = json!({"language":"planet","scheme":"file"});
                    if mode == "dynamic-pattern" {
                        selector["pattern"] = json!("**/*.orbit");
                    }
                    write_lsp(&mut output,&json!({"jsonrpc":"2.0","id":"register","method":"client/registerCapability","params":{"registrations":[{"id":"doc","method":"textDocument/diagnostic","registerOptions":{"documentSelector":[selector]}}]}})).await?;
                }
                if mode == "handshake" {
                    write_lsp(&mut output,&json!({"jsonrpc":"2.0","id":"config","method":"workspace/configuration","params":{"items":[{"section":"rules.enabled"},{"section":"missing"},{}]}})).await?;
                    write_lsp(&mut output,&json!({"jsonrpc":"2.0","id":"edit","method":"workspace/applyEdit","params":{"edit":{"changes":{}}}})).await?;
                }
                write_lsp(&mut output, &json!({"jsonrpc":"2.0","method":"test/ready"})).await?;
            }
            "workspace/didChangeConfiguration" => {
                settings = p["settings"]["rules"]["enabled"] == true
            }
            "textDocument/didOpen" => {
                let doc = &p["textDocument"];
                assert_eq!(doc["languageId"], "planet");
                documents.insert(
                    doc["uri"].as_str().unwrap().into(),
                    (
                        doc["text"].as_str().unwrap().into(),
                        doc["version"].as_i64().unwrap(),
                    ),
                );
            }
            "textDocument/didChange" => {
                let doc = &p["textDocument"];
                let value = documents.get_mut(doc["uri"].as_str().unwrap()).unwrap();
                assert_eq!(doc["version"].as_i64().unwrap(), value.1 + 1);
                value.0 = p["contentChanges"][0]["text"].as_str().unwrap().into();
                value.1 += 1;
            }
            "textDocument/didClose" => {
                documents.remove(p["textDocument"]["uri"].as_str().unwrap());
            }
            "textDocument/diagnostic" => {
                pulls += 1;
                if mode == "hang" || mode == "tree" {
                    continue;
                }
                if mode == "crash" {
                    return Ok(());
                }
                if mode == "race" {
                    std::fs::write("changed.config", "changed")?;
                }
                if mode == "refresh-always" || mode == "refresh" && pulls == 1 {
                    write_lsp(&mut output,&json!({"jsonrpc":"2.0","id":format!("refresh-{pulls}"),"method":"workspace/diagnostic/refresh"})).await?;
                }
                if mode == "capability-race" {
                    write_lsp(&mut output,&json!({"jsonrpc":"2.0","id":"reg","method":"client/registerCapability","params":{"registrations":[{"id":"extra","method":"textDocument/diagnostic"}]}})).await?;
                }
                if mode == "handshake" {
                    assert!(settings);
                    if handshake != 2 {
                        pending = Some(id);
                        continue;
                    }
                }
                if mode == "malformed" {
                    write_lsp(&mut output,&json!({"jsonrpc":"2.0","id":id,"result":{"kind":"full","items":"invalid"}})).await?;
                    continue;
                }
                if mode == "bad-unchanged" {
                    write_lsp(&mut output,&json!({"jsonrpc":"2.0","id":id,"result":{"kind":"unchanged","resultId":"unknown"}})).await?;
                    continue;
                }
                if mode == "unchanged"
                    && p["previousResultId"]
                        == json!(documents.values().next().unwrap().1.to_string())
                {
                    write_lsp(&mut output,&json!({"jsonrpc":"2.0","id":id,"result":{"kind":"unchanged","resultId":p["previousResultId"]}})).await?;
                    continue;
                }
                report(&mut output, id, &documents, &mode, pulls).await?;
            }
            "workspace/executeCommand" => {
                assert_eq!(p["command"], "typescript.tsserverRequest");
                assert!(
                    ["syntacticDiagnosticsSync", "semanticDiagnosticsSync"]
                        .contains(&p["arguments"][0].as_str().unwrap())
                );
                let body = if documents.values().any(|d| d.0.contains("violation")) {
                    json!([{"code":2322,"message":"Type rule","category":"error","startLocation":{"line":1,"offset":1},"endLocation":{"line":1,"offset":2}}])
                } else {
                    json!([])
                };
                write_lsp(
                    &mut output,
                    &json!({"jsonrpc":"2.0","id":id,"result":{"success":true,"body":body}}),
                )
                .await?;
            }
            "shutdown" => {
                write_lsp(&mut output, &json!({"jsonrpc":"2.0","id":id,"result":null})).await?
            }
            "exit" => break,
            _ => {}
        }
    }
    Ok(())
}
async fn report(
    output: &mut tokio::io::Stdout,
    id: Value,
    documents: &BTreeMap<String, (String, i64)>,
    mode: &str,
    _pulls: u64,
) -> Result<()> {
    let (uri, doc) = documents.iter().next().unwrap();
    let range = if mode == "utf16-ranges" {
        json!({"start":{"line":0,"character":2},"end":{"line":0,"character":11}})
    } else {
        json!({"start":{"line":0,"character":0},"end":{"line":0,"character":1}})
    };
    let items = if doc.0.contains("violation") || doc.0.contains("missing") {
        json!([{"code":"RULE001","source":"planet-enforcer","message":"Configured rule violation","severity":if mode=="unknown-severity"{9}else{1},"range":range,"codeDescription":{"href":"https://example.invalid/rules/1"},"tags":[1,2],"data":{"opaque":[1,"kept"]},"relatedInformation":[{"message":"Related context","location":{"uri":uri,"range":range}}]}])
    } else {
        json!([])
    };
    let mut report = json!({"kind":"full","items":items});
    if mode == "unchanged" {
        report["resultId"] = json!(doc.1.to_string());
    }
    write_lsp(output, &json!({"jsonrpc":"2.0","id":id,"result":report})).await
}
