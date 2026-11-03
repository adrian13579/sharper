# Diagnostic connector service contract

## Responsibilities and project boundaries

Sharplint is an MCP diagnostic service for coding agents. Its executable starts MCP stdio and configures agent clients; diagnostic operations are MCP tools backed by persistent official Roslyn language-server sessions.

- `SharpLint.Analysis` owns LSP framing, request correlation, process/session lifecycle, disk synchronization, diagnostic normalization, freshness, scope, errors and cancellation. It uses only .NET libraries.
- `SharpLint.Cli` owns MCP stdio hosting, repository selection, bounded session pooling and Copilot configuration through ModelContextProtocol 2.3.0 and Microsoft.Extensions.Hosting 10.0.0. Its only production project dependency is Analysis.
- `SharpLint.Tests` verifies the connector through controlled transports, a fake server and a real multi-project Roslyn fixture. The fixture's custom analyzer is an ordinary external analyzer, not an application component.

Roslyn owns project evaluation, workspace loading, compilation, source generators and analyzer execution. Sharplint neither defines rules nor executes user-authored configuration programs. Users configure analyzers with native .NET mechanisms. The repository contains no separate rule engine, configuration DSL, analyzer module registry or compatibility libraries. Standard LSP diagnostic codes and normalized JSON fields are public contracts.

## Agent repository selection and session lifetime

`sharplint` and `sharplint mcp` start the MCP server. The executable also provides `sharplint setup copilot`; standalone diagnostic `check` and `status` commands have been removed. The two MCP tools remain `sharplint_check` and `sharplint_status`.

