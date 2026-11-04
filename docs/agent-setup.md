# Agent setup

Install the Rust executable and your chosen language servers, then prepare an explicit [LSP profile](custom-language-servers.md). Sharper does not install project dependencies or automatically provision language-specific packages.

## Guided setup

Run `sharper setup` or bare `sharper` in an interactive terminal. Choose an existing profile or create one by entering a server command, literal JSON argument array and extension mappings. Choose where to save it, review the write, then select a client and confirm registration. An existing file is never silently replaced. Type q or close input to cancel.

Colors are disabled when output is redirected, with `--no-color`, or with NO_COLOR. Profiles and agent registrations remain after completed writes. Install missing servers with their native tooling; setup does not claim to validate server protocol compatibility. Use sharper_status and a scoped check after connecting.

## Direct registration

```sh
sharper setup codex --lsp-config /absolute/path/servers.json
sharper setup claude --lsp-config /absolute/path/servers.json
sharper setup opencode --lsp-config /absolute/path/servers.json
sharper setup copilot --lsp-config /absolute/path/servers.json
```

- Codex requires its native CLI. `--codex-path` / `--codex-home` select an executable/profile. Sharper delegates TOML edits to `codex mcp add` and propagates errors.
- Claude Code writes ~/.claude.json or CLAUDE_CONFIG_DIR/.claude.json. `--config` overrides it.
- OpenCode writes its global opencode.json under XDG_CONFIG_HOME (or ~/.config). `--config` / OPENCODE_CONFIG override it.
- Copilot configures CLI and VS Code by default; `--client cli|vscode|both` selects scope. `--copilot-config` / `--vscode-config` override paths. COPILOT_HOME and platform-specific VS Code user directories are honored.

JSONC updates preserve unrelated values/comments and existing owned environment values. Files are validated before edits and written atomically per file. Setup stores the executable and absolute profile path, without a fixed workspace. Restart the MCP connection after registration or profile changes.

For other clients using an mcpServers collection:

```json
{"mcpServers":{"sharper":{"command":"/absolute/path/sharper","args":["mcp","--lsp-config","/absolute/path/servers.json"]}}}
```

Use client-specific setup for Codex/OpenCode, which have different schemas.

## Agent instructions

Use sharper_check to establish a baseline before meaningful changes. Edit with normal tools, then recheck affected files and dependent code. Pass the absolute workspace and affected path. Require completed, fresh analysis; inspect severities and remaining findings separately. Fix introduced violations, recheck, and run relevant builds/tests separately. Navigation tools provide optional context and use zero-based UTF-16 positions. Status reports availability, not diagnostic verification.

See [feedback](feedback.md), [configuration](custom-language-servers.md) and [Rust migration](migration-to-rust.md).
