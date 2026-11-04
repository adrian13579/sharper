use anyhow::{Result, ensure};
use clap::{Parser, Subcommand};
use std::{
    io::{self, IsTerminal},
    path::PathBuf,
    time::Duration,
};
#[derive(Parser)]
#[command(
    name = "sharper",
    version,
    about = "Fast, language-agnostic LSP feedback and rule verification for coding agents"
)]
struct Cli {
    #[command(subcommand)]
    command: Option<Command>,
    /// Explicit LSP server configuration; never loaded from a repository automatically.
    #[arg(long, global = true, env = "SHARPER_LSP_CONFIG")]
    lsp_config: Option<PathBuf>,
    #[arg(long, global = true, default_value = "120")]
    timeout: f64,
    /// Fallback workspace only. MCP tools normally select their workspace per call.
    #[arg(long, global = true)]
    workspace: Option<PathBuf>,
    #[arg(long, global = true)]
    no_color: bool,
}
#[derive(Subcommand)]
enum Command {
    Mcp,
    Setup(sharper::setup::Options),
}
#[tokio::main(worker_threads = 2)]
async fn main() {
    let result = run().await;
    if let Err(error) = result {
        eprintln!("sharper: {error:#}");
        std::process::exit(2)
    }
}
async fn run() -> Result<()> {
    let cli = Cli::parse();
    ensure!(
        cli.timeout.is_finite() && cli.timeout > 0.0 && cli.timeout <= 3600.0,
        "timeout must be a positive number of seconds up to 3600"
    );
    let command = cli.command.unwrap_or_else(|| {
        if io::stdin().is_terminal() && io::stdout().is_terminal() {
            Command::Setup(sharper::setup::Options {
                agent: None,
                config: None,
                client: "both".into(),
                copilot_config: None,
                vscode_config: None,
                codex_path: "codex".into(),
                codex_home: None,
            })
        } else {
            Command::Mcp
        }
    });
    match command {
        Command::Setup(options) => sharper::setup::run(options, cli.lsp_config, cli.no_color).await,
        Command::Mcp => {
            let config = cli
                .lsp_config
                .as_deref()
                .map(sharper::config::Config::load)
                .transpose()?;
            let fallback = cli
                .workspace
                .map(|p| sharper::config::absolute(&p))
                .transpose()?;
            sharper::mcp::serve(
                sharper::engine::Engine::new(config, fallback),
                Duration::from_secs_f64(cli.timeout),
            )
            .await
        }
    }
}
