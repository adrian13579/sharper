# Compiler integration engineering report

The compiler feature extends the existing `WorkspaceCoordinator` and daemon. A
check aggregates Roslyn compilation diagnostics and existing policy findings for
one validated snapshot. It preserves `CS*` IDs, effective compiler severities,
messages, physical coordinates, project contexts, and Naming IDs. Safe policies
continue to execute when compiler errors exist. No target source edits are made.

## Validation boundary

At the user's request, final validation uses the isolated source snapshot at
`/tmp/sharplint-compiler-validation`, excluding a separate module-catalog refactor
that began concurrently in the shared workspace. That refactor's changes were
preserved. Results below apply to this compiler-feature snapshot and its earlier
module contracts; they do not claim that the combined shared working tree passes.

The isolated snapshot includes the pre-existing daemon work, this feature, and
Roslyn 5.3 aligned with the .NET 10 SDK compiler generation. A fresh Debug build is
required before reproducing CLI tests. SDK: 10.0.202, runtime: 10.0.6, Linux/WSL2.

The isolated [source manifest](/tmp/sharplint-compiler-validation/source-manifest.json)
identifies 87 code/build/test inputs with aggregate SHA-256
`5cd929367e06cb508a4590fc00d9d9952c6ec34f88a6525fd9c2ce71a2a5645e`.
Reproduce from that directory using `bash coverage-test.sh` and
`bash mutation-test.sh all --config-file compiler-mutation-config.json`, sequentially,
then rebuild Debug before CLI tests. The mutation configuration is preserved in
the isolated snapshot.

## Implementation and changed files

Paths are relative to the repository. Existing daemon changes already present at
task start were extended and preserved, without a second workspace pipeline.

| Area | Files changed or created by this feature |
| --- | --- |
| Unified model | `src/SharpLint.Core/Diagnostics/Diagnostic.cs`, `DiagnosticOrigin.cs` |
| Analysis prerequisites | `src/SharpLint.Core/Analysis/AnalysisContext.cs`, `AnalysisResult.cs`, `Analyzer.cs`; `src/SharpLint.Core/Rules/RuleContracts.cs` |
| Project loading and capture | `src/SharpLint.Analysis/ProjectWorkspace.cs`, `InputSnapshot.cs`, `WorkspaceCoordinator.cs`, `AnalysisSnapshot.cs` |
| Conversion and ordering | `src/SharpLint.Analysis/CompilerDiagnosticAdapter.cs`, `DiagnosticAggregator.cs` |
| CLI rendering and statistics | `src/SharpLint.Cli/Commands/CheckCommand.cs` |
| Compiler/runtime dependencies | `src/SharpLint.Core/SharpLint.Core.csproj`, `src/SharpLint.Analysis/SharpLint.Analysis.csproj`, `tests/SharpLint.Tests/SharpLint.Tests.csproj` |
| Tests | `tests/SharpLint.Tests/Daemon/CompilerDiagnosticsTests.cs`, `WorkspaceCoordinatorTests.cs`; `tests/SharpLint.Tests/Cli/CompilerFunctionalTests.cs`, `DaemonFunctionalTests.cs`; `tests/SharpLint.Tests/Analysis/TaxonomyAnalyzerTests.cs` |
| Documentation | `README.md`, `docs/daemon.md`, `docs/spec/taxonomy.md`, `docs/compiler-diagnostics.md`, this report |
| Measurements | `benchmarks/daemon.py`, `benchmarks/compiler-results.json`, `benchmarks/README.md` |

`ProjectWorkspace` loads recursive C# projects and project references through
MSBuildWorkspace. The coordinator updates captured Roslyn documents and calls
`Project.GetCompilationAsync` followed by `Compilation.GetDiagnostics`, without
executing a full build on every check. Compiler configuration remains Roslyn's
responsibility, including suppressed warnings and warnings-as-errors. Loose
directories use a synthetic framework-referenced library. Executable configuration
and unowned sources stay policy inputs rather than a second target program.

