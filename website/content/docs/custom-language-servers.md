# Language servers and custom rule enforcers

**Sharper speaks LSP.** A compatible language server or custom rule enforcer can deliver its findings to your agent through Sharper. Configure language support through protocol capabilities and file mappings. The server owns rules and semantics; Sharper owns transport and verification evidence.

## Configure your services

Create a JSON file outside your projects:

```json
{
  "servers": [
    {
      "id": "project-rules",
      "command": "/opt/team/bin/rule-server",
      "arguments": ["--stdio"],
      "languages": { ".orbit": "planet" },
      "initializationOptions": { "ruleset": "team" },
      "settings": { "rules": { "strict": true } }
    }
  ]
}
```

Save the file as `servers.json` and pass its absolute path with `--lsp-config`. The filename is your choice. Use ordinary JSON with quoted keys; Sharper validates field names, types and mappings before starting services. A profile can contain 1–32 servers, and up to four workspace/server contexts stay warm at once.

`.orbit` and `planet` are example extension and language-ID mappings. `ruleset` and `rules.strict` illustrate options a team-owned server might understand. Choose actual option names and values from your service's documentation.

### Server parameters

Each entry in `servers` has these fields:

| Field                   | Type / default                       | Purpose                                                                                                                                                                               |
| ----------------------- | ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                    | String; `"custom"`                   | Identifies this service in findings and status. Use a unique ID for every entry: 1–64 ASCII letters, digits, hyphens or underscores.                                                  |
| `command`               | Required string                      | Executable to launch, such as `"node"` or an absolute path to your service. A bare name resolves on `PATH`; a relative executable path resolves against the profile file's directory. |
| `arguments`             | Array of strings; `[]`               | Literal command-line arguments, in order. Include required flags and script paths explicitly.                                                                                         |
| `languages`             | Required object                      | Maps file extensions to LSP language IDs. At least one mapping is required. These mappings select which files the service analyzes.                                                   |
| `initializationOptions` | Object or `null`; `null`             | Service-specific startup options sent with the LSP `initialize` request.                                                                                                              |
| `settings`              | Object or `null`; `null`             | Service-specific configuration sent after initialization and returned when the service requests configuration.                                                                        |
| `navigation`            | Boolean; `true`                      | Makes this service eligible for supported symbol-navigation requests. Use `false` for a service dedicated to rule findings.                                                           |
| `diagnostics`           | `"pull"` or `"typeScript"`; `"pull"` | Selects standard document-pull diagnostics or the explicit TypeScript compatibility adapter.                                                                                          |
| `requireResultId`       | Boolean; `false`                     | With `"pull"`, require a diagnostic result ID as additional document-recognition evidence. Enable this when your service relies on that evidence.                                     |
| `readyNotification`     | String or `null`; `null`             | Optional LSP notification method the service emits when its workspace is ready. With this set, Sharper waits for that notification within the request deadline.                       |

### Commands and arguments

Use one array element per argument, including values that contain spaces:

```json
{
  "command": "node",
  "arguments": [
    "/opt/team tools/rule-server/server.js",
    "--stdio",
    "--log-level",
    "warning"
  ]
}
```

This fragment belongs inside a server entry alongside its `id` and `languages`. `command` names the executable; `arguments` supplies its parameters. Include flags such as `--stdio` when your service requires them. Keep spaces inside a single string and use JSON escaping for Windows paths, such as `"C:\\tools\\rule-server.exe"`.

Sharper starts services in the selected workspace directory and passes arguments literally. Use absolute paths for script or configuration arguments to keep them stable across workspaces. Choose an executable or an explicit script runtime, such as `node`, for the command. The `--lsp-config` option selects Sharper's profile; a service's own configuration-file flag belongs in its `arguments` array.

### File mappings

For example, a service that handles several source types could use:

```json
"languages": {
  ".ts": "typescript",
  ".tsx": "typescriptreact",
  ".js": "javascript"
}
```

Extension matching is case-insensitive; language IDs are passed exactly as written. Use a leading dot and an extension made of ASCII letters, digits, hyphens or underscores. Match the file's final extension: `.ts` also selects `example.d.ts`. Each extension appears once per server, while several server entries can map the same extension to combine language and rule feedback. Choose mappings supported by each service.

### Initialization options and settings

These objects serve different points in the connection lifecycle:

