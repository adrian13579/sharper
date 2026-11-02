# LSP migration validation

Validation was performed in the dedicated `feat/lsp-mcp` worktree with SDK **10.0.202**, runtime **10.0.6**, official Roslyn language server **5.13.0-1.26514.10**, and official MCP SDK **2.3.0**, on Ubuntu 22.04 x64.

## Build and tests

Commands run from the worktree root:

```sh
dotnet build SharpLint.slnx -t:Rebuild -m:1
SHARPLINT_TEST_ROSLYN=/tmp/sharplint-tools/roslyn-language-server \
  dotnet test SharpLint.slnx --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=lsp-final.trx' --results-directory TestResults/final
bash mutation-test.sh
```

- Clean Debug rebuild: **passed**, zero warnings and errors.
- Full suite: **439 passed, 0 failed, 0 skipped**; test duration **1 min 26 s**. TRX: `TestResults/final/lsp-final.trx`.
- Core mutation: **91.90%**, above the required 90%; 522 killed, 43 survived, 3 uncovered (62 ignored by Stryker's existing block-coverage filtering, 74 compile errors).
- Configuration mutation: **92.70%**, above the required 90%; 251 killed, 19 survived, 3 timed out, 1 uncovered (83 ignored by existing block-coverage filtering, 15 compile errors). The instrumented mutant timeouts are counted as detected by Stryker; the ordinary suite had no timeouts.
- No production mutation exclusions or threshold reductions were introduced. The filter excludes new LSP tests, which do not execute the retained Core/Configuration libraries. The new Analysis project was not mutation-scored.

Surviving mutants were inspected in `StrykerOutput/{Core,Configuration}/reports/mutation-report.json` (matching HTML reports are available). Core survivors concentrate in the retained analyzer engine, symbol extraction, naming precedence and legacy policy paths. Configuration survivors concentrate in executable configuration loading and legacy module assembly resolution, with several message-only changes. These compatibility libraries are outside the active service pipeline.

## Real integration assertions

The initial protocol probe returned real compiler **CS0103** and SDK analyzer **CA1822** diagnostics. Final real-server tests asserted:

- Compiler errors, external fixes and repeated checks in one persistent process.
- SDK analyzer **CA1822** and custom analyzer **FIXTURE001** through a normal analyzer ProjectReference.
- Cross-project semantic updates (**CS0029**), source creation/deletion/rename, and removed project references (**CS0246/CS0234**).
- Unexpected process termination produces failed/uncertain results; the next request starts a fresh working session.
- The actual MCP stdio entry point exposes exactly two tools and returns structured diagnostics, completion/freshness metadata and a reused process identity.
- The actual CLI returns parsed diagnostic JSON and error exit code 1.

Controlled transport/server tests additionally cover byte framing, out-of-order correlation, dynamic capabilities, malformed responses, push-only limitations, old pushes, result IDs, empty results, synchronization versions and UTF-16 ranges, watcher deduplication/overflow, concurrent disk changes, active/queued cancellation, deadlines and shutdown/process reaping. Location-free diagnostics are covered at the model/parser layer; real project-level retrieval is outside the document scope.

The sandbox denied local sockets for VSTest and Roslyn's build host. Final tests ran with that required access. An earlier attempt overlapped mutation testing with the full suite and conflicted with rewritten output files; the final rebuild and test run were sequential and passed. Development runs that exposed uncached unchanged reports, initial refresh handling and cold-baseline issues were corrected before the final run.

## Coverage

```sh
SHARPLINT_TEST_ROSLYN=/tmp/sharplint-tools/roslyn-language-server \
  bash coverage-test.sh --no-build --no-restore
```

Passed again: **439 tests, zero failures/skips**, duration **1 min 23 s**.
`CoverageReport/index.html`, `Summary.json` and `Cobertura.xml` were inspected.

| Assembly | Line coverage | Branch coverage |
| --- | ---: | ---: |
| SharpLint.Analysis (new bridge) | 95.2% | 79.2% |
| SharpLint.Core (retained library) | 99.3% | 97.6% |
| SharpLint.Configuration (retained library) | 99.1% | 95.5% |
| Overall | 97.8% | 88.6% |

Remaining bridge gaps include default PATH/global-tool discovery (real tests use an explicit server path), static capability advertisement (the tested server dynamically registers), symbolic-link rejection and several exceptional/disposal paths. CLI/MCP child processes are tested through returned data, but their subprocess execution is outside the in-process coverage report. Mutation testing remains a separate requirement and passed for both measured compatibility projects.

## Baseline measurements

```sh
SHARPLINT_TEST_ROSLYN=/tmp/sharplint-tools/roslyn-language-server \
  dotnet run -c Release --project benchmarks/SharpLint.Benchmarks -- 20
```

All **125 measurements completed**, with zero incomplete/failed final results. Five process-cold starts and 20 samples per warm scenario were recorded. Context: **Intel Core i9-12900HK**, 20 logical processors visible to .NET, Ubuntu **22.04.5 LTS** x64, **.NET 10.0.6** and SDK **10.0.202**. Coverage and mutation testing had finished before measurement. OS/NuGet caches were warm; this is a small fixture, not a large-repository benchmark.

| Scenario | Samples | Median (ms) | P95 (ms) |
| --- | ---: | ---: | ---: |
| initialLoad | 5 | 2648.11 | 2759.21 |
| unchangedFile | 20 | 160.15 | 231.67 |
| syntaxEdit | 20 | 284.29 | 426.12 |
| semanticEdit | 20 | 340.52 | 358.41 |
| multipleChangedFiles | 20 | 902.41 | 952.51 |
| repeatedWarmCall | 20 | 3.14 | 84.28 |
| projectReferenceReload | 20 | 4756.76 | 4886.09 |

The tracked [raw results](../benchmarks/lsp-results.json) contain all timings and attempt counts. Timings include reconciliation/debounce and any bounded incomplete-result retries. Scenarios execute in the harness's documented sequence: unchanged-file samples follow the prior iteration's project reload, while repeated warm calls follow batch analysis. Native post-load work and diagnostic refresh therefore affect these distributions differently. Cold loading is readiness time, separate from first diagnostic analysis. Project-reference timings include a new server session. No performance target is asserted.

## Packaging

```sh
dotnet pack src/SharpLint.Cli -c Release -o artifacts/packages -m:1
dotnet tool install sharplint --version 0.2.0-alpha \
  --tool-path /tmp/sharplint-packaged --configfile /tmp/sharplint-package-NuGet.config
# In the temporary probe project directory:
/tmp/sharplint-packaged/sharplint check Test.cs --workspace /tmp/sharplint-probe \
  --server /tmp/sharplint-tools/roslyn-language-server --format json
```

The temporary NuGet config contained only the built `artifacts/packages` feed. Pack and installation **passed**. The installed distributable returned exit code **1**, with JSON assertions confirming **completed**, **complete: true**, **freshness: verified**, and compiler **CS0103** at error severity (two diagnostics total). This used the actual packaged executable and official server, not the build-tree CLI or fake server.

Native AOT and the old nested daemon deployment are retired; the active executable requires .NET 10.

## Remaining scope and next steps

- The MVP uses document pull diagnostics. Roslyn advertises workspace pull for some providers, but its long-polling and analysis-scope semantics are not integrated. Full build/project-level coverage is not claimed.
- External dependencies/imports/package caches and parent configuration must remain unchanged during a session; automatic reconciliation is limited to the root. Symlinks are rejected. Extend external dependency reconciliation before broadening the freshness guarantee.
- Linked files and multi-targeted projects use Roslyn's active document context. No cross-target completeness is claimed.
- The service depends on the tested Roslyn automatic-loading behavior and readiness/explicit-solution extensions. Validate new server versions before changing the pin.
- Next measurements should use representative larger solutions before choosing performance changes. Steady warm calls and project reloads have very different costs.

## Major changes

- `src/SharpLint.Analysis/DiagnosticService.cs`, `FileSynchronization.cs`, `DocumentState.cs`, `DiagnosticModels.cs`, `DiagnosticNormalizer.cs` and `Lsp/`: replace the custom workspace/compiler/cache pipeline with the shared service and persistent stdio LSP session.
- `src/SharpLint.Cli/CliApplication.cs`, `Mcp/DiagnosticTools.cs` and project references: expose check/status/MCP over the same service using the official SDK.
- `src/SharpLint.Daemon*`, old workspace/daemon tests and guides, and AOT/daemon benchmark tooling: removed.
- `tests/SharpLint.Tests/Lsp/`, `Fixtures/Lsp/` and `tests/SharpLint.FakeServer/`: protocol, lifecycle, synchronization, CLI/MCP and real Roslyn fixtures/tests.
- `benchmarks/SharpLint.Benchmarks/`, `benchmarks/lsp-results.json`, README, CI, coverage settings and service documentation: repeatable baseline and operating/validation instructions. Retained DSL specifications are marked as compatibility documentation.