The existing diagnostic record gains origin, project, help URL, optional source
span and explicit source-location presence. Existing JSON fields retain their
meanings. Lines and UTF-16 columns are one-based; spans are zero-based physical
UTF-16 offsets/lengths. Locationless findings serialize null location fields.
Exact duplicate records are removed; project contexts remain distinct. Sorting
uses normalized path, coordinates, project, origin, ID and stable metadata ties.

Compiler results live in the coordinator's cache per project. Source edits
invalidate every owning context and its transitive project dependents through
Roslyn's dependency graph. Unaffected groups are reused. Project membership,
options, imports, assets, generated inputs or reference binaries cause conservative
reloads. Metadata images are immutable, and input content hashes detect changes
even when paths, file sizes and timestamps are unchanged. Newly discovered
dependencies require pre-load observation and retry. Failed/canceled/unstable
attempts never replace the published snapshot or return a stale cached success.

## CLI behavior

```sh
sharplint check --format json --stats
sharplint check --no-daemon --format json
```

Both routes use the same coordinator and aggregation. Pretty output identifies
`[Compiler]` and `[SharpLint]`. JSON adds `origin`, `project`, `helpUrl`,
`spanStart`, and `spanLength` to diagnostic objects, plus top-level `status` and
`failures`. For example:

```json
{
  "ruleId": "CS0103",
  "origin": "compiler",
  "category": "Compiler",
  "severity": "error",
  "message": "The name 'missing' does not exist in the current context",
  "file": "/repo/App.cs",
  "line": 1,
  "column": 26,
  "project": "/repo/App.csproj::App",
  "spanStart": 25,
  "spanLength": 7
}
```

Errors return exit 1, including ordinary C# compilation errors; nonblocking
warnings/info return 0. Configured warning promotion is respected. Incomplete
analysis or configuration/analysis failures return 2; existing IPC failures retain
exit 3. `ErrorFreeCompilation` rules report an explicit prerequisite failure when
unsafe to run, retaining compiler and safe policy findings. Syntax rules remain
the default. The earlier module contract still exposes syntax inputs, without
semantic models.

## Tests and differential checks

The isolated coverage run passed **293 tests, zero failures/skips** in about
2 minutes 30 seconds. Relevant compatibility and CLI checks also passed in focused
runs. The final post-mutation Debug rebuild passed with zero warnings/errors. The
complete suite then passed **294 tests, zero failures/skips**, in 3 minutes
54 seconds, including the final external/nested-ruleset regression. The tool
package contains the MSBuild .NET build host, and packaged library hashes match
the rebuilt assemblies. [Machine-readable results](compiler-integration-validation.json)
record the completed checks.

Mutation testing measures the entire Core and Configuration production scopes,
using unit/analyzer tests. Its test filter excludes CLI functional and Daemon
workspace/protocol integration cases, which run in the complete suite separately.
This avoids repeatedly loading MSBuild workspaces for Core mutations; no production
code or mutants were newly excluded, and thresholds remain 90%. Core retained the
same 344 mutants and three uncovered mutants as the broader test selection.
Core scored **91.20%** (258 killed, one timeout, 22 survived, three uncovered;
compile errors and default ignored mutants excluded by Stryker). Surviving mutants were inspected in JSON reports; HTML reports are available; the new compilation-prerequisite logic has no
surviving mutants. Configuration scored **92.07%** (150 killed, one timeout, 12 survived and one
uncovered). Both measured projects pass the unchanged 90% requirement.

Tests exercise syntax errors, unknown symbols/types, conversions, method calls,
generic constraints, accessibility, warnings and warnings-as-errors, suppression,
locationless errors, origins, spans, serialization, stable ordering, aggregation,
multiple project contexts and unchanged Naming behavior. C# 14 syntax, a downgrade
to C# 13, nullable changes and same-path/same-size/same-timestamp binary replacement
have explicit regression coverage.

Differential scenarios compare full diagnostic records or CLI JSON with a fresh
complete analysis of the same snapshot. Covered transitions include warm reuse,
body errors and fixes, cross-file API changes, dependent projects, shared source,
reference removal, warning/compiler options, external compiler rulesets and nested
includes, imported configuration, optional
external import creation/deletion, generated source edits, file additions,
deletions/renames, concurrent checks, retries and recovery after failed loading.
IDs, origins, categories, effective severity, messages, project context, locations,
help URLs and spans participate in equality; timings and snapshot counters do not.