- **`initializationOptions`:** Sharper passes this object unchanged in `initialize`. Use it for startup options defined by the service, such as workspace loading or feature selection.
- **`settings`:** after `initialized`, Sharper sends a `workspace/didChangeConfiguration` notification containing this object. It also uses the object to answer `workspace/configuration` requests from the service.

For an illustrative team-owned rule service:

```json
{
  "initializationOptions": {
    "ruleset": "team-architecture"
  },
  "settings": {
    "rules": {
      "strict": true,
      "severity": "error"
    }
  }
}
```

The service defines what `ruleset`, `strict` and `severity` mean. Sharper preserves their values, including nested objects, arrays, booleans and numbers. Keep the exact structure expected by your service.

When a service requests configuration section `"rules"`, Sharper returns the nested `rules` object above. Section `"rules.strict"` returns `true`. A request with an empty or omitted section returns the entire settings value; a missing section returns `null`. Use nested objects for dotted section names.

Omitting either field gives it the value `null`. Supplying `"settings": {}` sends an empty settings object after initialization. Restart the agent's MCP connection after changing profile values so new sessions use the updated configuration.

### Diagnostic and readiness options

Start with `"diagnostics": "pull"` for standard LSP document diagnostics. The `"typeScript"` mode is an explicit adapter for the supported synchronous syntax/semantic diagnostic commands; choose it for a service that exposes that interface. `requireResultId` applies to the standard pull mode. With it enabled, a report missing its result ID is treated as incomplete, so your agent can distinguish an unrecognized document from a completed check.

Use `readyNotification` when your service documents a specific workspace-ready notification. Enter its exact method name, such as one supplied in the service's integration guide. With the default `null`, readiness follows completion of initialization. The service remains responsible for completing diagnostic requests once its analysis is available.

With `navigation: true`, Sharper can route a navigation request to the first matching service that advertises the requested capability. With `false`, the service still contributes rule findings to checks.

Install services with their own tooling and select trusted programs and profiles. These programs run with your user permissions. Sharper uses the explicitly selected profile and the workspace chosen for each tool call.

## Connect your agent

After saving `servers.json`, register it with your agent. Prepare language services and project dependencies with their own tooling.

### Guided setup

Run `sharper setup` or bare `sharper` in an interactive terminal. Choose an existing profile or create one by entering a server command, literal JSON argument array and extension mappings. Choose where to save it, review the write, then select a client and confirm registration. Choose a new destination when creating a profile. Type q or close input to cancel.

Colors are disabled when output is redirected, with `--no-color`, or with NO_COLOR. Profiles and agent registrations remain after completed writes. Install servers with their native tooling, then use `sharper_status` and a scoped check to confirm your connection and expected findings.

### Direct registration

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

JSONC updates preserve unrelated values/comments and existing owned environment values. Files are validated before edits and written atomically per file. Setup stores the executable and absolute profile path; tool calls select the workspace dynamically. Restart the MCP connection after registration or profile changes.

For other clients using an mcpServers collection:

```json
{
  "mcpServers": {
    "sharper": {
      "command": "/absolute/path/sharper",
      "args": ["mcp", "--lsp-config", "/absolute/path/servers.json"]
    }
  }
}
```

Use client-specific setup for Codex/OpenCode, which have different schemas.

## Server requirements

Implement the relevant [LSP 3.17](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/) capabilities:

- Stdio JSON-RPC with Content-Length framing and protocol-only stdout.
- initialize/initialized, UTF-16 positions, open/close and full or incremental changes.
- `textDocument/diagnostic` with valid full reports, or unchanged reports matching a cached ID. Document pulls provide completion evidence for verified feedback.
- Optional definition/implementation/reference capabilities. Use dynamic file/language selectors to scope capabilities.
- Optional workspace/configuration and didChangeConfiguration using profile settings.
- Cancellation, errors, refresh, capability changes and orderly shutdown.

Sharper synchronizes mapped source documents in the selected workspace, collects rule findings and returns their scope, completion and freshness. Your agent uses those findings to make focused corrections.

## Compatibility profiles

[Example profiles](https://github.com/adrian13579/sharper/tree/main/examples/lsp) illustrate real server integration, including optional readiness settings and the explicitly selected `diagnostics: "typeScript"` compatibility adapter. That adapter permits only the existing TypeScript synchronous syntax/semantic diagnostic commands. Standard `diagnostics: "pull"` is the default for every language.

Qualify each chosen server, its project settings and rule behavior against representative workspaces. Test a known violation and its correction with your chosen configuration before relying on the findings.
