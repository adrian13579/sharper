# Feedback and verification service contract

Sharper is an MCP stdio server and an LSP stdio client. External servers own semantics, project loading, rules, analyzers and native configuration. Sharper owns process lifecycle, synchronization, normalization and scoped verification. It does not edit source, install project dependencies, implement a rule engine or proxy arbitrary LSP commands.

## Language-agnostic routing

Load an explicit `--lsp-config` JSON profile at startup. Its `servers` array maps file extensions to exact LSP language IDs and commands. Every matching server contributes diagnostics. Navigation chooses the first configured, navigation-enabled server advertising the requested operation. Server IDs are provenance metadata, never a tool-call routing parameter. No repository configuration is automatically executed. Restart MCP after profile/toolchain/dependency changes.

The five tools are `sharper_check`, `sharper_status`, `sharper_definition`, `sharper_implementation` and `sharper_references`. Calls take an absolute `workspace` and file/directory `path`; omitted check paths cover configured workspace documents. Absolute source paths discover a Git root or their containing directory. Relative paths require an explicit workspace, a single client root, or `mcp --workspace` fallback. Client roots are requested fresh when necessary; multiple roots require explicit selection. Paths cannot escape the workspace.

## Protocol and lifecycle

MCP uses newline-delimited UTF-8 JSON-RPC on stdout; stderr is for logging. Supported negotiation versions are 2025-03-26, 2025-06-18 and 2025-11-25. LSP uses Content-Length framing, bounded headers/messages and standard initialize/initialized/shutdown/exit exchange. Only UTF-16 positions are supported.

Servers must support open/close and full (1) or incremental (2) synchronization. Sharper opens every mapped source within the workspace, including dependencies of requested files. Source edits increase document versions and preserve warm sessions. Incremental synchronization replaces the prior complete text with its correct UTF-16 range, including CRLF handling. Membership or tracked non-source configuration changes reload the session.

Up to four workspace/server contexts stay warm in an LRU pool. Eviction, cancellation and shutdown dispose contexts. Server processes are placed in Windows jobs or Unix process groups so descendants are terminated too. All discovery, initialization, queueing, synchronization and analysis share the request deadline (default 120 seconds, maximum one hour). Cleanup has separate bounded shutdown/kill waits. Disconnection is explicit; a later request may start a fresh context.

Generic initialization is not proof of project readiness. An optional `readyNotification` declares a server-specific readiness barrier, with the same deadline. Native project readiness and the meaning/completeness of the server's diagnostics remain the server's responsibility.

## Diagnostic evidence

Standard `textDocument/diagnostic` pulls use the default report without a provider identifier. Full reports must contain valid items; resultId is optional unless the profile explicitly requires it as server-specific document-recognition evidence. Unchanged reports require a matching cached ID. Push-only feedback cannot establish verified results. A closed compatibility adapter for TypeScript's synchronous diagnostic bridge is available by explicit profile configuration; it permits only syntax and semantic diagnostic requests, never arbitrary commands.

Sharpening the code means **baseline, edit, check, fix, verify**. For current analysis require `status: completed`, `complete: true` and `freshness: verified`. Inspect findings separately: completion can include errors. Empty findings do not prove a successful build/test run, exhaustive analysis or execution of every rule.

Each check hashes workspace files before/after synchronization and analysis. Watchers are conservative hints: concurrent events or disk differences invalidate results. A request can repull once for diagnostic refresh; repeated refresh or capability changes make it incomplete. Error-level server messages, malformed reports, cancellation and failures never become verified empty findings. Combined results require every selected server to complete with current inputs.

The boundary excludes `.git`, `node_modules`, `target`, `artifacts` and `.next` directories. Symlinks, special filesystem entries and non-UTF-8 source text are unsupported. Reconciliation is limited to 100,000 files, 32 MiB per file and 512 MiB total; exceeding a limit fails explicitly. Non-UTF-8 filesystem paths are rejected explicitly. External dependencies, excluded inputs, SDKs, installed servers and unsaved buffers are outside the freshness guarantee. Restart when they change. An edit after the final reconciliation may naturally stale the result; the check is not a filesystem transaction.

## Normalized output

Diagnostics preserve codes, severities (including unknown numeric values), optional source, related information, tags, opaque data and help links. Paths are normalized absolute paths; positions are zero-based UTF-16. Findings are deterministically ordered and only identical complete records are deduplicated. Combined findings retain their configured server identity. Historical `backend` / `backendResults` JSON fields remain for wire compatibility as provenance only; consumers do not select an implementation.

Navigation retains Location and LocationLink ranges, external/generated URIs and optional origin ranges. Non-file targets have no invented filesystem path. Unsupported capabilities return unsupported/uncertain, rather than verified empty results. No cross-runtime symbol graph is constructed.

`sharper_status` reports availability and advertised capabilities, not diagnostic verification. Unsupported server-to-client requests receive method-not-found; workspace edits are rejected. `workspace/configuration` returns configured settings by dotted section lookup, and settings are also sent with didChangeConfiguration.

See [configuration](custom-language-servers.md), [feedback](feedback.md) and [compatibility](compatibility.md).
