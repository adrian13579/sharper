# Core rule taxonomy and modules

Status: adopted built-in module taxonomy, initial policies, and external Roslyn
module integration. The taxonomy groups SharpLint's initial rules. It is not a
classification system that external users must register into.

| Built-in module | Stable module ID | Responsibility |
| --- | --- | --- |
| `Rules.Naming` | `naming` | Identifier conventions, prefixes, suffixes, exact names, patterns. See [Naming](naming.md). |
| `Rules.Syntax` | `syntax` | Preferred/permitted C# constructs; no rewriting. |
| `Rules.Organization` | `organization` | Source file and declaration structure; no whitespace formatting. |
| `Rules.Design` | `design` | Local type, member, and API constraints. |
| `Rules.Dependencies` | `dependencies` | Explicit package, namespace, and reference policies. |
| `Rules.Architecture` | `architecture` | Logical namespace and type boundaries. |
| `Rules.Reliability` | `reliability` | Statically detectable exception and dangerous-code patterns. |
| `Rules.Performance` | `performance` | Statically detectable allocation and conversion policies; no benchmarking. |
| `Rules.Project` | `project` | Project compiler/framework declarations and build conventions. |

`SharpLint.Core.Rules.Rules` is the read-only catalog of nine built-in
`RuleModule` values. Each module has an ID, display name, and provided rules.
Naming uses its existing specialized DSL and selector-chain evaluator. Its
rules' provider is module `naming`; existing naming rule IDs and precedence
remain unchanged. Merely referencing a module enables no rules.

## Built-in configuration

```csharp
#:package SharpLint.Configuration@1.0.0-dev

using SharpLint.Configuration;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Naming;
using SharpLint.Core.Rules;

Lint.Configure(config => config
    .Naming(n => n.Classes().RequireConvention(NamingConvention.PascalCase))
    .Module(Rules.Syntax, m => m
        .Rule("syntax.no-goto")
        .Rule("syntax.file-scoped-namespaces"))
    .Module(Rules.Design, m => m
        .Rule("design.maximum-parameters", new { maximum = 5 }, Severity.Error))
    .Module(Rules.Project, m => m
        .Rule("project.nullable-enabled")
        .Rule("project.target-frameworks", new { allowed = new[] { "net10.0" } })))
    .Apply();
```

`Module` validates that the chosen module owns each full rule ID. The generic
`config.Rule(id, JsonElement, severity)` API remains available. A configured ID
selects severity and JSON options; the corresponding policy validates its options.
Duplicate configured rule IDs are rejected. `Hidden` rules are validated for
registration but do not execute. No rules are enabled implicitly by registration.

### Initial built-in policies

All options objects default to `{}`. Required options below must be supplied.
Array options must contain at least one nonblank string; limits must be positive
integers. Invalid options fail explicitly instead of silently disabling checks.

| Rule ID | Options | Observable behavior |
| --- | --- | --- |
| `syntax.no-goto` | None | Reports goto statements, including goto case/default. |
| `syntax.file-scoped-namespaces` | None | Reports block namespace declarations. Global-namespace files are allowed. |
| `organization.one-type-per-file` | None | Reports each top-level type after the first, including records, enums, structs, interfaces and delegates. Nested types do not count. |
| `organization.match-type-file-name` | None | Requires each top-level type's identifier to equal the file stem, ordinally. Partial declarations are checked individually. |
| `design.maximum-parameters` | `maximum` (default 5) | Reports parameter lists over the limit, including constructors, primary constructors, local functions, lambdas and indexers. |
| `design.maximum-type-members` | `maximum` (default 20) | Reports type declarations whose direct member declaration count exceeds the limit; nested members are not added to the outer type's count. |
| `dependencies.forbidden-usings` | `namespaces` (required) | Reports explicit using names matching a namespace or its dotted descendants, including aliases, global and static using directives. This syntax policy does not claim to resolve all dependencies. |
| `dependencies.forbidden-packages` | `packages` (required) | Reports matching PackageReference/PackageVersion Include/Update declarations in `.csproj` and `Directory.Packages.props`, ignoring package ID case. |
| `architecture.namespace-boundaries` | `from` and `to` (required string and string array) | Reports resolved name references from the source namespace or descendants to forbidden target namespaces or descendants. Resolves fully qualified names and aliases using project semantic models. |
| `architecture.forbidden-inheritance` | `types` (required) | Reports explicit base type/interface declarations matching fully qualified semantic type names. |
| `reliability.empty-catch` | None | Reports catch blocks with no statements, including comment-only blocks. |
| `reliability.rethrow-caught-exception` | None | Reports `throw ex;` for the enclosing catch variable; suggests `throw;`. |
| `performance.empty-array-allocation` | None | Reports explicit `new T[0]` allocations, including jagged arrays; skips rectangular arrays and computed sizes. Suggests considering `Array.Empty<T>()` only when shared identity is acceptable. |
| `performance.explicit-boxing` | None | Reports explicit casts classified as boxing by Roslyn, including nullable value types. Does not claim to detect implicit boxing. |
| `project.nullable-enabled` | None | Requires an explicit Nullable declaration in every `.csproj`; reports each declaration whose trimmed text is not `enable`. |
| `project.target-frameworks` | `allowed` (required) | Requires explicit TargetFramework/TargetFrameworks declarations; reports each framework outside the ordinal allowlist. |

