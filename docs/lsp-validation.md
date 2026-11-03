# Diagnostic connector validation

SharpLint contains two production projects: the shared LSP diagnostic service and its CLI/MCP agent host. Protocol, synchronization, lifecycle and real-server tests exercise that connector. Architecture tests enforce the production boundary: no embedded compiler, MSBuild workspace or policy engine.

The tested environment is SDK **10.0.202**, runtime **10.0.6**, official Roslyn language server **5.13.0-1.26514.10**, official MCP SDK **2.3.0**, Ubuntu 22.04 x64. Real integration tests require `SHARPLINT_TEST_ROSLYN` and local sockets/named pipes.

## Validation commands

Run sequentially from the repository root:

```sh
dotnet build SharpLint.slnx -t:Rebuild -m:1
SHARPLINT_TEST_ROSLYN=/absolute/path/to/roslyn-language-server dotnet test SharpLint.slnx --no-build --no-restore -m:1
SHARPLINT_TEST_ROSLYN=/absolute/path/to/roslyn-language-server bash coverage-test.sh --no-build --no-restore
bash mutation-test.sh
dotnet pack src/SharpLint.Cli -c Release -o artifacts/packages -m:1
```

Coverage and mutation target `SharpLint.Analysis`. The mutation threshold is **90%**. Controlled-server service tests stay in mutation scope; actual CLI/MCP/real-server subprocess tests run separately. Do not run instrumentation concurrently with other checks.

## Integration coverage

Real-server tests assert compiler **CS0103**, SDK analyzer **CA1822**, custom analyzer **FIXTURE001** through a normal analyzer ProjectReference, external fix detection, cross-project semantic updates, create/delete/rename, project-reference reloads, persistent sessions and crash recovery. Actual CLI and MCP entry points are tested against returned diagnostics, completion/freshness metadata and process identity.

Controlled tests cover framing, correlation, capability registration, normalization, related information, empty reports, location-free model data, UTF-16 versions/ranges, watcher reconciliation/overflow, races, cancellation, deadlines and process cleanup. Location-free model support does not imply implemented workspace/project-level retrieval.

## Baseline measurements

```sh
SHARPLINT_TEST_ROSLYN=/tmp/sharplint-tools/roslyn-language-server \
  dotnet run -c Release --project benchmarks/SharpLint.Benchmarks -- 20
```

This baseline was recorded before repository consolidation (commit `434cab9`); it is not a new cleanup performance measurement. All **125 measurements completed**, with zero incomplete/failed final results. Five process-cold starts and 20 samples per warm scenario were recorded. Context: **Intel Core i9-12900HK**, 20 logical processors visible to .NET, Ubuntu **22.04.5 LTS** x64, **.NET 10.0.6** and SDK **10.0.202**. Coverage and mutation testing had finished before measurement. OS/NuGet caches were warm; this is a small fixture, not a large-repository benchmark.

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

## Supported scope

Checks cover C# document pull diagnostics, not full solution/build verification. External imports/dependencies/package caches must remain fixed during a session; symlinks are rejected. Linked/multi-targeted documents use Roslyn's active context. The service contract describes those limits and the tested server-specific loading/readiness mechanisms.

## Cleanup validation results

The connector-only controlled suite passed **81 tests** with zero failures/skips. It includes new architecture, protocol callback, server discovery, static capability, watcher-payload, metadata and synchronization-boundary regressions.

`bash mutation-test.sh` completed against Analysis in **32 min 25 s**. The score is **66.67%**, which **fails the unchanged 90% requirement**: 547 killed, 241 survived, 13 timed out and 39 uncovered; Stryker also recorded 121 compile-error mutants and 99 automatic block-filter ignores. No production code was excluded and the threshold was not lowered. The retained-library scores from the removed projects are not used as evidence for this connector.

The surviving cases were inspected in `StrykerOutput/Analysis/reports/mutation-report.json`. They include asynchronous context choices, error/limitation text, initialization negotiation, watcher event configuration, lifecycle/resource cleanup, result ordering and unobserved push-cache behavior. These are a connector test-quality gap exposed by measuring the active service, and the mutation gate remains failing. The initial inadequate mutation run was stopped after coverage made a passing score impossible; the completed run includes the stronger tests and fully instrumented framing parser.

The first full-suite attempt also exposed an existing watcher timing assumption in a test. It now requires an explicit uncertain result while events settle and verified diagnostics on a bounded subsequent check, matching the documented consistency contract.

The clean rebuild (`dotnet build SharpLint.slnx -t:Rebuild -m:1`) passed with zero warnings/errors. The final full suite with `SHARPLINT_TEST_ROSLYN` set passed **96 tests, zero failures and zero skips**, in **1 min 18 s**:

```sh
SHARPLINT_TEST_ROSLYN=/tmp/sharplint-tools/roslyn-language-server \
  dotnet test SharpLint.slnx --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=connector-cleanup.trx' --results-directory TestResults/connector-cleanup
```

TRX output is available in `TestResults/connector-cleanup/connector-cleanup.trx`. Documentation links and `git diff --check` passed. Coverage passed the same **96 tests**, with zero failures/skips, in **1 min 16 s**. `CoverageReport/index.html`, `Summary.json` and `Cobertura.xml` were inspected: Analysis has **98.3% line coverage**, **91.7% branch coverage**, and **100% method coverage**. Remaining uncovered lines are exceptional/cancellation/disposal and solution-settings fallback paths.

At the user's instruction, **no further Stryker runs are performed**. The completed mutation result above remains a failing quality check; coverage does not replace it. This cleanup's functional build and integration tests pass, while further mutation-quality improvement remains outstanding.


## Packaging after cleanup

`dotnet pack src/SharpLint.Cli -c Release -o artifacts/connector-cleanup -m:1` passed. Archive assertions confirmed the connector assembly and packaged README are present and retired rule/configuration/daemon assemblies are absent. The tool installed successfully into `/tmp/sharplint-connector-tool` using an isolated temporary package cache and a feed restricted to the package under test.

The isolated packaged executable was checked against the official server using `/tmp/sharplint-probe/Test.cs`. It returned the expected diagnostic exit code **1**; JSON assertions confirmed **completed**, **complete: true**, **freshness: verified** and compiler **CS0103** at error severity.
