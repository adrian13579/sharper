# Sharper

![Sharper logo](https://raw.githubusercontent.com/adrian13579/sharper/main/logo.svg)

[![Tests](https://github.com/adrian13579/sharper/actions/workflows/tests.yml/badge.svg?branch=main)](https://github.com/adrian13579/sharper/actions/workflows/tests.yml)
[![Build](https://github.com/adrian13579/sharper/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/adrian13579/sharper/actions/workflows/build.yml)
[![Latest release](https://img.shields.io/github/v/release/adrian13579/sharper?include_prereleases&label=release)](https://github.com/adrian13579/sharper/releases)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

**Rapid rule feedback for coding agents. Check, fix, verify after every edit.**

Sharper gives coding agents **rapid feedback on rule violations**, so they can correct changes while the work is still in context. Reuse warm local sessions to check compiler, analyzer and custom rule findings, fix violations, and verify the affected scope through one MCP interface. It speaks **Language Server Protocol**, connecting your existing language services and custom rule enforcers across languages.

Bring your own architecture rules: connect a [custom LSP enforcer](https://adrian13579.github.io/sharper/docs/architecture-rules/) to flag forbidden dependencies and layer violations, so your agent can fix and recheck them as it works.

**[Website](https://adrian13579.github.io/sharper/) | [Documentation](https://adrian13579.github.io/sharper/docs/overview/) | [Get started](#get-started)**

## Why Sharper

- **Keep feedback close to the edit.** Reuse warm local server sessions instead of starting a fresh language server for every check.
- **Enforce your native rules.** Your compiler, analyzers and custom LSP rule servers define the checks. Sharper returns evidence for your agent to act on.
- **Use your languages.** Configure extensions, LSP language IDs and server commands. Multiple matching servers can contribute findings to the same files.
- **Verify with evidence.** Inspect findings, precise locations, checked scope, completion and freshness separately.
- **Navigate when useful.** Definitions, implementations and references are supporting capabilities when your configured server advertises them.

Sharper reports violations; it does not apply edits or block commits. Clean document diagnostics complement builds/tests and never certify them. Verified feedback requires the supported LSP contract, including document pulls; speaking some part of LSP alone is insufficient.

## Get started

Install through npm, then run guided setup:

```sh
npm install --global @adrian13579/sharper@next
sharper setup
```

For an MCP client that launches commands directly, use `npx --yes @adrian13579/sharper@next mcp --lsp-config /absolute/path/servers.json`. See [npm installation](https://adrian13579.github.io/sharper/docs/npm/) for requirements and registry availability.

Configure one or more LSP servers, then connect **Codex, Claude Code, OpenCode or GitHub Copilot**:

```sh
sharper setup codex --lsp-config /absolute/path/servers.json
```

Use the same option with the other client setup commands. Guided setup can create a profile and register your client after confirmation. Install language servers and restore target-project dependencies with their native tooling. See [server configuration](https://adrian13579.github.io/sharper/docs/custom-language-servers/) and [agent setup](https://adrian13579.github.io/sharper/docs/agent-setup/).

## Tools

| Tool | Purpose |
| --- | --- |
| `sharper_check` | Get configured compiler, analyzer and rule-server findings for a file or directory. |
| `sharper_status` | Inspect server availability and advertised capabilities. |
| `sharper_definition` | Find symbol definitions. |
| `sharper_implementation` | Find implementations. |
| `sharper_references` | Find references, optionally including declarations. |

For example, pass this to `sharper_check`:

```json
{"workspace":"/work/app","path":"src"}
```

Follow **baseline, edit, check, fix, verify**. A current completed request can still contain errors: inspect severities and remaining findings. Run relevant builds and tests separately. See [the feedback workflow](https://adrian13579.github.io/sharper/docs/feedback/).

## Documentation

Browse the [documentation website](https://adrian13579.github.io/sharper/docs/overview/) for installation, agent setup, server configuration and the check, fix, verify workflow. Search all guides and navigate sections from the sidebar.

## Contributing

Follow [AGENTS.md](AGENTS.md) for validation and protocol boundaries. Bug reports should include your server versions, profile, minimal reproduction and actual tool response. See [website development](website/README.md) for the Next.js site.

## License

[Apache License 2.0](LICENSE). Sharper relies on external LSP servers for language semantics and rule execution.
