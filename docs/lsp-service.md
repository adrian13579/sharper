# Migration and service contract

## Repository inspection and migration plan

The pre-migration repository had six production projects: Core (Roslyn-backed rules, diagnostics and syntax evaluation), Configuration (executable configuration and fluent DSL), Analysis (MSBuildWorkspace, compilation/reference snapshots and diagnostic caches), Daemon (local process/RPC lifecycle), Daemon.Client (protocol/client), and Cli (dispatch/rendering and AOT packaging). Tests covered those paths. It used .NET 10.0.202, Roslyn 5.3.0, Microsoft.Build 17.14.28, Microsoft.Build.Locator 1.5.5, and Spectre.Console 0.49.1.

The adopted migration plan was:

1. Verify the official server's actual protocol and runtime; select the official MCP SDK.
2. Replace Analysis's workspace/compiler/cache implementation with a small LSP transport, persistent session, disk reconciliation and normalized responses.
3. Route CLI and MCP through that same service; remove the obsolete daemon transport and native-AOT/managed-daemon deployment coupling.
4. Retain Core/Configuration public library APIs and isolated library tests; remove the obsolete daemon/CLI tests and replace them with protocol, service and real integration tests.
5. Document scoped completeness, synchronization boundaries, setup, tests and repeatable baseline measurement.

The active dependency graph is `Cli -> Analysis` plus ModelContextProtocol 2.3.0 and Microsoft.Extensions.Hosting 10.0.0 in Cli. Analysis uses only .NET libraries. Core and Configuration are independent compatibility libraries and are not loaded by the service. No built-in DSL behavior or IDs changed.

## Tested server and protocol evidence

Tested tool: **roslyn-language-server 5.13.0-1.26514.10**, .NET runtime 10.0.6, SDK 10.0.202, Ubuntu 22.04 x64. The package's repository metadata identifies Roslyn commit [`d82b7f7dc6c203f66c41a23207e10bf462af896f`](https://github.com/dotnet/roslyn/tree/d82b7f7dc6c203f66c41a23207e10bf462af896f). Source inspection and an actual protocol probe confirmed:

| Mechanism | Observed support / SharpLint use |
| --- | --- |
| `initialize` | Workspace folders, UTF-16, dynamic diagnostic registration, configuration and progress capabilities |
| `initializationOptions.autoLoadProjects` | Supported; loads one root solution or discovers projects; SharpLint requests maximum 500 |
| `solution/open` | Roslyn extension used only for explicit `--solution`, with automatic loading disabled |
| `workspace/projectInitializationComplete` | Roslyn extension used as loading barrier; no guessed sleep marks loading complete |
| `textDocumentSync` | `openClose: true`, `change: 2` (incremental); full-sync mode is also implemented and tested with the controlled server |
| `client/registerCapability` | Registers compiler/analyzer/document diagnostic sources and watched-file patterns |
| `textDocument/diagnostic` | Used with no identifier: server aggregates enabled sources; requires an LSP-open document |
| Diagnostic `resultId` | Full reports retained; matching unchanged reports reuse diagnostics only after current synchronization and pull |
| `workspace/diagnostic` | Advertised for some providers; not used in the MVP (long-polling, closed-document/analysis-scope semantics) |
| `workspace/diagnostic/refresh` | Acknowledged; a refresh causes one bounded repull; continued refresh makes the check incomplete |
| `textDocument/publishDiagnostics` | Versioned pushes accepted, older versions ignored; pushes never establish verified freshness |
| Server requests | Configuration, progress creation, registration/unregistration, workspace folders, message requests; edits rejected |

Primary implementation references at the tested commit:

- [Automatic loading](https://github.com/dotnet/roslyn/blob/d82b7f7dc6c203f66c41a23207e10bf462af896f/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/HostWorkspace/AutoLoadProjectsInitializer.cs)
- [Explicit solution loading](https://github.com/dotnet/roslyn/blob/d82b7f7dc6c203f66c41a23207e10bf462af896f/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/HostWorkspace/OpenSolutionHandler.cs)
- [Project loading completion](https://github.com/dotnet/roslyn/blob/d82b7f7dc6c203f66c41a23207e10bf462af896f/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/HostWorkspace/LanguageServerProjectSystem.cs)
- [Document diagnostics](https://github.com/dotnet/roslyn/blob/d82b7f7dc6c203f66c41a23207e10bf462af896f/src/LanguageServer/Protocol/Handler/Diagnostics/DocumentPullDiagnosticsHandler.cs)
- [Source aggregation for a null identifier](https://github.com/dotnet/roslyn/blob/d82b7f7dc6c203f66c41a23207e10bf462af896f/src/LanguageServer/Protocol/Handler/Diagnostics/DiagnosticSourceProviders/DiagnosticSourceManager.cs)
- [Dynamic registration](https://github.com/dotnet/roslyn/blob/d82b7f7dc6c203f66c41a23207e10bf462af896f/src/LanguageServer/Protocol/Handler/Diagnostics/DocumentPullDiagnosticsHandler_IOnInitialized.cs)
- [Workspace long polling](https://github.com/dotnet/roslyn/blob/d82b7f7dc6c203f66c41a23207e10bf462af896f/src/LanguageServer/Protocol/Handler/Diagnostics/AbstractWorkspacePullDiagnosticsHandler.cs)
- [Non-local analyzer scope restriction](https://github.com/dotnet/roslyn/blob/d82b7f7dc6c203f66c41a23207e10bf462af896f/src/LanguageServer/Protocol/Handler/Diagnostics/PublicDocumentNonLocalDiagnosticSourceProvider.cs)

Automatic loading is documented upstream. Explicit selection and the readiness notification are Roslyn-specific extensions, necessary because standard LSP lacks a solution-load request and a project-system-ready barrier. Versions without these mechanisms are unsupported; startup fails or times out explicitly. Their presence does not guarantee successful dependency/analyzer loading.

## Consistency contract

The synchronization boundary is the configured root. Each check fully reads/hashes relevant files before and after analysis; watcher events are coalesced per path and debounced as hints. A watcher overflow triggers reconciliation and session reload. Missing notifications do not bypass the before/after scan.

All ordinary files inside the root are tracked. `.git` and `.sharplint` are excluded. In `bin`/`obj`, restored assets, DLLs, props, targets and editorconfig inputs are tracked; generated C# sources are Roslyn's responsibility. Symbolic links cause a clear failure rather than silently escaping the boundary. Directory enumeration and hashing are deliberately simple; large roots may cost more than the LSP pull itself.

SharpLint opens every tracked C# source in Roslyn so changes in another source can affect the requested document's semantic diagnostics. Versions increase only for changed text. Incremental mode sends one replacement edit spanning the previous complete document, using zero-based UTF-16 positions including CRLF handling. Full mode sends a complete replacement without a range. Documents close during reload/disposal. Native watcher notifications are forwarded when registered, with disk diffs resolving create/delete/rename sequences. Changes to the file set, project/configuration inputs, analyzer DLLs or restore assets conservatively reload the session and await project initialization again. Ordinary C# text edits preserve the same process.

External watched-file registrations are acknowledged, but this MVP only forwards/reconciles root-contained files. External project references, additional files, imports, package/SDK changes and parent-directory configuration are outside the freshness guarantee and require restart. Do not interpret root-scoped freshness as verification of changing external dependencies. Workspaces relying on symlinks are unsupported. Do not change installed toolchains or dependencies during a session.

A successful check establishes that root input contents matched across the LSP synchronization/pull interval, no relevant watcher event or outstanding refresh invalidated it, the connection remained alive, and each selected document returned a recognized report. It is not a filesystem transaction: an edit after the final reconciliation can naturally make a returned result stale. It is not a Roslyn build-completion barrier, and result IDs do not carry document version numbers. We rely on ordered LSP notifications followed by the server's completed document request; unsolicited pushes cannot provide that proof.

Responses distinguish `completed`, `incomplete`, `timedOut`, `failed` and `cancelled`. Only completed, recognized document pulls with stable observed root inputs have `complete: true` and `freshness: verified`. Unsupported pull, missing result IDs, observable loading errors, concurrent changes or refresh give uncertainty. Malformed/uncached unchanged results are errors. Cached diagnostic arrays are never returned as fresh without a new pull following reconciliation.

Requests are serialized to avoid interleaving versions. Deadlines include queueing, initialization, hashing, synchronization and analysis. Cancellation sends `$/cancelRequest`, closes the uncertain session and returns a cancellation status; cleanup has bounded shutdown/cancellation waits. An unexpected exit fails the current check and disposes the failed session. A subsequent request can start a new session; there is at most one repull for a diagnostic refresh within a check. Initial shutdown uses `shutdown` then `exit`, followed by process-tree termination if needed. LSP frame size is capped at 32 MiB and headers at 8 KiB.

## Diagnostic scope and normalization

`file` and `documents` refer to Roslyn's document pull reports for selected `.cs` files. SharpLint does not call `workspace/diagnostic` or claim full-solution analysis. Compiler syntax/semantic, ordinary document analyzer callbacks and custom analyzer callbacks are tested. Project-level or compilation-end findings and non-local diagnostics can be absent. The model supports location-free diagnostics for servers/reports that supply them; a parser unit test covers that representation, not real project-level retrieval.

Absolute file URIs normalize to local OS paths. Unknown/non-file URIs do not get invented paths. Source is retained only when supplied. Positions are zero-based UTF-16 throughout, including pretty CLI output. Sorting uses path, position, code and full serialized contents for deterministic ties. Only fully identical normalized findings are deduplicated; different related information, tags, source or data survive. LSP may already collapse findings across linked project contexts; SharpLint cannot reconstruct identities the server omits.

## Integration setup

```sh
dotnet tool install --tool-path /tmp/sharplint-tools roslyn-language-server --version 5.13.0-1.26514.10
dotnet build SharpLint.slnx -m:1
SHARPLINT_TEST_ROSLYN=/tmp/sharplint-tools/roslyn-language-server \
  dotnet test tests/SharpLint.Tests/SharpLint.Tests.csproj --filter FullyQualifiedName~SharpLint.Tests.Lsp -m:1
```

The fixture contains `TestSolution.slnx`, Domain, Application, Tests and CustomAnalyzer. Tests copy it to a temporary directory, restore dependencies and build the analyzer through ordinary .NET integration. They assert compiler errors, fix detection, SDK analyzer diagnostics, a custom analyzer finding, external/semantic edits, create/delete/rename, project-reference reload, process reuse/crash and actual SDK MCP stdio calls. They never edit a caller's code. If the server is unavailable, leave the tests in place and report explicit skips; do not substitute fake-server results for these integration tests.