A connection can serve multiple repositories. Each tool accepts an absolute `workspace` and optional `solution`; checks also accept `path`. An explicit workspace selects the exact synchronization boundary. With an absolute check path, Sharplint discovers the Git repository (including a worktree's `.git` file), or the nearest solution/project directory when Git metadata is absent. Relative paths resolve inside the selected boundary. A solution must be inside that boundary.

For compatibility, `mcp --workspace` supplies a fallback when the client supplies no repository context. Explicit tool workspaces and absolute paths can override it. Older MCP clients advertising roots can provide current repository context when no explicit workspace/path is supplied. Roots are requested afresh on each such call and take precedence over a startup fallback. This adapter supports the older roots feature deprecated by the MCP SDK; explicit tool arguments work independently of that feature. Several distinct roots require an explicit repository selection. A context-free call fails with instructions instead of scanning the server's launch directory.

Each MCP process owns at most four contexts, keyed by normalized workspace and explicit solution path. Tool operations are serialized; request deadlines include repository-context requests and session queueing. Repeated calls reuse each context's `DiagnosticService` and Roslyn process. Selecting another repository retains previous contexts; exceeding capacity disposes the least recently used context. Server shutdown disposes every context. Separate MCP processes have independent pools.

`setup copilot` configures both clients by default; `--client cli` and `--client vscode` limit its scope. Copilot CLI uses `$COPILOT_HOME/mcp-config.json` or `~/.copilot/mcp-config.json`. VS Code uses its platform's default user-profile `mcp.json`; custom paths are accepted through `--copilot-config` and `--vscode-config`. Setup launches the running executable with `mcp`, without a fixed workspace. It preserves unrelated values and comments, replaces only the owned server entry, writes atomically per file, and checks all selected files before editing. Agent setup is an explicit configuration-writing utility; diagnostic tools remain read-only.

Configuration references: [Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers), [VS Code MCP configuration](https://code.visualstudio.com/docs/agents/reference/mcp-configuration).

## Tested server and protocol evidence

Tested tool: **roslyn-language-server 5.13.0-1.26514.10**, .NET runtime 10.0.6, SDK 10.0.202, Ubuntu 22.04 x64. The package's repository metadata identifies Roslyn commit [`d82b7f7dc6c203f66c41a23207e10bf462af896f`](https://github.com/dotnet/roslyn/tree/d82b7f7dc6c203f66c41a23207e10bf462af896f). Source inspection and an actual protocol probe confirmed:

| Mechanism | Observed support / Sharplint use |
| --- | --- |
| `initialize` | Workspace folders, UTF-16, dynamic diagnostic registration, configuration and progress capabilities |
| `initializationOptions.autoLoadProjects` | Supported; loads one root solution or discovers projects; Sharplint requests maximum 500 |
| `solution/open` | Roslyn extension used only for an explicit tool `solution` or fallback `--solution`, with automatic loading disabled |
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

All ordinary files inside the root are tracked. Only `.git` metadata is excluded by directory name; tool-named directories such as `.sharplint` are ordinary workspace inputs. In `bin`/`obj`, restored assets, DLLs, props, targets and editorconfig inputs are tracked; generated C# sources are Roslyn's responsibility. Symbolic links cause a clear failure rather than silently escaping the boundary. Directory enumeration and hashing are deliberately simple; large roots may cost more than the LSP pull itself.

Sharplint opens every tracked C# source in Roslyn so changes in another source can affect the requested document's semantic diagnostics. Versions increase only for changed text. Incremental mode sends one replacement edit spanning the previous complete document, using zero-based UTF-16 positions including CRLF handling. Full mode sends a complete replacement without a range. Documents close during reload/disposal. Native watcher notifications are forwarded when registered, with disk diffs resolving create/delete/rename sequences. Changes to the file set, project/configuration inputs, analyzer DLLs or restore assets conservatively reload the session and await project initialization again. Ordinary C# text edits preserve the same process.

External watched-file registrations are acknowledged, but this MVP only forwards/reconciles root-contained files. External project references, additional files, imports, package/SDK changes and parent-directory configuration are outside the freshness guarantee and require restart. Do not interpret root-scoped freshness as verification of changing external dependencies. Workspaces relying on symlinks are unsupported. Do not change installed toolchains or dependencies during a session.

A successful check establishes that root input contents matched across the LSP synchronization/pull interval, no relevant watcher event or outstanding refresh invalidated it, the connection remained alive, and each selected document returned a recognized report. It is not a filesystem transaction: an edit after the final reconciliation can naturally make a returned result stale. It is not a Roslyn build-completion barrier, and result IDs do not carry document version numbers. We rely on ordered LSP notifications followed by the server's completed document request; unsolicited pushes cannot provide that proof.

Responses distinguish `completed`, `incomplete`, `timedOut`, `failed` and `cancelled`. Only completed, recognized document pulls with stable observed root inputs have `complete: true` and `freshness: verified`. Unsupported pull, missing result IDs, observable loading errors, concurrent changes or refresh give uncertainty. Malformed/uncached unchanged results are errors. Cached diagnostic arrays are never returned as fresh without a new pull following reconciliation.

Requests are serialized to avoid interleaving versions. Deadlines include queueing, initialization, hashing, synchronization and analysis. Cancellation sends `$/cancelRequest`, closes the uncertain session and returns a cancellation status; cleanup has bounded shutdown/cancellation waits. An unexpected exit fails the current check and disposes the failed session. A subsequent request can start a new session; there is at most one repull for a diagnostic refresh within a check. Initial shutdown uses `shutdown` then `exit`, followed by process-tree termination if needed. LSP frame size is capped at 32 MiB and headers at 8 KiB.

## Diagnostic scope and normalization

`file` and `documents` refer to Roslyn's document pull reports for selected `.cs` files. Sharplint does not call `workspace/diagnostic` or claim full-solution analysis. Compiler syntax/semantic, ordinary document analyzer callbacks and custom analyzer callbacks are tested. Project-level or compilation-end findings and non-local diagnostics can be absent. The model supports location-free diagnostics for servers/reports that supply them; a parser unit test covers that representation, not real project-level retrieval.

Absolute file URIs normalize to local OS paths. Unknown/non-file URIs do not get invented paths. Source is retained only when supplied. Positions are zero-based UTF-16 throughout, in MCP structured responses. Sorting uses path, position, code and full serialized contents for deterministic ties. Only fully identical normalized findings are deduplicated; different related information, tags, source or data survive. LSP may already collapse findings across linked project contexts; Sharplint cannot reconstruct identities the server omits.

## Integration setup

```sh
dotnet tool install --tool-path /tmp/sharplint-tools roslyn-language-server --version 5.13.0-1.26514.10
dotnet build SharpLint.slnx -m:1
SHARPLINT_TEST_ROSLYN=/tmp/sharplint-tools/roslyn-language-server \
  dotnet test tests/SharpLint.Tests/SharpLint.Tests.csproj --filter FullyQualifiedName~SharpLint.Tests.Lsp -m:1
```

The fixture contains `TestSolution.slnx`, Domain, Application, Tests and CustomAnalyzer. Tests copy it to a temporary directory, restore dependencies and build the analyzer through ordinary .NET integration. They assert compiler errors, fix detection, SDK analyzer diagnostics, a custom analyzer finding, external/semantic edits, create/delete/rename, project-reference reload, process reuse/crash and actual SDK MCP stdio calls. They never edit a caller's code. If the server is unavailable, leave the tests in place and report explicit skips; do not substitute fake-server results for these integration tests.
