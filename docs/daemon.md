# Incremental analysis daemon

`sharplint check` starts a local daemon automatically and reuses it across CLI
invocations. SharpLint remains read-only: it never applies workspace changes to
source files. Executing configuration can build/restore its own file-based app,
just as in daemonless mode.

```sh
sharplint check --path ./repository
sharplint check --path ./repository --stats --format json
sharplint check --path ./repository --no-daemon
sharplint daemon start --path ./repository --idle-timeout 900
sharplint daemon status --path ./repository
sharplint daemon stop --path ./repository
```

The analysis root is the exact `--path` directory (current directory by default),
including its recursive C# files and its own `sharplint.config.cs`. This preserves
the existing CLI path/configuration boundary. Use the same repository root for
all commands; there is no automatic upward Git/solution discovery. Different
explicit analysis roots have separate daemons.

`--stats` adds a `statistics` object to JSON output, or a JSON statistics line to
pretty output. Existing diagnostic fields retain their meanings. `filesAnalyzed`
continues to count all source inputs, including reused documents and the config
script; `statistics.documentsAnalyzed` counts Naming cache misses, and
`documentsReused`/`cacheHits` count Naming cache hits. Custom module execution is
reported in rule execution time and is **not** represented by document hit counts.
Statistics include the validated snapshot version/input fingerprint,
reconciliation, workspace update, rule execution, aggregation, connection, and
CLI execution timing. They are optional and nondeterministic. End-to-end process
startup and JSON rendering costs are measured by the benchmark harness.

| Exit | Meaning |
| --- | --- |
| 0 | Analysis completed without Error diagnostics (Warning/Info do not fail), or lifecycle command succeeded |
| 1 | Analysis completed with Error diagnostics; also retained for command parser errors |
| 2 | Configuration, filesystem, or analysis failure; cancellation |
| 3 | Daemon startup, compatibility, or IPC failure |

Failures write a cause to stderr and do not emit a successful diagnostic payload.

## Process and IPC lifecycle

The CLI launches its installed assembly as a detached `--daemon-host` process;
it performs no analysis in the client during a normal daemon-backed check. The
host initializes a persistent Roslyn workspace; the first check loads configuration
and analyzes its initial snapshot. Explicit `daemon start` establishes readiness
without eagerly running executable configuration. Stopping cancels active requests,
kills an active configuration process tree, releases the workspace/pipe/lock, and
waits for host exit. The default idle timeout is 15 minutes; status/check activity
resets it. A host also exits when its analysis root disappears. A timeout supplied
to `daemon start` applies when a new host is started.

Identity hashes the canonical directory (resolving directory symlinks), local user,
OS/distribution, process architecture/runtime, WSL distribution, PATH, DOTNET_ROOT,
and NUGET_PACKAGES. Windows, WSL, users, and different selected package environments
therefore do not accidentally share caches. Startup and host ownership use separate
interprocess exclusive file handles in a private per-environment temp directory.
Concurrent starters recheck readiness while holding the startup lock. OS locks
release on crashes; lock files may remain and are deliberately not unlinked while
other clients could still hold them. The next check recreates a crashed daemon.
A connection that fails during a check returns an explicit failure; retry it.

IPC uses `NamedPipeServerStream`/`NamedPipeClientStream` with `CurrentUserOnly`,
never TCP. One request/response travels per connection as a 4-byte little-endian
length and UTF-8 JSON. Protocol v1 supports `Handshake`, `Check`, `Status`, and
`Shutdown`. Envelopes contain a request ID, protocol version, canonical root,
and implementation fingerprint. A SHA-256 fingerprint of the loaded Daemon, Analysis, Core, and Configuration
module identities detects rebuilds even when assembly versions stay unchanged.
Loaded module IDs describe the executing code even when DLL files are replaced
by a subsequent build. Incompatible checks fail explicitly. A same-protocol Shutdown accepts
an old build fingerprint so a rebuilt CLI can stop its prior host.

Requests are limited to 64 KiB, responses to 32 MiB, and JSON nesting to 32.
There are at most 16 active connections and a five-minute request deadline.
Messages with bad lengths, unknown operations, invalid request IDs, incompatible
versions, or another repository identity fail. Same-user executable code is not
an adversarial sandbox boundary. No request can select another analysis root or
supply a command to execute.

## Workspace, snapshots, and cache

`SharpLint.Analysis` owns the reusable `WorkspaceCoordinator`. Both daemon and
`--no-daemon` use the same compiler aggregation and Core `Analyzer.AnalyzeTreesAsync` rule engine; the latter
creates an independent coordinator and forces all document analysis.

