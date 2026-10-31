# Working on SharpLint

SharpLint is a C#/.NET static analysis CLI built on Roslyn, with a fluent configuration DSL. Run commands from the repository root.

## Sources of truth

- Read `docs/spec/README.md` and the relevant category spec before changing the DSL. `docs/spec/` defines adopted configuration behavior; `SPEC.md` contains planning and roadmap material.
- Check current code and tests when establishing implemented behavior. Parts of `README.md` describe an older scaffold, including a rules project that is no longer present. Do not treat planned features as implemented.

## Repository layout

- `src/SharpLint.Core/`: naming rule models, rule IDs, convention matching, diagnostics, and Roslyn syntax analysis.
- `src/SharpLint.Configuration/`: configuration builders, presets, JSON emission, and loading of executable `sharplint.config.cs` files. Fluent naming builders live in `Dsl/`.
- `src/SharpLint.Cli/`: Spectre.Console.Cli commands and pretty/JSON diagnostic output.
- `tests/SharpLint.Tests/`: xUnit tests for naming, analysis, and CLI behavior.
- `sharplint.config.cs`: the repository's own configuration, using locally built NuGet packages.

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

- Build in Debug before running CLI functional tests: they locate `src/SharpLint.Cli/bin/Debug/net10.0/sharplint.dll` and use packages produced by the build.
- Builds produce local NuGet packages in `packages/`; `NuGet.config` includes that feed. Config loading runs `dotnet run` on a .NET file-based app and may require package restore.
- If manually running a config picks up stale `1.0.0-dev` packages, `./build.sh` evicts the cached SharpLint.Core and SharpLint.Configuration packages before rebuilding. This script modifies the NuGet cache outside the repository.
- Verify tests for every change, including documentation and configuration changes. Run the relevant tests and the full suite when changes cross project boundaries; if no narrower set applies, run `dotnet test SharpLint.slnx`. Report the results and any checks that could not run, with the reason.
- Every new feature must include new tests that verify its observable behavior and relevant edge cases. Include those tests in the same atomic commit as the feature.

## Implementation conventions

- Follow nearby C# style: file-scoped namespaces, four-space indentation, explicit access modifiers, and nullable annotations. Preserve cancellation support and existing asynchronous patterns.
- Keep analysis and rule models in Core, configuration authoring/loading in Configuration, and command rendering in Cli.
- Naming changes can span fluent builders, predicate flags, symbol extraction, rule evaluation, rule ID formatting, and serialization. Check all affected layers and update the adopted spec when changing its contract.
- Treat emitted rule IDs and JSON fields as public contracts. Keep rule IDs deterministic and cover changed formatting behavior with tests.
- Preserve naming precedence: among applicable rules, the last declared rule wins within each verb group. Hidden rules can override earlier rules; different verbs can apply together.
- Analysis currently parses source files directly and skips `bin` and `obj`. Do not assume semantic compilation or MSBuild workspace loading exists.
- Add regression coverage for changed behavior using the existing test patterns. CLI tests should exercise observable output and exit codes.

## Change hygiene

- Keep changes scoped to the task and preserve unrelated working-tree or staged edits.
- Do not hand-edit generated `bin/`, `obj/`, or NuGet package artifacts.
- Update documentation and examples alongside public DSL or CLI behavior changes.

## Commits

- Make commits atomic, organized primarily around a single feature, fix, or coherent behavior change. Include all related implementation, tests, and documentation in the same commit, even when they span multiple files or projects.
- Separate unrelated changes into separate commits. Each commit should be independently understandable and leave the project in a working state.
- Write meaningful commit messages that describe the shared purpose and resulting behavior of the entire change. For example: `Add visibility predicates to naming rules`.
- Avoid file inventories and vague wording such as `Modified files X, Y, and Z for better naming`. Explain what the change accomplishes; use the body for rationale or details when needed.