Architecture and explicit-boxing policies require host-provided project
compilations and a validated error-free compilation. If unavailable, they report
an `AnalysisFailure` and mark the result incomplete while retaining safe findings.
Scopes are metadata, not cache invalidation declarations.

Project and package policies inspect literal XML in the requested directory tree,
skipping `bin`, `obj`, `.git`, and `.sharplint`. They do **not** evaluate MSBuild
conditions, imports, inherited values, or property substitutions. For example,
a Nullable value inherited from Directory.Build.props does not satisfy the explicit
project declaration policy. Conditional declarations are checked individually.
Malformed XML fails explicitly. No rule claims to cover all examples in a module's
responsibility; this table defines the implemented initial set.

## External modules use Roslyn analyzers

External users create their own module identities and group standard Roslyn
`DiagnosticAnalyzer` implementations into them. No core category registration,
custom `IRuleAnalyzer` implementation, or edit to SharpLint's catalog is needed.
A descriptor's category is provider metadata; it does not select its module.
One analyzer can supply multiple rule descriptors. IDs must be globally unique.

Given a package `Acme.SharpLint` exporting public, parameterless
`Acme.SecurityAnalyzer : DiagnosticAnalyzer`, executable configuration can use:

```csharp
#:package SharpLint.Configuration@1.0.0-dev
#:package Acme.SharpLint@1.0.0

using Acme;
using SharpLint.Configuration;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Rules;

var security = new RuleModule("acme.security", "Acme Security", [new SecurityAnalyzer()]);

Lint.Configure(config => config.UseModule(security, module => module
    .Rule("ACME001", new { maximum = 5 }, Severity.Error)))
    .Apply();
```

This is a provider integration example; Acme is not a shipped package. `UseModule`
registers the module and optionally configures its rules. Additional rules can be
selected with `config.Rule(...)`. Analyzer types must be public, parameterless,
and stored in a physical assembly. Configuration serializes module identity,
assembly paths and type names, plus configured IDs/options/severity; it never
serializes analyzer instances. These assembly paths are local execution references,
not a portable configuration format for another machine.

`SharpLintConfig.CreateRegistry()` loads the referenced types and combines them
with built-ins. The stock CLI and daemon call it automatically. A custom host can
pass its existing registry to `CreateRegistry(hostRegistry)`. Missing assemblies,
types, constructors, duplicate module/rule IDs, and analyzer exceptions fail
explicitly. There is no automatic assembly scanning or analyzer discovery.
External code executes with the same trust as executable configuration.

Roslyn analyzers run via [Compilation.WithAnalyzers](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.diagnosticanalyzerextensions.withanalyzers)
against the host's project compilations. Syntax, symbol, operation, semantic-model,
and compilation callbacks work through the standard Roslyn driver. JSON option
properties are exposed as strings through AnalyzerConfigOptionsProvider under
`sharplint.<rule-id>.<property>`; for example `sharplint.ACME001.maximum`.
The driver runs each configured descriptor separately, suppresses sibling IDs,
and explicitly enables configured descriptors even if disabled by default.
Source suppressions remain effective. Configured SharpLint severity controls the
reported finding. Additional files and project `.editorconfig` values are not
currently forwarded to external module analyzers.

A direct Core host may supply `AnalysisContext.Compilations`. Without them, the
adapter creates a syntax-host compilation with only the core runtime reference;
this is suitable for syntax policies, not complete project semantic analysis.
Providers requiring validated semantics can declare `ErrorFreeCompilation` in
explicit host registrations. The CLI workspace supplies real project compilations.

## Registration, diagnostics, and execution

`RuleRegistry` snapshots modules and sorts registrations ordinally. Built-in
module IDs and `sharplint` are reserved. Existing generated `naming.*` rule IDs
are reserved to Naming. Other duplicate IDs are rejected even for identical
implementations. Provider names and descriptor categories are case-sensitive.
The old `RuleCategory`, `RuleRegistration`, and `IRuleAnalyzer` host registration
path remains a compatibility API; it does not define the new extension model.

Diagnostics carry `ModuleId` (CLI JSON `module`) independently of `CategoryId`
(CLI JSON `category`). Descriptor categories are preserved for external analyzers.
Naming's category remains `naming`; its module is now `naming`. Compiler diagnostics
retain their compiler origin and are separate from configured module rules.
Roslyn source spans and help links are preserved; compilation-wide findings have
`HasSourceLocation = false`. Results sort by file, line, column, preserving emission
order for ties. Providers must emit deterministic findings and honor cancellation.

The incremental host caches Naming per document. All other configured policies
run over the complete captured inputs on every check, including unchanged checks.
Custom analyzers receive project compilations; module scope does not imply narrow
cache invalidation. Referenced analyzer assemblies participate in input reconciliation.
Source generators and analyzer references discovered from target projects are not
automatically executed. See [compiler integration](../compiler-diagnostics.md) and
[daemon behavior](../daemon.md). No source rewriting, autofix, benchmarking, AI
inference, or execution of target applications is introduced.
