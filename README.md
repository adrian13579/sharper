<p align="center">
  <img src="logo.png" alt="SharpLint logo" width="560">
</p>

# SharpLint

A Roslyn-powered static analysis CLI for C# with fluent, typed configuration, readable diagnostics, and JSON output for CI and coding agents.

**Status: pre-alpha.** Naming policies and incremental daemon checks are implemented. The [adopted specifications](docs/spec/README.md) define the configuration contract; [SPEC.md](SPEC.md) describes the broader vision and planned features.

## Quick start

Use the .NET SDK selected by [global.json](global.json): **10.0.202**, with `latestMinor` roll-forward. Run these commands from the repository root:

```bash
dotnet build SharpLint.slnx
dotnet test SharpLint.slnx

# Create a starter configuration in the current directory, if one does not exist.
dotnet run --project src/SharpLint.Cli -- init

# Analyze the directory containing its own sharplint.config.cs.
dotnet run --project src/SharpLint.Cli -- check --path ./some/dir

# Emit JSON diagnostics, including cache and timing statistics.
dotnet run --project src/SharpLint.Cli -- check --path ./some/dir --format json --stats
```

The repository already includes [sharplint.config.cs](sharplint.config.cs); `init` refuses to overwrite an existing configuration. Without a configuration in the analyzed directory, SharpLint runs with **no rules**. Use `init` or add a configuration to enable checks.

Builds produce development NuGet packages in `packages/`, exposed through [NuGet.config](NuGet.config). When analyzing another directory, its NuGet sources must also include this local feed so its configuration can restore `SharpLint.Configuration@1.0.0-dev`. If a rebuilt package is stale in the NuGet cache, run `bash build.sh`; this evicts cached SharpLint.Core and SharpLint.Configuration packages before rebuilding.

## Configuration

`sharplint.config.cs` is a .NET 10 file-based app. SharpLint executes it with `dotnet run --file` and reads the configuration emitted by `.Apply()`.

```csharp
#:package SharpLint.Configuration@1.0.0-dev

using SharpLint.Configuration;
using SharpLint.Core.Naming;

Lint.Configure(config =>
{
    config.UsePreset(Preset.Recommended);

    config.Naming(n =>
    {
        n.Classes(c => c.Public())
            .RequireConvention(NamingConvention.PascalCase)
            .AsError();

        n.Interfaces()
            .RequirePrefix("I")
            .ForbidPrefix("IInternal");

        n.Methods(m => m.Async())
            .RequireSuffix("Async")
            .AsError();
    });
}).Apply();
```

`Preset.Recommended` declares PascalCase classes, interfaces, properties, and public methods; `I`-prefixed interfaces; `_camelCase` private fields; `UPPER_SNAKE_CASE` constants; `Async`-suffixed async methods; and camelCase parameters and local variables. Preset rules default to warnings.

Naming supports classes, interfaces, records, enums, methods, properties, fields, parameters, local variables, and namespaces. Rules can require or forbid conventions, prefixes, suffixes, exact names, and regular expressions. Accessibility and modifier predicates narrow selectors; `.ExceptWithAttribute<T>()` exempts attributed symbols from an individual rule.

Constraints in a selector chain compose with AND. For each verb, the last applicable selector chain wins; different verbs resolve independently. Severity and exemption modifiers apply to the most recent constraint. Use `.AsError()`, `.AsWarning()`, `.AsInfo()`, or `.Disabled()` to control reporting.

See the [Naming specification](docs/spec/naming.md) for selectors, predicates, precedence, and worked examples.

## CLI

The examples below use the built Debug binary, so build the solution first.

```bash
# Analyze the current directory; automatically start or reuse its daemon.
dotnet src/SharpLint.Cli/bin/Debug/net10.0/sharplint.dll check

# Run a complete analysis in this process.
dotnet src/SharpLint.Cli/bin/Debug/net10.0/sharplint.dll check --no-daemon

# Manage a daemon for a specific directory.
dotnet src/SharpLint.Cli/bin/Debug/net10.0/sharplint.dll daemon start --path ./some/dir --idle-timeout 900
dotnet src/SharpLint.Cli/bin/Debug/net10.0/sharplint.dll daemon status --path ./some/dir
dotnet src/SharpLint.Cli/bin/Debug/net10.0/sharplint.dll daemon stop --path ./some/dir
```

| Command / option | Behavior |
| --- | --- |
| `init` | Create a recommended configuration in the current directory. |
| `check --path <PATH>` | Analyze a directory recursively; defaults to the current directory. |
| `check --format pretty\|json` | Select readable output or JSON; defaults to `pretty`. |
| `check --stats` | Include snapshot, cache, and timing metadata. JSON output adds a `statistics` object. |
| `check --no-daemon` | Run an independent, complete analysis without starting a daemon. |
| `daemon start\|status\|stop --path <PATH>` | Manage the daemon for a directory; defaults to the current directory. |
| `daemon start --idle-timeout <SECONDS>` | Set the idle timeout when starting a new daemon; defaults to 900 seconds. |

The daemon retains a Roslyn syntax workspace and per-document diagnostics between checks. It reconciles inputs from disk, reuses unchanged documents, and invalidates cached analysis when configuration or project inputs change. Each check validates its input snapshot before returning results. Daemons stop after 15 minutes of inactivity by default. See the [daemon guide](docs/daemon.md) for cache boundaries, lifecycle details, and troubleshooting.

Analysis recursively reads C# source files, skipping `bin`, `obj`, `.git`, `.sharplint`, and linked directories. Project and build files participate in cache invalidation, but analysis remains syntax-only: SharpLint does not load projects through MSBuild or resolve semantic compilations.

`check` returns these exit codes:

