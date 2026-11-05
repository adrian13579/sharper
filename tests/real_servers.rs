//! Optional real-server qualification. These never substitute fixture results for native integrations.
use anyhow::{Context, Result};
use serde_json::{Value, json};
use sharper::{
    config::Config,
    engine::{Args, Engine},
};
use std::{path::PathBuf, time::Duration};
fn tools() -> Result<PathBuf> {
    std::env::var_os("SHARPER_TEST_NODE_TOOLS")
        .map(PathBuf::from)
        .context("Set SHARPER_TEST_NODE_TOOLS to the pinned npm test-tools directory")
}
async fn check(engine: &mut Engine, root: &std::path::Path, path: &str) -> Result<Value> {
    let mut result = Value::Null;
    for _ in 0..4 {
        result = tokio::time::timeout(
            Duration::from_secs(120),
            engine.call(
                "sharper_check",
                Args {
                    workspace: Some(root.to_string_lossy().into_owned()),
                    path: Some(path.into()),
                },
            ),
        )
        .await??;
        if result["status"] != "incomplete" {
            break;
        }
    }
    assert_eq!(result["complete"], true, "{result}");
    assert_eq!(result["freshness"], "verified");
    Ok(result)
}
#[tokio::test]
#[ignore = "requires pinned TypeScript Language Server and Node"]
async fn real_typescript_compatibility_adapter() -> Result<()> {
    let root = tempfile::tempdir()?;
    let tools = tools()?;
    std::fs::write(
        root.path().join("tsconfig.json"),
        r#"{"compilerOptions":{"strict":true,"noEmit":true},"include":["*.ts"]}"#,
    )?;
    std::fs::write(
        root.path().join("example.ts"),
        "export const value: string = 42;\n",
    )?;
    let cfg = root.path().join("lsp.json");
    std::fs::write(
        &cfg,
        serde_json::to_vec(
            &json!({"servers":[{"id":"type-checker","command":"node","arguments":[tools.join("node_modules/typescript-language-server/lib/cli.mjs"),"--stdio"],"languages":{".ts":"typescript"},"diagnostics":"typeScript","initializationOptions":{"disableAutomaticTypingAcquisition":true}}]}),
        )?,
    )?;
    let mut engine = Engine::new(Some(Config::load(&cfg)?), None);
    let r = check(&mut engine, root.path(), "example.ts").await?;
    assert!(
        r["diagnostics"]
            .as_array()
            .unwrap()
            .iter()
            .any(|d| d["code"] == "2322")
    );
    std::fs::write(
        root.path().join("example.ts"),
        "export const value: string = 'fixed';\n",
    )?;
    let fixed = check(&mut engine, root.path(), "example.ts").await?;
    assert!(
        !fixed["diagnostics"]
            .as_array()
            .unwrap()
            .iter()
            .any(|d| d["severity"] == "error"),
        "{fixed}"
    );
    engine.close().await;
    Ok(())
}
#[tokio::test]
#[ignore = "requires pinned ESLint server, ESLint and Node"]
async fn real_eslint_standard_document_pull() -> Result<()> {
    let temp = tempfile::tempdir()?;
    let root = sharper::workspace::clean(std::fs::canonicalize(temp.path())?);
    let tools = tools()?;
    let rules = root.as_path().join("eslint.config.mjs");
    std::fs::write(
        &rules,
        "export default [{ files: ['**/*.js'], rules: { 'no-unused-vars': 'error' } }];",
    )?;
    std::fs::write(root.as_path().join("example.js"), "const unused = 1;\n")?;
    let cfg = root.as_path().join("lsp.json");
    std::fs::write(
        &cfg,
        serde_json::to_vec(
            &json!({"servers":[{"id":"lint-rules","command":"node","arguments":[tools.join("node_modules/vscode-langservers-extracted/bin/vscode-eslint-language-server"),"--stdio"],"languages":{".js":"javascript"},"settings":{"validate":"on","packageManager":"","useESLintClass":true,"experimental":{"useFlatConfig":false},"codeActionOnSave":{"enable":false,"mode":"all"},"format":false,"problems":{"shortenToSingleLine":false},"options":{"overrideConfigFile":rules},"quiet":false,"onIgnoredFiles":"warn","rulesCustomizations":[],"run":"onType","nodePath":tools.join("node_modules"),"workingDirectory":{"directory":root.as_path()},"workspaceFolder":{"uri":sharper::normalize::uri(root.as_path())?,"name":"fixture"}}}]}),
        )?,
    )?;
    let mut engine = Engine::new(Some(Config::load(&cfg)?), None);
    let r = check(&mut engine, root.as_path(), "example.js").await?;
    assert!(
        r["diagnostics"]
            .as_array()
            .unwrap()
            .iter()
            .any(|d| d["code"] == "no-unused-vars"),
        "{r}"
    );
    std::fs::write(root.as_path().join("example.js"), "console.log(1);\n")?;
    let fixed = check(&mut engine, root.as_path(), "example.js").await?;
    assert!(
        !fixed["diagnostics"]
            .as_array()
            .unwrap()
            .iter()
            .any(|d| d["severity"] == "error"),
        "{fixed}"
    );
    engine.close().await;
    Ok(())
}
#[tokio::test]
#[ignore = "requires the pinned official Roslyn server and .NET SDK"]
async fn real_roslyn_standard_pull_with_configured_readiness() -> Result<()> {
    let server = std::env::var_os("SHARPER_TEST_ROSLYN")
        .context("Set SHARPER_TEST_ROSLYN to an official server executable")?
        .to_string_lossy()
        .into_owned();
    let root = tempfile::tempdir()?;
    std::fs::write(
        root.path().join("sample.csproj"),
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
    )?;
    std::fs::write(
        root.path().join("Example.cs"),
        "class Example { string Value => 42; }",
    )?;
    let restored = tokio::time::timeout(
        Duration::from_secs(90),
        tokio::process::Command::new("dotnet")
            .arg("restore")
            .current_dir(root.path())
            .output(),
    )
    .await??;
    assert!(
        restored.status.success(),
        "{}",
        String::from_utf8_lossy(&restored.stderr)
    );
    let cfg = root.path().join("lsp.json");
    std::fs::write(
        &cfg,
        serde_json::to_vec(
            &json!({"servers":[{"id":"compiler-rules","requireResultId":true,"command":server,"arguments":["--stdio","--telemetryLevel","off","--logLevel","Warning"],"languages":{".cs":"csharp"},"initializationOptions":{"autoLoadProjects":500},"readyNotification":"workspace/projectInitializationComplete","settings":{"projects":{"dotnet_enable_automatic_restore":false}}}]}),
        )?,
    )?;
    let mut engine = Engine::new(Some(Config::load(&cfg)?), None);
    let r = check(&mut engine, root.path(), "Example.cs").await?;
    assert!(
        r["diagnostics"]
            .as_array()
            .unwrap()
            .iter()
            .any(|d| d["code"] == "CS0029")
    );
    std::fs::write(
        root.path().join("Example.cs"),
        "class Example { string Value => \"fixed\"; }",
    )?;
    let fixed = check(&mut engine, root.path(), "Example.cs").await?;
    assert!(
        !fixed["diagnostics"]
            .as_array()
            .unwrap()
            .iter()
            .any(|d| d["severity"] == "error"),
        "{fixed}"
    );
    engine.close().await;
    Ok(())
}
