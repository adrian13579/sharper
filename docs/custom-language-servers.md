# Language servers and custom rule enforcers

**Sharper speaks LSP.** A compatible language server or custom rule enforcer can deliver its findings to your agent through Sharper. Support is configured by protocol capabilities and file mappings, rather than a fixed language list. The server owns rules and semantics; Sharper owns transport and verification evidence.

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

`.orbit` / `planet` illustrate an arbitrary extension and language ID, not a built-in language. Replace them with your server's mappings. Add more entries to combine language analysis and custom enforcement on matching files. IDs must be unique and contain ASCII letters, digits, hyphens or underscores. One to 32 server profiles are accepted; four workspace/server contexts stay warm at once.

A bare command resolves on PATH. Relative executable paths resolve against the profile directory. Windows JSON paths need escaped backslashes, for example `"command": "C:\\tools\\rule-server.exe"`. Arguments are literal separate strings; no shell expansion or implicit `--stdio` is added. Use absolute file paths in arguments. Managed/script servers use an explicit runtime command and DLL/script argument. `.cmd` and `.bat` launchers are rejected.

`languages` maps case-insensitive extensions to exact LSP IDs. Mappings, command and arguments are validated before startup. Optional initializationOptions/settings must be JSON objects. Optional `requireResultId: true` treats missing document result IDs as incomplete for servers that use them as recognition evidence. This is a profile-specific requirement, not imposed on standard full reports generally.

Optional `navigation: false` disables a rule-only service for navigation. Optional `readyNotification` names an explicit server readiness notification; without it, initialization completes readiness and the server must make its own diagnostic request completion meaningful.

The old single-server object is still accepted with default ID `custom`, but no built-in language checks are added implicitly. Install servers separately and use trusted programs/configurations: external servers run with your user's permissions. Profiles are explicit startup data, never auto-discovered from target repositories.

## Connect the agent

```sh
sharper setup codex --lsp-config /absolute/path/servers.json
sharper setup claude --lsp-config /absolute/path/servers.json
sharper setup opencode --lsp-config /absolute/path/servers.json
sharper setup copilot --lsp-config /absolute/path/servers.json
```

Use your client's command or run guided `sharper setup` to create/select a profile. Direct MCP launch is `sharper mcp --lsp-config ...`. `SHARPER_LSP_CONFIG` is an optional fallback. Restart the client's MCP connection after profile changes.

The agent keeps passing workspace and affected path to the ordinary tools. File/directory checks include every matching configured service. No source match or unavailable rules is an explicit failure, not a clean verification. Navigation uses the first enabled matching service advertising the requested capability.

## Supported LSP contract

Implement the relevant [LSP 3.17](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/) capabilities:

- Stdio JSON-RPC with Content-Length framing and protocol-only stdout.
- initialize/initialized, UTF-16 positions, open/close and full or incremental changes.
- `textDocument/diagnostic` with valid full reports, or unchanged reports matching a cached ID. Push-only services cannot produce verified feedback.
- Optional definition/implementation/reference capabilities. Dynamic file/language selectors are supported; pattern-scoped selectors are conservatively unsupported.
- Optional workspace/configuration and didChangeConfiguration using profile settings.
- Cancellation, errors, refresh, capability changes and orderly shutdown.

Sharper does not apply edits, forward arbitrary commands, provide formatting/rename/code actions, install dependencies or certify a server's rules. See [the service contract](lsp-service.md) for freshness boundaries.

## Compatibility profiles

[Example profiles](../examples/lsp) illustrate real server integration, including optional readiness settings and the explicitly selected `diagnostics: "typeScript"` compatibility adapter. That adapter permits only the existing TypeScript synchronous syntax/semantic diagnostic commands. Standard `diagnostics: "pull"` is the default for every language.

Speaking LSP alone is not a universal compatibility guarantee. Qualify each chosen server, its project settings and rule behavior against representative workspaces. See [compatibility tests](compatibility.md).