| Code | Meaning |
| --- | --- |
| `0` | Analysis completed without error-severity diagnostics; warnings and information may be present. |
| `1` | Analysis completed with error-severity diagnostics. |
| `2` | Configuration or analysis failed, or analysis was canceled. |
| `3` | Daemon communication failed. |

Disabled rules produce no diagnostics. Use `.AsError()` for policies that should fail CI.

### JSON output

`--format json` returns an object with analysis totals and a diagnostics array:

```json
{
  "filesAnalyzed": 1,
  "elapsedMs": 12.5,
  "diagnostics": [
    {
      "ruleId": "naming.class.public.convention",
      "category": "naming",
      "severity": "error",
      "message": "Class 'bad_name' must use PascalCase convention.",
      "file": "/repo/Example.cs",
      "line": 1,
      "column": 14,
      "suggestion": null
    }
  ]
}
```

Paths, messages, and timing values above are illustrative. Diagnostics include stable rule IDs, categories, severities, source locations, and optional suggestions.

## Rule categories and extensions

SharpLint defines nine stable [core categories](docs/spec/taxonomy.md): Naming, Syntax, Organization, Design, Dependencies, Architecture, Reliability, Performance, and Project. **Naming is the only category with built-in policies.**

Custom hosts can implement `IRuleAnalyzer`, register policies and categories through `RuleModule` and `RuleRegistry`, and configure them with `config.Rule(...)`. See the [module contracts and host integration example](docs/spec/taxonomy.md). The stock CLI does not load external modules; configured rules without registered analyzers fail explicitly.

Built-in policies for the other categories, semantic analysis, MSBuild project loading, SARIF output, autofix, and external plugin loading remain future work. See [SPEC.md](SPEC.md) for planning material.

## Repository structure

| Path | Responsibility |
| --- | --- |
| [src/SharpLint.Core](src/SharpLint.Core) | Naming models and evaluation, rule metadata and registration, diagnostics, and syntax analysis. |
| [src/SharpLint.Configuration](src/SharpLint.Configuration) | Fluent DSL, presets, JSON emission, and executable configuration loading. |
| [src/SharpLint.Analysis](src/SharpLint.Analysis) | Syntax workspace coordination, input snapshots, cache invalidation, and incremental analysis. |
| [src/SharpLint.Daemon](src/SharpLint.Daemon) | Per-directory daemon identity, protocol, client, and host lifecycle. |
| [src/SharpLint.Cli](src/SharpLint.Cli) | Spectre.Console.Cli commands and pretty/JSON rendering. |
| [tests/SharpLint.Tests](tests/SharpLint.Tests) | xUnit tests, including analyzer integration and CLI/daemon functional tests. |
| [docs/spec](docs/spec/README.md) | Adopted DSL and taxonomy contracts. |
| [benchmarks/daemon.py](benchmarks/daemon.py) | End-to-end cold, warm, changed-input, concurrent-client, and restart benchmarks. |

## Development and validation

```bash
# Build Debug before CLI functional tests: they invoke the Debug binary.
dotnet build SharpLint.slnx
dotnet test SharpLint.slnx

# Run a focused test group.
dotnet test tests/SharpLint.Tests/SharpLint.Tests.csproj --filter FullyQualifiedName~RuleIdFormatterTests
```

Keep analysis and rule models in Core, configuration authoring and loading in Configuration, and command rendering in Cli. Add observable behavior tests with each change. New rule categories need analyzer integration coverage for compliant and violating source. See [AGENTS.md](AGENTS.md) for contributor guidance.

### Code coverage

```bash
bash coverage-test.sh
```

The script restores pinned tools, runs the suite with Coverlet, and generates HTML, JSON, text, and Cobertura reports. Inspect `CoverageReport/index.html` and `CoverageReport/Summary.json` for uncovered lines and branches. Raw results are stored in a fresh directory under `TestResults/coverage/` for each run; generated reports are ignored by Git.

[coverage.runsettings](coverage.runsettings) measures Core and Configuration. CLI functional tests run, but their subprocesses are not instrumented. There is no line or branch coverage gate. Additional test arguments can follow the script, for example `bash coverage-test.sh --filter FullyQualifiedName~AnalyzerTests` for a partial measurement.

### Mutation testing

```bash
bash mutation-test.sh                # Core and Configuration, separate reports
bash mutation-test.sh Core           # Core analysis and naming rules
bash mutation-test.sh Configuration  # Configuration DSL and presets
```

The script restores the pinned Stryker.NET tool and builds the solution. Its configuration is in [tests/SharpLint.Tests/stryker-config.json](tests/SharpLint.Tests/stryker-config.json). Inspect surviving mutants in `StrykerOutput/<project>/reports/mutation-report.html`; JSON reports are available alongside the HTML. These outputs are ignored by Git.

Each measured project must reach a **mutation score of at least 90%**. A lower score fails this requirement even if the process exits successfully. Strengthen tests rather than lowering the threshold or excluding code to improve the score. Additional Stryker arguments can follow the target, for example `bash mutation-test.sh Core --mutate '**/RuleIdFormatter.cs'`.

Mutation runs measure Core and Configuration through in-process tests. Subprocess-based CLI tests are excluded because they execute separate binaries and packaged assemblies. Run `dotnet test SharpLint.slnx` separately to verify the full suite.

### Daemon benchmarks

After building Debug, run the benchmark with Python 3:

```bash
python3 benchmarks/daemon.py --sizes 100 1000 --iterations 10
```

The benchmark creates temporary source repositories, stops their daemons on completion, and writes results to `benchmarks/results.json` by default. Results include median and p95 elapsed time, document reuse, cache hit rates, and daemon peak memory where available.
