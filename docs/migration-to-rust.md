# Migration to Rust (0.3)

Sharper's executable and MCP implementation are now Rust. The five public tool names and diagnostic/navigation evidence model remain, but this is a breaking pre-alpha migration.

## Configure language support explicitly

There is no automatic built-in C#/TypeScript/JavaScript routing. Supply --lsp-config with one or more standard LSP server profiles and extension mappings. Multiple matching services contribute diagnostics; navigation uses an advertised operation on the first matching enabled service. A former single custom profile is accepted, but no implicit native services are added.

Move old server paths and settings into [profiles](custom-language-servers.md). Per-call backend and solution parameters and the former --server/--typescript-server/--eslint-server/--node/--solution provisioning flags are removed. Servers own project selection through their initialization/settings. For services with an explicit readiness notification, configure readyNotification. The optional TypeScript diagnostic bridge remains an explicitly selected compatibility adapter.

Tool calls continue to select workspace/path dynamically. Historical backend/backendResults fields are retained as server provenance, not routing inputs. Consumers must continue to assess complete/freshness and findings separately.

## Reinstall and register

Build with Cargo or use an available native release/npm package. NuGet/.NET tool packages, .NET SDK build scripts, managed/native split and trim metadata are retired. The Rust host needs no .NET SDK/runtime. Individual servers still require their native tooling.

Run setup for your agent again with the profile path. JSONC registrations preserve unrelated settings/comments; Codex delegates configuration to its CLI. Guided setup creates/selects profiles and confirms registration. It no longer installs language-specific packages; install your chosen servers using their own instructions.

## Requalify

The workspace boundary now explicitly excludes .git, node_modules, target, artifacts and .next. Source files must be UTF-8; non-UTF-8 files fail rather than being silently misread. Reconciliation has declared file/count/size limits. See the service contract.

The original C# tests and packaging scripts were replaced by Rust protocol, lifecycle and actual MCP executable tests. Historical benchmark/results files and release notes are preserved as historical .NET evidence only. Coverage, mutation, performance, stability, third-party server compatibility and production readiness require fresh Rust evidence. Sharper remains pre-alpha.
