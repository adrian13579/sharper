# Rapid rule feedback for your agent

Sharper brings your compiler, analyzer and custom rule findings into the agent's editing loop. Check the affected code, fix violations, and verify the result without starting a fresh language server for every request.

## Start with a small loop

1. [Install Sharper with npm](npm.md) or configure an npx MCP command.
2. [Connect your agent](agent-setup.md) using an explicit server profile.
3. [Configure your language services and rules](custom-language-servers.md).
4. Establish a baseline, edit, check, fix and verify with [the feedback workflow](feedback.md).

## Your languages. Your rules.

Sharper speaks Language Server Protocol (LSP). Your existing language services and custom enforcers supply the rules; Sharper delivers their findings through MCP with scope, completion and freshness evidence. Support follows your configured servers and file mappings, not a fixed language list.

## Bring your architecture rules

Connect a custom LSP enforcer to give your agent rapid feedback on project-specific constraints, such as allowed module dependencies and layer boundaries. See [project architecture rules](architecture-rules.md) to turn guidelines into findings your agent can fix and verify.

## Tools at a glance

| Tool                     | Purpose                                                                  |
| ------------------------ | ------------------------------------------------------------------------ |
| `sharper_check`          | Get compiler, analyzer and rule-server findings for a file or directory. |
| `sharper_status`         | Inspect availability and capabilities.                                   |
| `sharper_definition`     | Find a symbol's definition.                                              |
| `sharper_implementation` | Find implementations.                                                    |
| `sharper_references`     | Find references.                                                         |

## Know what a check proves

A completed, current check can still contain violations. Inspect findings and their severities before deciding the next step. Clean document diagnostics complement builds and tests; they do not certify them. Sharper reports findings and never applies source edits. See the [service contract](lsp-service.md) for exact boundaries.