Core and Configuration coverage: **99.2% lines, 96.9% branches**. HTML and JSON
reports were inspected. Remaining uncovered lines are existing fallback cases in
Naming evaluation, deferred Naming builders and a configuration-loader failure
path. Analysis/Daemon/CLI are outside the repository's current coverage and mutation
gates; their integration/subprocess tests still execute in the full suite.

## Exploratory performance comparison

The existing syntax-only baseline is preserved in `benchmarks/results.json`.
The compiler-enabled 100-file sample is in `benchmarks/compiler-results.json`:
three measured samples, one warmup, Debug, same SDK/host class. These are small
shared-host samples with different restore/build-cache histories and warmup counts,
not controlled statistical regression estimates. Projects are restored before
checking in the updated harness; executable-config restore remains part of cold
checks. Materialized SDK inputs increase document contexts from 101 to 103.

| Scenario, 100 files | Earlier syntax median ms | Compiler median ms |
| --- | ---: | ---: |
| Cold | 1319.03 | 4802.32 |
| Warm | 225.01 | 311.62 |
| One-file edit | 498.58 | 576.60 |
| Ten-file edit | 511.02 | 591.30 |
| Policy configuration | 551.87 | 630.87 |
| Project/build input | 1544.23 | 3095.37 |
| Concurrent client | 205.52 | 329.35 |
| Restart and check | 1718.13 | 4901.22 |

Warm checks reused all 103 document contexts and the compiler group, with zero
document/project evaluations. One-file edits analyzed one document and one project.
Policy-only configuration changes reused compiler diagnostics. Peak daemon memory
was approximately 200 MiB after warm checks and 283 MiB after the later concurrent
scenario; this excludes child processes. Cold/reload cost grows with design-time
project loading, dependency discovery and immutable metadata capture. The earlier
latency goals remain unmet; correctness and cache validity take priority.

## Limitations and follow-up

Restore projects before checking. MSBuildWorkspace uses design-time targets and
may write SDK intermediate files; project/configuration code is trusted, not
sandboxed. SharpLint does not apply source writes. It does not execute project
analyzers/source generators, full builds, emit validation or arbitrary custom build
targets as a substitute for build validation. Already materialized generated Compile
inputs participate in compilation; policy traversal continues to skip build folders.
Missing generator-produced types can produce compiler errors. Solution configuration
mappings are not selected, and a directory without projects cannot infer packages,
target frameworks or executable output. A changed SDK selection requires restart.

Source capture uses UTF-8 with BOM detection; legacy code-page overrides are not
modeled and require separate build validation.

The existing two-observation snapshot boundary and external executable-configuration
limitations remain documented in [the daemon guide](daemon.md). Changes derived
from time, network, arbitrary property functions or untracked external configuration
inputs require fresh analysis/environment control. The compiler feature does not
validate the concurrent module refactor.

Follow-up work should prioritize broader performance samples, reducing repeated
metadata/content-capture allocations while preserving content validation, generator
and analyzer support with explicit freshness contracts, and semantic inputs for
future policy providers. See [the diagnostic contract](compiler-diagnostics.md)
for detailed assumptions and examples.

## Preserved artifacts

- [Isolated source archive](/tmp/sharplint-compiler-feature-source.tar.gz)
- [Source manifest](/tmp/sharplint-compiler-validation/source-manifest.json)
- [Coverage HTML](/tmp/sharplint-compiler-validation/CoverageReport/index.html) and [summary](/tmp/sharplint-compiler-validation/CoverageReport/Summary.json)
- [Core mutation JSON](/tmp/sharplint-compiler-validation/StrykerOutput/Core/reports/mutation-report.json)
- [Configuration mutation JSON](/tmp/sharplint-compiler-validation/StrykerOutput/Configuration/reports/mutation-report.json)

The source archive excludes binaries, caches and the concurrent module refactor.
It includes the temporary mutation configuration and an empty local package-feed
directory so validation can be reproduced after extraction and restore.
