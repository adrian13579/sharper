use anyhow::{Result, ensure};
use clap::{
    ColorChoice, CommandFactory, FromArgMatches, Parser, Subcommand,
    builder::styling::{AnsiColor, Styles},
};
use std::{
    io::{self, IsTerminal},
    path::PathBuf,
    time::Duration,
};
#[derive(Parser)]
#[command(
    name = "sharper",
    bin_name = "sharper",
    version,
    about = "Rapid rule feedback for coding agents",
    styles = Styles::styled().header(AnsiColor::Cyan.on_default().bold()).usage(AnsiColor::Cyan.on_default().bold()).literal(AnsiColor::Green.on_default()),
    after_help = "Quick start:\n  sharper setup\n  sharper setup claude --lsp-config /absolute/path/servers.json\n\nIn a terminal, sharper opens guided setup.\nGuide: https://adrian13579.github.io/sharper/docs/overview/"
)]
struct Cli {
    #[command(subcommand)]
    command: Option<Command>,
    /// Language-service profile to use for setup or MCP.
    #[arg(
        long,
        global = true,
        env = "SHARPER_LSP_CONFIG",
        value_name = "SERVERS_JSON"
    )]
    lsp_config: Option<PathBuf>,
    /// Overall MCP request deadline in seconds (up to 3600).
    #[arg(long, global = true, default_value = "120", value_name = "SECONDS")]
    timeout: f64,
    /// Fallback workspace when the agent does not supply one.
    #[arg(long, global = true)]
    workspace: Option<PathBuf>,
    /// Use plain output, including help and setup prompts.
    #[arg(long, global = true)]
    no_color: bool,
}
#[derive(Subcommand)]
enum Command {
    /// Serve MCP requests over stdio for a connected agent.
    Mcp,
    /// Connect language services and an agent; omit the agent for guided setup.
    Setup(sharper::setup::Options),
}
#[tokio::main(worker_threads = 2)]
async fn main() {
    let result = run().await;
    if let Err(error) = result {
        if error.downcast_ref::<sharper::setup::Cancelled>().is_some() {
            eprintln!("Setup cancelled.");
            std::process::exit(130);
        }
        eprintln!("error: {error:#}");
        std::process::exit(2)
    }
}
async fn run() -> Result<()> {
    let plain = std::env::var_os("NO_COLOR").is_some()
        || std::env::args_os().any(|arg| arg == "--no-color");
    let matches = Cli::command()
        .color(if plain {
            ColorChoice::Never
        } else {
            ColorChoice::Auto
        })
        .get_matches();
    let cli = Cli::from_arg_matches(&matches)?;
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