The coordinator loads C# projects through a persistent MSBuildWorkspace, or uses
an AdhocWorkspace library compilation for a directory without projects. Compiler
and policy analysis share that solution. It tracks project references, compiler
options, imported build settings, metadata binaries and generated Compile inputs.
[Compiler diagnostics](compiler-diagnostics.md) describes collection, origins,
project identity, coordinates, invalidation, and build/generator limitations.
There is no full build or automatic restore on every check; restore projects first.
Direct source enumeration still excludes `bin`, `obj`, `.git`, `.sharplint` and
linked directories; materialized project/generated inputs are captured explicitly.

Each published immutable `WorkspaceSnapshot` contains its version, Roslyn Solution,
configuration hash, project-input hash, and complete tracked-input hash. Source
updates use `Solution.WithDocumentText`; additions/removals update DocumentIds.
No `TryApplyChanges`/source writes occur. Unchanged documents keep their syntax
state. Renames remove the old entry and analyze the new path. Only the current
solution, configuration, and one result per live document remain in memory.
There is no on-disk diagnostic cache. Compiler results are cached per live project;
source changes invalidate owning and transitively dependent projects. Unchanged
projects reuse their complete compiler diagnostic groups without reevaluation.

A Naming cache entry includes its document ID, content SHA-256 and diagnostics.
Configuration hash changes invalidate all Naming entries; project/build input
changes invalidate all entries conservatively. Rule implementation changes require
a compatible new host, so results from different implementations never mix.
Executable configuration is reused on an unchanged tracked snapshot and reevaluated
on *any* captured input change, because it can read other repository inputs.
If reevaluation emits the same configuration, unchanged Naming results remain reusable.
This deliberately costs more on edits than a declarative configuration format would.

Workspace mutations and publication are serialized. Concurrent equivalent requests
queue behind the current check and reuse its resulting cache without rerunning
Naming analysis. Every request independently validates freshness; there is no shared
cancellable analysis task. Canceling/disconnecting one request cancels only its work,
never the queued requests. Document work uses at most eight workers (bounded by
CPU count); lifecycle/status requests do not wait for analysis. Final diagnostics
are deduplicated as complete records and sorted by normalized path, coordinates,
project, origin, ID and stable metadata tie-breakers. Distinct project contexts
are preserved. Compiler errors do not stop syntax-based Naming analysis.

## Consistency and trust contract

Watcher events increment a coalescing change epoch; they never directly start
analysis. Events are hints. Every check enumerates and hashes all tracked inputs,
regardless of watcher state or file timestamps. Tracked inputs include C#,
project/solution files, props/targets, config/JSON/editorconfig/response files, and
ancestor Directory.Build/Directory.Packages/global.json/NuGet.config settings.
Before publishing, a second complete capture must match, and the watcher epoch
must remain stable. Renames, overflow/error events, and edits during analysis
cause a retry. After three unsuccessful observations, the check fails explicitly.
Failed/canceled attempts never replace the last successful cache or snapshot.

`validated: true` means these content observations agreed around analysis. It does
**not** assert an atomic OS filesystem snapshot or that the filesystem is still
unchanged when the client receives the response. An external writer can write
immediately after validation; the next check detects it. Without filesystem
transactions, an adversarial writer can also change and restore files between
observations. For strict point-in-time guarantees, run against an immutable
checkout or stop writers during validation. Never use the result as proof of
writes performed after its reported input fingerprint.

