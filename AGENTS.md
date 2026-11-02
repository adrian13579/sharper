# Working on SharpLint

SharpLint is a read-only MCP/CLI diagnostic bridge to Microsoft's official Roslyn language server. Run commands from the repository root. Core and Configuration retain the legacy fluent DSL as compatibility libraries, outside the service pipeline.

## Sources of truth

- Read `docs/lsp-service.md` for the active service contract. Read `docs/spec/README.md` and the relevant category spec before changing the retained legacy DSL. `docs/spec/` defines adopted configuration behavior; `SPEC.md` contains planning and roadmap material.
- Check current code and tests when establishing implemented behavior. The DSL specs describe compatibility libraries; they do not configure the LSP service. Do not treat planned features as implemented.

## Repository layout

- `src/SharpLint.Analysis/`: diagnostic service, LSP framing/connection/session, disk synchronization and normalized diagnostic contracts. It must not reference Roslyn or MSBuild packages.
- `src/SharpLint.Core/`: legacy naming/rule library APIs and their Roslyn syntax analysis.
- `src/SharpLint.Configuration/`: configuration builders, presets, JSON emission, and loading of executable `sharplint.config.cs` files. Fluent naming builders live in `Dsl/`.
- `src/SharpLint.Cli/`: command dispatch, pretty/JSON output and official C# SDK MCP stdio hosting. Both entry points use the same DiagnosticService.
- `tests/SharpLint.Tests/`: xUnit tests for naming, analysis, and CLI behavior.
- `tests/SharpLint.FakeServer/`: controlled stdio LSP server for lifecycle and synchronization tests.
- `tests/SharpLint.Tests/Fixtures/Lsp/`: real Roslyn multi-project fixture and ordinary custom analyzer.
- `benchmarks/SharpLint.Benchmarks/`: disposable-fixture cold/warm latency baseline.

## Build and validation

Use the .NET SDK selected by `global.json` (10.0.202 with `latestMinor` roll-forward). Shared settings in `Directory.Build.props` enable nullable references, implicit usings, and warnings as errors.

```sh
dotnet build SharpLint.slnx
dotnet test SharpLint.slnx

# Focused tests, for example:
dotnet test tests/SharpLint.Tests/SharpLint.Tests.csproj --filter FullyQualifiedName~RuleIdFormatterTests

# Run the CLI against a directory:
dotnet run --project src/SharpLint.Cli -- check --path ./some/dir --format json
```

- Build in Debug before running CLI/MCP integration tests: they locate `src/SharpLint.Cli/bin/Debug/net10.0/sharplint.dll`. Set `SHARPLINT_TEST_ROSLYN` to the installed official server executable; without it, real-server tests are explicitly skipped.
- Builds produce retained Core/Configuration NuGet packages in `packages/`; `NuGet.config` includes that feed. Legacy configuration loading tests may require package restore. The active service never executes `sharplint.config.cs`.
- `build.sh` builds without evicting global NuGet caches. Restore target solution dependencies and build custom analyzers before checking; the service disables automatic restore.
- Changes limited to documentation or Markdown files do not require tests. For all other changes, including configuration changes, run the relevant tests and the full suite when changes cross project boundaries; if no narrower set applies, run `dotnet test SharpLint.slnx`. Report the results and any checks that could not run, with the reason.
- Every new feature must include new tests that verify its observable behavior and relevant edge cases. Include those tests in the same atomic commit as the feature.
- Every new rule category must include integration tests through the analyzer in `tests/SharpLint.Tests/Analysis/AnalyzerTests.cs` (or an equivalent category-specific analyzer test class). Exercise configuration, source analysis, and emitted diagnostics together, covering both compliant and violating code; isolated builder or evaluator tests alone are insufficient.
- Use `bash coverage-test.sh` to run the suite and generate line and branch coverage reports for Core and Configuration. Inspect `CoverageReport/index.html` and `CoverageReport/Summary.json` for uncovered paths. CLI subprocess coverage is not included. Coverage reports complement mutation testing and do not replace its 90% requirement.
- Use `bash mutation-test.sh` to measure Core and Configuration test quality, or pass `Core` or `Configuration` for a focused run. Inspect surviving mutants in the ignored `StrykerOutput/` reports. Mutation testing supplements the full test suite; its configuration excludes subprocess-based CLI functional tests. See the README's mutation testing section for scope and thresholds.
- Require a minimum mutation score of 90% for each measured project (Core and Configuration). Add or strengthen tests to meet this threshold; do not lower the threshold or exclude code merely to improve the score. Report any project below 90% as failing this requirement, even if the Stryker process exits successfully.

