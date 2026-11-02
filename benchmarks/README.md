# LSP diagnostic baseline

Run from the repository root after installing the pinned server and building Debug (for integration tests):

```sh
SHARPLINT_TEST_ROSLYN=/absolute/path/to/roslyn-language-server \
  dotnet run -c Release --project benchmarks/SharpLint.Benchmarks -- 20
```

An optional second argument selects the output JSON path. The default is `benchmarks/lsp-results.json`. The harness copies the integration solution to a temporary directory, restores it and builds its custom analyzer before timing. It edits only that disposable copy. It uses the same diagnostic service as CLI/MCP.

Five cold sessions measure initialization through Roslyn's project-ready notification. Each warm scenario gets at least 20 observations: unchanged file, single-file syntax edit, cross-project semantic edit, three-file batch, repeated warm request, and project-reference change/session reload. JSON contains raw timings and status/attempt counts, median and nearest-rank P95, runtime/OS/CPU context and sample counts. Warm timings include reconciliation, debounce and up to three bounded attempts after explicit incomplete results. Restore/build are excluded. Incomplete/failed samples remain in the report; they are not discarded to improve latency.

Cold process startup is separated from document analysis. Initial-load measurements can benefit from OS/NuGet caches; these are process-cold, not machine-cold. Project-reference results include restarting Roslyn. A small fixture is not representative of a large repository. Performance runs should occur without concurrent coverage/mutation work. No target latency is assumed.

Historical daemon/compiler benchmark scripts and results were retired with that architecture; they are not evidence for the LSP service.

The recorded baseline is in [lsp-results.json](lsp-results.json), with a human-readable table in [validation results](../docs/lsp-validation.md). Scenarios execute in sequence: unchanged-file samples follow the previous project reload; repeated warm calls follow batch analysis. The two distributions include different amounts of native post-load work and should not be treated as interchangeable latency claims.