Configuration executes arbitrary C# under the current user through `dotnet run
--file`. Neither configuration nor in-process analyzers are sandboxed. For cacheable
configuration, author it deterministically from tracked repository inputs. External
files, network services, clock/random values, arbitrary nontracked file types, and
new environment values are not automatically dependency-tracked: use `--no-daemon`
or restart after such inputs change. The equality guarantee applies to the same
tracked source snapshot, effective configuration, and fixed analysis environment;
it does not make arbitrary C# configuration pure. Preserve this boundary when adding
new configuration features.

## Rule authors

`RuleDefinition.Scope` describes where a rule applies; it does not authorize cache
reuse. Built-in policies receive the syntax snapshot and available project
compilations; external modules use standard Roslyn analyzers against those compilations. Every configured custom module executes over the complete current
snapshot on **every** check, including unchanged checks. Unknown dependencies are
therefore conservatively broad. Missing registrations or analyzer exceptions fail
the check, including missing Hidden-rule registrations. The CLI loads external modules explicitly registered with `config.UseModule(...)`.
Custom hosts can also supply `RuleRegistry` to the coordinator. Referenced analyzer
assembly paths participate in input reconciliation.

Only built-in Naming has a known document/syntax dependency and gets document
caching. Compiler results separately use project dependency invalidation. Modules
may declare an error-free compilation prerequisite; unsatisfied prerequisites
produce explicit incomplete-analysis failures while retaining safe findings. There is intentionally no speculative dependency declaration API yet.
When adding one, distinguish execution scope from inputs that invalidate results,
default unknown dependencies to repository scope, include implementation/options/
semantic/project fingerprints in validity, and reject requirements the host cannot
supply. An incorrect dependency declaration can suppress violations; declarations
must be enforced by the host and covered by differential tests before narrowing
module invalidation.

## Troubleshooting

- **Stale/crashed process:** run `daemon status --path ...`, then retry `check`.
  Dead processes release ownership automatically. Normal `daemon stop` is idempotent.
- **Rebuilt/incompatible daemon:** run `daemon stop --path ...`, then `check` to
  start the current build. A different protocol may require stopping the old host
  with its matching binary or terminating the PID identified by that binary.
- **Startup failure:** verify the installed .NET runtime, root accessibility,
  permission to create local named pipes, and private permissions of the named
  SharpLint temp state directory. Status never starts a host.
- **Workspace/filesystem failure:** verify the directory exists and inputs can be
  read; run `check --no-daemon` to reproduce independently. Project load failures
  are analysis failures; verify restore assets and the installed SDK.
- **Invalid config:** stderr includes the `dotnet run --file` exit code/stdout/stderr.
  Verify SDK/package restore and `.Apply()`. The explicit `--file` prevents an
  adjacent `.csproj` from being run instead of the config script.
- **Unexpected cache behavior:** compare `--stats` with `--no-daemon --stats`;
  stop the host to clear all in-memory state. External config inputs need an
  independent check/restart. A changed configuration/project conservatively
  invalidates documents; an unchanged warm check should reuse all Naming results.

See [benchmark instructions and measured results](../benchmarks/README.md) and
[the differential/consistency tests](../tests/SharpLint.Tests/Daemon/WorkspaceCoordinatorTests.cs).

## Implementation and verification

The implementation adds `SharpLint.Analysis` (input capture, immutable snapshots,
workspace coordinator and document cache) and `SharpLint.Daemon` (identity, framed
protocol, client, host and terminal detachment). Core's analyzer now also accepts
already-captured syntax trees; `SourceFiles` is the shared source boundary.
Configuration loading explicitly selects file-based execution and terminates
canceled script process trees. CLI changes add routing, lifecycle commands,
`--no-daemon`, `--stats`, and explicit failure exit codes. The solution and test
project reference the new libraries. No Naming DSL or rule ID contract changes.

Regression coverage lives in `Analysis/SourceFilesTests.cs`,
`Daemon/WorkspaceCoordinatorTests.cs`, `Daemon/ProtocolTests.cs`,
`Cli/DaemonFunctionalTests.cs`, and the existing configuration/CLI test fixtures.
The differential tests compare entire Diagnostic records (ID, category, severity,
message, file/line/column, and suggestion) with fresh Core analysis, plus real CLI
JSON comparisons against independent `--no-daemon` checks.

Historical syntax-daemon baseline on .NET SDK 10.0.202 (Linux/WSL2), before compiler integration:

| Check | Result |
| --- | --- |
| Debug solution build | Passed, zero warnings/errors |
| Full suite | 271 passed, zero failures/skips |
| Core/Configuration combined coverage | 99.1% lines, 96.9% branches |
| Core mutation testing | 90.88%, passes the unchanged 90% gate |
| Configuration mutation testing | 92.07%, passes the unchanged 90% gate |
| Tool packaging | Includes Analysis, Daemon and Roslyn workspace runtime assemblies |

The existing coverage and mutation scripts measure Core and Configuration;
Analysis, Daemon, and CLI do not yet have mutation/coverage gates. Their behavior
is exercised by the in-process and subprocess tests above. Coverage HTML/JSON
and surviving mutation reports were inspected. Run mutation testing sequentially
with builds/full tests because Stryker rewrites shared build output. Rebuild Debug
before running subprocess tests after a mutation run.

Reproducible 100/1,000/10,000-file cold, warm, edit, configuration, project-input,
concurrency and restart measurements are in [the benchmark report](../benchmarks/README.md).
Performance targets remain unmet; this foundation prioritizes verified reuse and
explicit failures. Automatic upward repository
resolution, narrower declared module dependencies, general external configuration dependency
tracking, and sharing pending reconciliation across clients remain follow-up work.