## Implementation conventions

- Follow nearby C# style: file-scoped namespaces, four-space indentation, explicit access modifiers, and nullable annotations. Preserve cancellation support and existing asynchronous patterns.
- Keep LSP analysis and normalization in Analysis, MCP/CLI hosting in Cli, and retained library rules/configuration in Core/Configuration. The active service must not execute the legacy analyzer engine.
- Expose built-in rules through a typed fluent DSL in every category, following Naming's API design: category callback, target selector, `Require…`/`Forbid…` requirement, then optional `.AsError()`, `.AsWarning()`, `.AsInfo()`, or `.Disabled()` modifiers. For example: `config.Design(d => d.Parameters().RequireMaximumCount(5).AsError())`. Modifiers apply only to the most recent requirement on their selector chain.
- Use the fluent DSL in built-in rule documentation and examples. Keep string-based `Module(...).Rule(...)` and generic `Rule(...)` APIs for compatibility and external analyzers; new built-in policies must also have typed fluent authoring. Document each fluent requirement's stable rule ID and serialized options in the adopted specs.
- Naming changes can span fluent builders, predicate flags, symbol extraction, rule evaluation, rule ID formatting, and serialization. Check all affected layers and update the adopted spec when changing its contract.
- Treat emitted rule IDs and JSON fields as public contracts. Keep rule IDs deterministic and cover changed formatting behavior with tests.
- Preserve naming precedence: for each verb, the last applicable declaration selects the winning selector chain; all constraints of that verb in that chain compose with AND. Separate selectors can override earlier chains. Hidden rules participate in precedence but are skipped individually during evaluation; different verbs resolve independently. Rules without chain metadata retain standalone last-declaration-wins behavior.
- Roslyn owns workspace loading, compilation, analyzer execution and project evaluation. SharpLint reconciles root-contained disk inputs and reports document pull scope; never equate an empty diagnostic list with successful solution verification.
- Add regression coverage for changed behavior using the existing test patterns. CLI tests should exercise observable output and exit codes.

## Change hygiene

- Each agent session must work in its own separate Git worktree. Create a dedicated worktree before making changes, and run edits, builds, and tests from that worktree. Never share a worktree between agent sessions.
- Keep changes scoped to the task and preserve unrelated working-tree or staged edits.
- Do not hand-edit generated `bin/`, `obj/`, or NuGet package artifacts.
- Update documentation and examples alongside public DSL or CLI behavior changes.

## Commits

- Make commits atomic, organized primarily around a single feature, fix, or coherent behavior change. Include all related implementation, tests, and documentation in the same commit, even when they span multiple files or projects.
- Separate unrelated changes into separate commits. Each commit should be independently understandable and leave the project in a working state.
- Use Conventional Commits for every commit: `<type>[optional scope][!]: <description>`. Allowed types are `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, and `revert`. Use a lowercase type and optional scope, an imperative description, and no trailing period. Mark breaking changes with `!` and explain them in a `BREAKING CHANGE:` footer.
- Write meaningful descriptions that explain the shared purpose and resulting behavior of the entire change. For example: `feat(naming): add visibility predicates to naming rules`. Use `test` for test coverage improvements, `build` for build tooling, and `docs` for contributor guidance; choose the type that best describes the primary purpose of an atomic change.
- Avoid file inventories and vague wording such as `Modified files X, Y, and Z for better naming`. Explain what the change accomplishes; use the body for rationale or details when needed.
