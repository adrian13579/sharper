# SharpLint

A fast, opinionated, developer-friendly static analysis and governance tool for the .NET ecosystem — an ESLint/Biome-style DX over Roslyn for C#.

> Status: **pre-alpha scaffold.** The CLI builds, runs, and ships one working rule. The full v1 specification (everything in this document past [Project Specification (v1)](#project-specification-v1)) is the design target, not the current behavior.

---

## Current Status

What works today:

- `sharplint check` walks `*.cs` files under a path and reports diagnostics.
- One rule shipped: `async.suffix-required` (warns when an `async` method's name does not end in `Async`).
- Pretty CLI output and `--format json` for machine consumers.
- `sharplint init` scaffolds a starter `sharplint.config.cs` (a .NET 10 file-based app).
- Fluent configuration DSL skeleton (`Lint.Configure(...).Apply()`).
- `sharplint.config.cs` is loaded and applied: preset selects the active rule set; severity overrides change emitted severities (`Severity.Hidden` disables a rule).
- 6 passing unit tests.

What is **not** wired up yet:

- Presets are structurally selectable but functionally identical today (only one rule shipped). Preset-specific rule sets and severity defaults will diverge as more rules land.
- `sharplint fix` is a stub (exit code 2).
- Semantic and architectural rules — only syntax rules are implemented.
- MSBuild workspace loading (the analyzer parses files directly; `.csproj`/`.sln` discovery is deferred).
- SARIF output, daemon mode, autofixes, presets beyond the default.

---

## Quick Start

```bash
# build everything
dotnet build SharpLint.slnx

# run the tests
dotnet test SharpLint.slnx

# analyze a directory
dotnet run --project src/SharpLint.Cli -- check --path ./some/dir

# machine-readable output
dotnet run --project src/SharpLint.Cli -- check --path ./some/dir --format json

# scaffold a config in the current directory
dotnet run --project src/SharpLint.Cli -- init
```

Requires the .NET 10 SDK (pinned via [global.json](global.json)). The config format is a [.NET 10 file-based app](https://learn.microsoft.com/dotnet/core/tools/dotnet-run-file) — a regular `.cs` file with `#:package` directives — so editor autocomplete works out of the box in any C# tooling, and SharpLint executes it via `dotnet run sharplint.config.cs` and reads the emitted JSON.

During development, `dotnet build` produces local NuGet packages into [packages/](packages) (configured via [NuGet.config](NuGet.config)) so the repo's own `sharplint.config.cs` can resolve `#:package SharpLint.Configuration@1.0.0-dev`. Once SharpLint publishes to nuget.org, end users won't need the local feed.

---

## Repository Structure

```
sharplint/
├── SharpLint.slnx
├── Directory.Build.props        shared MSBuild settings (net10.0, nullable, warnings-as-errors)
├── global.json                  SDK pin
├── src/
│   ├── SharpLint.Core/          rule + diagnostic abstractions, file-walking Analyzer
│   │   ├── Diagnostics/         Diagnostic, Severity, RuleCategory, SourceLocation
│   │   ├── Rules/               IRule, ISyntaxRule, ISemanticRule
│   │   └── Analysis/            AnalysisContext, AnalysisResult, Analyzer
│   ├── SharpLint.Rules/         built-in rules (one shipped: AsyncSuffixRule)
│   ├── SharpLint.Configuration/ fluent DSL (Lint.Configure → ConfigBuilder) + file-based-app config loader
│   └── SharpLint.Cli/           Spectre.Console.Cli entrypoint; commands: check, init, fix
└── tests/
    └── SharpLint.Tests/         xUnit
```

| Project | Responsibility |
| --- | --- |
| `SharpLint.Core` | Public rule contracts (`IRule`, `ISyntaxRule`, `ISemanticRule`), diagnostic types, the `Analyzer` that walks source files and dispatches to enabled rules. No knowledge of specific rules or config. |
| `SharpLint.Rules` | Built-in opinionated rules. Add new rules here and register them in `BuiltInRules.All`. |
| `SharpLint.Configuration` | The fluent DSL (`Lint.Configure(...)`), preset names, and the config loader (runs `sharplint.config.cs` as a .NET 10 file-based app and reads the emitted JSON). Published as the `SharpLint.Configuration` NuGet package that user configs reference via `#:package`. |
| `SharpLint.Cli` | `Spectre.Console.Cli`-based entrypoint. Packaged as a .NET global tool (`PackAsTool=true`, command name `sharplint`). |
| `SharpLint.Tests` | xUnit tests. |

### Adding a new rule

1. Implement `ISyntaxRule` (or `ISemanticRule` once semantic analysis lands) under `src/SharpLint.Rules/<Category>/`.
2. Register the rule in `src/SharpLint.Rules/BuiltInRules.cs`.
3. Add a test under `tests/SharpLint.Tests/Rules/`.

See [src/SharpLint.Rules/Async/AsyncSuffixRule.cs](src/SharpLint.Rules/Async/AsyncSuffixRule.cs) for a reference implementation.

---

## Roadmap (short-term)

In rough priority order, the next pieces to land before this is useful beyond a demo:

1. Architectural layer rules (`Architecture.Layer("Domain").CannotReference("Infrastructure")`) — a key differentiator from the spec below. The architecture DSL records severity overrides today but no rule consumes them yet.
2. SARIF output for CI integration.
3. MSBuild workspace loading so semantic rules can run.
4. A handful of additional high-value rules: `async.cancellation-token-required`, naming conventions, common .NET pitfalls.
5. Make presets functionally distinct (different rule sets / severity defaults) once there are enough rules to differentiate them.

The longer-term vision — daemon mode, autofix infrastructure, plugin ecosystem, policy packs — is documented in detail in the specification below.

---

# Project Specification (v1)

## Vision

SharpLint is a fast, opinionated, developer-friendly static analysis and governance tool for the .NET ecosystem.

The project aims to bring an ESLint/Biome-like developer experience to C# and .NET while leveraging the power of Roslyn internally.

SharpLint is not just a linter.

It is intended to become:

* A code quality enforcement platform
* A repository governance engine
* An architectural policy system
* A machine-readable feedback layer for AI coding agents
* A developer-friendly abstraction over Roslyn analyzers

The tool should provide:

* Opinionated defaults
* Strong developer experience (DX)
* Extremely fast execution
* Fluent and typed configuration
* Organizational rule enforcement
* AI-agent-friendly diagnostics
* Easy customization without complexity explosion

---

# Core Philosophy

## Primary Principles

### 1. Opinionated by Default

The tool should provide value immediately after installation.

Users should not need to configure hundreds of rules.

Example:

```bash
sharplint check
```

should already enforce:

* naming conventions
* async best practices
* architectural rules
* common .NET pitfalls
* repository consistency
* clean code patterns

Configuration should be optional initially.

---

### 2. Convention Over Configuration

SharpLint should favor:

* sane defaults
* curated presets
* minimal configuration

instead of exposing every Roslyn analyzer setting directly.

---

### 3. Developer Experience First

The project must optimize for:

* readability
* discoverability
* autocomplete
* documentation
* fast feedback
* easy onboarding

The configuration experience should feel modern and intuitive.

Inspired by:

* ESLint
* Biome
* Ruff
* FluentAssertions
* ASP.NET Minimal APIs

---

### 4. AI-Agent Friendly

SharpLint should be designed with coding agents as first-class consumers.

The tool should provide:

* deterministic diagnostics
* structured machine-readable output
* stable schemas
* actionable remediation
* fast execution loops

SharpLint acts as:

> an extension of the compiler for organizational and architectural standards.

---

### 5. Fast by Design

Performance is a core feature.

The tool should feel:

* near-instant
* incremental
* cache-aware
* lightweight

The project should optimize for:

* repeated invocation
* CI usage
* local feedback loops
* AI iteration loops

---

# High-Level Product Positioning

SharpLint should position itself as:

> ESLint/Biome for modern C# and .NET teams.

But the long-term positioning is broader:

> Policy-as-code and architectural governance for .NET repositories.

---

# Technical Direction

## Core Technology

### Analysis Engine

SharpLint will use:

* Roslyn

internally for:

* syntax analysis
* semantic analysis
* incremental compilation
* workspace loading
* diagnostics
* code fixes

SharpLint will NOT reinvent:

* parsing
* compilation
* AST infrastructure

Roslyn is an implementation detail, not the exposed developer experience.

---

# Project Scope

## Initial Scope

### Supported Language

* C# only

### Supported Project Type

* SDK-style .NET projects

### Primary Interface

* CLI only initially

### Initial Workflow

```bash
sharplint check
```

---

# Installation Model

## Global Tool

SharpLint should be installable as a standalone CLI.

Example:

```bash
dotnet tool install -g sharplint
```

The tool should NOT require:

* NuGet package installation
* csproj modifications
* analyzer package references

The repository should only require:

```text
sharplint.config.cs
```

This avoids:

* project pollution
* analyzer version drift
* complex setup
* repository inconsistency

---

# Configuration System

## Configuration File

```text
sharplint.config.cs
```

The configuration system is one of the core differentiators of the project.

---

## Configuration Goals

The DSL must be:

* typed
* discoverable
* autocomplete-friendly
* well documented
* developer-friendly
* expressive
* stable

The DSL should feel like a modern C# API.

---

## Configuration Design Principles

### Strongly Typed APIs

Prefer:

```csharp
rules.Async.RequireSuffix()
    .AsWarning();
```

instead of:

```csharp
rules.SetSeverity("async-rule", "warning");
```

---

### Use Enums Where Appropriate

Example:

```csharp
rules.Naming.Methods(NamingConvention.PascalCase);
```

Benefits:

* autocomplete
* discoverability
* agent generation
* readability

---

### Human-Centered API Design

Rules should express intent.

Good:

```csharp
rules.Architecture.Layer("Domain")
    .CannotReference("Infrastructure");
```

Bad:

```csharp
rules.Enable("SL1004");
```

---

### IntelliSense Documentation

Methods should provide rich documentation.

Example:

```text
Requires async public methods to expose a CancellationToken parameter.

Why:
Prevents non-cancelable async operations.

Examples:
✔ Task SaveAsync(User user, CancellationToken ct)
✘ Task SaveAsync(User user)
```

---

# Example DSL

```csharp
using SharpLint;

Lint.Configure(config =>
{
    config.UsePreset(Presets.Recommended);

    config.Architecture(arch =>
    {
        arch.Layer("Domain")
            .CannotReference("Infrastructure");
    });

    config.Async(async =>
    {
        async.RequireCancellationTokens();
        async.RequireAsyncSuffix();
    });
});
```

---

# Rules Philosophy

## Rule Categories

Rules should map to developer mental models.

Examples:

* Architecture
* Async
* ASP.NET
* EF Core
* Dependency Injection
* Clean Code
* Testing
* Security
* Modernization
* AI Safety

Avoid compiler-oriented categories as the primary UX.

---

## Built-In Opinionated Defaults

SharpLint should ship with curated defaults.

Example presets:

```csharp
preset.Use("recommended");
preset.Use("clean-architecture");
preset.Use("aspnet");
preset.Use("strict");
```

---

# Architecture Enforcement

Architectural rules are a key differentiator.

Examples:

```csharp
rules.Architecture.Layer("Domain")
    .CannotReference("Infrastructure");
```

```csharp
rules.ForbidNamespace("System.Console")
    .Outside("Program.cs");
```

```csharp
rules.DependencyInjection.DisallowServiceLocator();
```

The tool should help encode:

* architecture decisions
* engineering standards
* repository governance
* organizational knowledge

---

# AI-Agent Workflow

SharpLint should optimize for:

```text
generate → analyze → fix → rerun
```

The tool should support:

* deterministic diagnostics
* structured outputs
* machine-readable schemas
* autofix capabilities
* fast iteration loops

---

# CLI Design

## Core Commands

### Analyze Repository

```bash
sharplint check
```

---

### Initialize Config

```bash
sharplint init
```

---

### JSON Output

```bash
sharplint check --format json
```

---

### Autofix

```bash
sharplint fix
```

Optional:

```bash
sharplint fix --rule async-suffix-required
```

---

# Diagnostics Design

## Human-Friendly Output

Example:

```text
✘ Domain layer references Infrastructure

File:
UserService.cs:42

Detected:
using Infrastructure.Persistence;

Suggested Fix:
Move the abstraction to Domain.Interfaces
```

---

## Machine-Friendly Output

Example:

```json
{
  "ruleId": "architecture.domain.infrastructure",
  "category": "architecture",
  "severity": "error",
  "message": "Domain layer cannot reference Infrastructure",
  "file": "UserService.cs",
  "line": 42,
  "suggestion": "Introduce abstraction in Domain layer"
}
```

---

# Performance Requirements

## Performance Is a Core Feature

SharpLint must optimize for:

* low startup time
* incremental analysis
* workspace reuse
* fast repeated execution
* low memory usage

---

## Performance Goals

### Small Repository

Target:

```text
< 1 second
```

### Medium Repository

Target:

```text
few seconds
```

---

# Incremental Analysis Strategy

The system should eventually support:

* cached Roslyn workspaces
* incremental analysis
* changed-file tracking
* dependency graph reuse

---

# Future Daemon Architecture

Future support:

```bash
sharplint daemon
```

Responsibilities:

* maintain workspace state
* cache semantic models
* file watching
* incremental diagnostics
* fast agent feedback loops

---

# Rule Performance Model

Rules should be categorized by cost.

## Syntax Rules

Fastest.

Examples:

* naming
* formatting
* suffix checks

---

## Semantic Rules

Require semantic analysis.

Examples:

* DI misuse
* async correctness
* EF Core misuse

---

## Architectural Rules

Require graph analysis.

Examples:

* forbidden dependencies
* layer boundaries

---

# Output Formats

SharpLint should support:

* human-readable CLI output
* JSON
* SARIF
* CI-friendly formats

This enables:

* GitHub Actions
* Azure DevOps
* AI integration
* repository automation

---

# Product Boundaries

## What SharpLint IS

* opinionated linter
* architecture validator
* governance engine
* policy-as-code system
* AI feedback layer

---

## What SharpLint Is NOT

Initially:

* custom compiler
* custom parser
* IDE-first product
* general-purpose scripting platform
* replacement for Roslyn

---

# Long-Term Vision

SharpLint may eventually evolve into:

* plugin ecosystem
* organizational presets
* repository governance platform
* modernization engine
* autofix/codemod framework
* AI coding policy system

Potential future capabilities:

* live config validation
* automated migrations
* modernization rules
* repository policy packs
* architecture visualization

---

# Key Differentiators

## 1. No Project Installation

No NuGet dependency required.

---

## 2. Fluent Typed DSL

Strong autocomplete and discoverability.

---

## 3. Opinionated Defaults

Works out of the box.

---

## 4. Architecture Rules

First-class support.

---

## 5. AI-Agent Friendly

Machine-readable diagnostics and fast iteration.

---

## 6. High Performance

Designed for continuous invocation.

---

## 7. Roslyn-Powered

Leverages compiler-grade analysis without exposing Roslyn complexity.

---

# Initial MVP Scope

## Included

* CLI
* Roslyn integration
* typed `sharplint.config.cs` configuration
* opinionated defaults
* 10–20 high-value rules
* architecture rules
* JSON output
* fast execution
* CI integration

---

## Deferred

* IDE integration
* daemon mode
* plugin marketplace
* distributed execution
* formatter
* advanced code fixes
* Rider/Visual Studio extensions

---


# Strategic Summary

SharpLint is intended to become:

> The modern developer-friendly governance and code quality layer for C# and .NET.

The project combines:

* Roslyn analysis power
* ESLint/Biome ergonomics
* architecture enforcement
* policy-as-code
* AI-assisted development workflows
* fast developer feedback loops

into one cohesive platform.

