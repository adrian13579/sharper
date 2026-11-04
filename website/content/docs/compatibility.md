# Compatibility

Sharper connects to configured language servers and custom rule enforcers. Compatibility depends on the server's protocol capabilities and behavior, rather than a built-in language list.

## Platforms

The npm package supports Windows x64 and Linux x64 with glibc. Node.js 22.22.2 or newer is required. Each language server retains its own installation and runtime requirements.

## Required capabilities

Verified feedback requires standard stdio LSP, UTF-16 positions, open/close and full or incremental synchronization, and document-pull diagnostics. Push-only servers cannot produce verified checks. Navigation additionally requires the corresponding advertised operation.

Configure server-specific initialization options, settings and optional readiness notifications in your [server profile](custom-language-servers.md). The [service contract](lsp-service.md) describes the precise guarantees.

## Qualify your rules

Before relying on a profile, test a representative violation and its correction. Confirm the expected diagnostic code, severity and location; inspect scope, completion and freshness after each check. A server that starts successfully may still lack the capabilities needed for verification.

[Example profiles](https://github.com/adrian13579/sharper/tree/main/examples/lsp) illustrate configured services. They are starting points to adapt and test, not a limit on supported languages or a guarantee about every server version.

## Workspace boundaries

Source text must be UTF-8. Files outside the selected workspace, symlinks and excluded dependency directories are outside the reconciliation boundary. Keep toolchains and external dependencies stable during a session; restart after changing them. See [scope and freshness](lsp-service.md).
