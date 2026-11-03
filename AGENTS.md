# Working on SharpLint

SharpLint is a local, read-only connector between Microsoft's official Roslyn language server and coding agents. CLI and MCP share one diagnostic service. Run commands from the repository root.

## Sources of truth

- Read `docs/lsp-service.md` before changing the diagnostic contract, LSP lifecycle or synchronization guarantees.
- Establish implemented behavior from current code and tests. Do not treat a capability advertised by Roslyn as a capability implemented by SharpLint.
- Keep README, the service contract and CLI/MCP tool descriptions consistent with actual scope and freshness guarantees.

## Repository layout

- `src/SharpLint.Analysis/`: diagnostic service, LSP framing/connection/session, file synchronization and normalized response models. It uses only .NET libraries and must not reference Roslyn or MSBuild packages.
- `src/SharpLint.Cli/`: command dispatch, pretty/JSON output and official C# MCP SDK stdio hosting. Both interfaces use `DiagnosticService`.
- `tests/SharpLint.Tests/`: protocol, synchronization, lifecycle, architecture, CLI and real Roslyn/MCP tests.
- `tests/SharpLint.FakeServer/`: controlled stdio LSP server for connector tests.
- `tests/SharpLint.Tests/Fixtures/Lsp/`: real multi-project solution and ordinary custom Roslyn analyzer. Analyzer dependencies belong to this fixture, not production projects.
- `benchmarks/SharpLint.Benchmarks/`: repeatable cold/warm latency measurements using a disposable fixture.

## Architectural boundaries

- Roslyn owns workspace loading, project evaluation, compilation, source generators, analyzer execution and native analyzer configuration.
- SharpLint owns LSP/MCP lifecycle, synchronization, diagnostic normalization/filtering, completion/freshness reporting, errors and cancellation.
- Do not add a rule engine, fluent policy DSL, executable configuration loader, analyzer registry, package manager, custom compiler or Roslyn workspace cache.
- Users enable ordinary and custom analyzers through project references, NuGet integration and `.editorconfig`. SharpLint does not need an analyzer inventory.
- Diagnostics are read-only. Never apply workspace edits or modify user source. Do not automatically restore/install packages into user projects.
- MCP stdout carries protocol messages only. Send logs to stderr.
- Check scope is C# document pull diagnostics. Never equate an empty array with successful solution/build verification, or report uncertain/stale results as verified.
- Treat diagnostic codes and JSON fields as public contracts. Preserve codes, severities, optional source and related information; do not invent analyzer identities. Positions are zero-based UTF-16 and paths are normalized absolute paths in every output format.

## Build and validation

Use the SDK selected by `global.json` (10.0.202 with `latestMinor` roll-forward). Shared settings enable nullable references, implicit usings and warnings as errors.

```sh
dotnet build SharpLint.slnx -m:1
dotnet test SharpLint.slnx -m:1
# Include real Roslyn and actual MCP stdio integration:
SHARPLINT_TEST_ROSLYN=/absolute/path/to/roslyn-language-server dotnet test SharpLint.slnx -m:1
# Focused protocol tests:
dotnet test tests/SharpLint.Tests/SharpLint.Tests.csproj --filter FullyQualifiedName~ProtocolTests
# CLI:
dotnet run --project src/SharpLint.Cli -- check --path ./some/dir --format json
```

- Build Debug before CLI/MCP tests: they locate `src/SharpLint.Cli/bin/Debug/net10.0/sharplint.dll`. Real-server tests are explicitly skipped without `SHARPLINT_TEST_ROSLYN`; report those skips and do not present fake-server tests as real integration results.
- Restore fixture/target project dependencies and build custom analyzers before checking. The service disables automatic restore. Roslyn and the test runner require local sockets/named pipes.
- Documentation-only changes do not require tests. Otherwise run relevant tests, and the full suite for changes across project boundaries or when no narrower set applies. Report exact results and any checks that could not run, with reasons.
- Add observable behavior and edge-case tests with each feature/fix. Exercise normalization and emitted metadata as well as diagnostic contents. CLI tests assert output and exit codes.
- Each root `.sh` script has a Windows PowerShell 5.1 / PowerShell 7 `.ps1` equivalent: `build.ps1`, `coverage-test.ps1` and `mutation-test.ps1`. Invoke them with `.\build.ps1`, for example; additional arguments are forwarded to the corresponding .NET command. Scripts resolve paths from their own location and restore the caller's working directory.
- Run `bash coverage-test.sh` (PowerShell: `.\coverage-test.ps1`) for in-process Analysis line/branch coverage. Inspect `CoverageReport/index.html` and `CoverageReport/Summary.json`. CLI subprocess coverage is excluded.
- Run `bash mutation-test.sh` (PowerShell: `.\mutation-test.ps1`) for Analysis mutation quality. Inspect surviving mutants under `StrykerOutput/Analysis`. CLI and real-server subprocess tests run separately; controlled-server service tests remain in mutation scope.
- Run `pwsh -NoProfile -File tests/scripts/Test-PowerShellScripts.ps1` to check script argument handling, paths and failure propagation with a fake `dotnet` command. Windows CI runs these checks in both PowerShell 7 and Windows PowerShell 5.1; these checks never run Stryker.
- Require at least **90% mutation score** for Analysis. Strengthen tests to meet it; never lower the threshold or exclude production code merely to improve the score. Report a lower score as failing even if the process exits successfully.
- Run builds/tests, coverage, mutation testing and benchmarks sequentially: instrumentation rewrites build outputs; benchmarks need an otherwise idle validation environment. Coverage does not replace mutation testing.
- Keep benchmark results honest: record sample counts, median/P95, statuses, hardware/runtime context and cold versus warm behavior. Do not infer unmeasured performance targets.

## Implementation conventions

- Follow nearby C# style: file-scoped namespaces, four-space indentation, explicit access modifiers and nullable annotations.
- Preserve cancellation, deadlines, bounded recovery and asynchronous patterns throughout process and I/O operations.
- Keep the dependency graph small. Use supported server capabilities and document any Roslyn-specific extensions and version requirements.
- Reconcile disk on checks; watchers are hints and may overflow. Preserve document versions and negotiated synchronization semantics. Handle request races and older diagnostic results explicitly.
- Order diagnostics deterministically. Preserve distinct findings and scope limitations when deduplicating.

## Change hygiene

- Each agent session normally uses its own dedicated Git worktree. Explicit user instructions to work in the current branch/worktree take precedence.
- Preserve unrelated working-tree/staged edits and keep changes scoped to the task.
- Do not hand-edit generated `bin/`, `obj/` or NuGet package artifacts.
- Update documentation and examples alongside public CLI/MCP behavior changes.

## Commits

- Make atomic commits around one feature, fix or coherent behavior change, including related implementation, tests and documentation.
- Use Conventional Commits: `<type>[optional scope][!]: <description>`. Allowed types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`.
- Use a lowercase type/scope, an imperative description and no trailing period. Explain breaking changes in a `BREAKING CHANGE:` footer.
- Describe the shared purpose and resulting behavior, not a file inventory. Each commit should be understandable and leave the project working.
