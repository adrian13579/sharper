> Legacy compatibility library documentation. The LSP diagnostic service does not execute this DSL or its analyzer module framework. See [README](../README.md) for the active service.

# Core rule reference

SharpLint ships nine built-in modules: Naming and eight modules containing 16
fixed policy IDs. This guide documents their current implementation, with
configuration examples, reported constructs, and boundaries. The adopted
[Naming specification](spec/naming.md), [module contract](spec/taxonomy.md),
and [fluent policy specification](spec/policies.md) define the configuration
contract; [SPEC.md](../SPEC.md) contains future plans.

## Configure rules

Save the following as `sharplint.config.cs`. The development package must be
available through your NuGet sources; see the [quick start](../README.md#quick-start).

```csharp
#:package SharpLint.Configuration@1.0.0-dev

using SharpLint.Configuration;
using SharpLint.Core.Naming;

Lint.Configure(config => config
    .UsePreset(Preset.Recommended)
    .Syntax(s =>
    {
        s.GotoStatements().Forbid().AsError();
        s.Namespaces().RequireFileScoped();
    })
    .Design(d => d.Parameters().RequireMaximumCount(5))
    .Project(p =>
    {
        p.Nullable().RequireEnabled();
        p.TargetFrameworks().RequireAllowed("net10.0");
    }))
    .Apply();
```

Run `sharplint check --path <directory>` against the directory containing the
configuration. In this example, goto findings are errors; the other selected
policies are warnings. `Preset.Recommended` adds only Naming rules. It does not
enable any of the 16 fixed policies.

The remaining configuration snippets are fragments for the `Lint.Configure`
callback above. Source snippets illustrate individual rules independently;
other enabled rules can also report findings in the same source.

- Each category callback selects targets, attaches `Require…` or `Forbid…`
  requirements, and optionally applies `.AsError()`, `.AsWarning()`, `.AsInfo()`,
  or `.Disabled()`. Selectors alone enable no rules. As in Naming, modifiers
  apply to the most recent requirement on that selector chain.
- Severity defaults to warning. Error findings make a completed CLI check return
  exit code `1`; warnings and information do not. Disabled rules do not execute.
- A fixed policy ID may be configured only once, including across fluent and
  generic declarations. Put multiple forbidden namespaces or packages in one
  verb call. Naming retains its separate selector precedence, described below.
- Fluent arguments are typed. Limits must be positive integers; lists must contain
  at least one nonblank string. Invalid arguments fail during configuration, even
  on a requirement you intend to disable. Strings are used without trimming, and
  options are snapshotted when the verb is called.
- `config.Module(Rules.Design, m => m.Rule(...))` and the generic
  `config.Rule(id, JsonElement, severity)` API remain available for compatibility
  and external modules. Their JSON option validation occurs when a rule executes.
  The options column below documents the unchanged serialized contract.

## Policy index

Input is either source syntax, literal project XML, or validated project
semantics. Scope is registered metadata; it does not restrict files inspected
or promise incremental caching at that scope.

| Rule ID | Scope | Input | Options |
| --- | --- | --- | --- |
| [syntax.no-goto](#syntaxno-goto) | SourceFile | Syntax | None |
| [syntax.file-scoped-namespaces](#syntaxfile-scoped-namespaces) | Namespace | Syntax | None |
| [organization.one-type-per-file](#organizationone-type-per-file) | SourceFile | Syntax | None |
| [organization.match-type-file-name](#organizationmatch-type-file-name) | Type | Syntax | None |
| [design.maximum-parameters](#designmaximum-parameters) | Symbol | Syntax | `maximum`, default `5` |
| [design.maximum-type-members](#designmaximum-type-members) | Type | Syntax | `maximum`, default `20` |
| [dependencies.forbidden-usings](#dependenciesforbidden-usings) | SourceFile | Syntax | Required `namespaces` array |
| [dependencies.forbidden-packages](#dependenciesforbidden-packages) | Project | XML | Required `packages` array |
| [architecture.namespace-boundaries](#architecturenamespace-boundaries) | Repository | Semantics | Required `from` string and `to` array |
| [architecture.forbidden-inheritance](#architectureforbidden-inheritance) | Type | Semantics | Required `types` array |
| [reliability.empty-catch](#reliabilityempty-catch) | Symbol | Syntax | None |
| [reliability.rethrow-caught-exception](#reliabilityrethrow-caught-exception) | Symbol | Syntax | None |
| [performance.empty-array-allocation](#performanceempty-array-allocation) | Symbol | Syntax | None |
| [performance.explicit-boxing](#performanceexplicit-boxing) | Symbol | Semantics | None |
| [project.nullable-enabled](#projectnullable-enabled) | Project | XML | None |
| [project.target-frameworks](#projecttarget-frameworks) | Project | XML | Required `allowed` array |

## Naming

Naming uses `config.Naming(...)` to generate IDs such as
`naming.class.public.convention` and `naming.method.async.suffix`. These IDs
describe a selector and verb; there is no fixed list of Naming policies to
enable through `Module(...).Rule(...)`.

```csharp
config.Naming(n =>
{
    n.Classes().RequireConvention(NamingConvention.PascalCase);
    n.Interfaces().RequirePrefix("I").ForbidPrefix("IInternal");
    n.Methods(m => m.Async()).RequireSuffix("Async").AsError();
    n.Fields(f => f.Private())
        .RequirePrefix("_")
        .RequireConvention(NamingConvention.CamelCase);
});
```

This permits `OrderService`, `IOrderService`, `LoadAsync`, and `_orderCount` in
their respective selectors. It reports `order_service`, `IInternalService`, an
async method named `Load`, and a private field named `orderCount`.

Selectors cover classes, interfaces, records, enums, methods, properties,
fields, parameters, local declaration statements, and namespace declarations.
Struct and enum-member naming selectors are not implemented. Method selectors
visit method declarations, excluding constructors and local functions. Local
variable selectors do not include foreach variables or deconstruction bindings.
`Async()` matches the explicit keyword; it does not infer awaitable return types.
Namespace matching uses each declaration's written name; nested block namespace
names are not combined with their parents.

The five verb pairs require or forbid a convention, prefix, suffix, exact name,
or regex. Prefixes, suffixes, and exact names use ordinal, case-sensitive matching;
multiple values in one call are alternatives. Regex matching adds missing leading
`^` and trailing `$` anchors. Conventions use ASCII letters and digits:

| Convention | Passing examples | Failing examples |
| --- | --- | --- |
| `PascalCase` | `OrderService`, `_Order`, `IO` | `orderService`, `Order_Service` |
| `CamelCase` | `orderService`, `_order`, `__order2` | `OrderService`, `order_service` |
| `SnakeCase` | `order_service`, `order2` | `_order`, `order__service` |
| `UpperSnakeCase` | `ORDER_SERVICE`, `ORDER2` | `_ORDER`, `Order_Service` |

Namespace conventions check each dot-separated segment. PascalCase and CamelCase
permit leading underscores; add an explicit prefix policy if you need to forbid
them. Naming currently evaluates identifier token text, including a leading `@`
on escaped identifiers.

For each verb, the last applicable selector chain wins, and all constraints of
that verb within the winning chain compose with AND. Different verbs resolve
independently. For example, a later public-class suffix selector can replace an
earlier class suffix selector while leaving its convention constraint active.
Hidden constraints participate in selection and are then skipped individually.
Severity modifiers and `.ExceptWithAttribute<T>()` affect only the most recent
constraint. Attribute exemptions compare written simple names, with the optional
`Attribute` suffix removed; they do not resolve attribute identity semantically.
See the [full Naming reference](spec/naming.md) for predicates, IDs, and precedence
examples.

## Syntax

### syntax.no-goto

```csharp
config.Syntax(s => s.GotoStatements().Forbid());
```

Reports each goto statement: a named-label jump, `goto case`, or `goto default`.
A label without a goto is allowed. The finding starts at the goto statement.

```csharp
// Reported
goto finished;

// Allowed by this rule
return;
```

The check does not analyze control flow or suggest an automatic rewrite.

### syntax.file-scoped-namespaces

```csharp
config.Syntax(s => s.Namespaces().RequireFileScoped());
```

Reports each block namespace declaration, including nested declarations. A
file-scoped namespace or a file with no namespace declaration passes.

```csharp
namespace Orders { class Order {} } // Reported
```

```csharp
namespace Orders; // Allowed
class Order {}
```

## Organization

### organization.one-type-per-file

```csharp
config.Organization(o => o.Files().RequireOneType());
```

Counts types directly under the compilation unit or any namespace declaration
across the whole file. Classes, records, structs, interfaces, enums, and delegates
count. Reports every declaration after the first, at that declaration's start.
Nested types do not count toward the file limit.

```csharp
class Order {}
record Receipt; // Reported: second top-level type
enum Status {}  // Reported: third top-level type
```

```csharp
class Order { private class State {} } // Allowed: one top-level type
```

### organization.match-type-file-name

```csharp
config.Organization(o => o.Files().RequireMatchingTypeName());
```

Each top-level type from the same set above must match the file stem exactly,
using ordinal, case-sensitive comparison. In `Order.cs`, `class Order {}` passes
and `class order {}` reports a finding at the declaration. Nested types are
ignored. The identifier's value is used, so `class @Order {}` matches `Order.cs`.

Partial declarations are checked independently: `partial class Order {}` in
`Order.Generated.cs` is reported because the stem is `Order.Generated`. Generic
arity is not part of the name: `class Order<T> {}` matches `Order.cs`.

## Design

### design.maximum-parameters

```csharp
config.Design(d => d.Parameters().RequireMaximumCount(2));
```

Reports parameter lists containing more than `maximum` parameters. The default
is `5`; exactly the limit passes. Counts declared parameters, including optional
ones, rather than arguments at call sites. A `params` parameter counts as one.

```csharp
void Save(int id, string name, bool active) {} // Reported with maximum = 2
void Save(int id, string name) {}             // Allowed
```

Checks parenthesized and bracketed parameter-list syntax, including methods,
delegates, constructors, primary constructors, local functions, parenthesized
lambdas, anonymous methods, operators, and indexers. A simple lambda such as
`x => x` has no parameter-list node and is not checked. Findings start at the
parameter list. No overload aggregation or semantic API analysis is performed.

### design.maximum-type-members

```csharp
config.Design(d => d.TypeMembers().RequireMaximumCount(2));
```

Reports class, struct, interface, and record declarations with more than
`maximum` direct member declarations. The default is `20`; exactly the limit
passes. Counts syntax declarations rather than symbols:

```csharp
class Order
{
    int id, version;       // One field declaration
    public int Id => id;    // One property declaration
    void Save() {}          // Third declaration: Order is reported with maximum = 2
}
```

A nested type counts as one outer member and is checked independently. Its
members are not added to the outer count. Partial declarations are not combined.
Enum entries, generated record members, and primary constructor parameters do not
contribute to this count. Findings start at the type declaration.

## Dependencies

### dependencies.forbidden-usings

```csharp
config.Dependencies(d => d.Usings().ForbidNamespaces("System.IO", "System.Net"));
```

Reports explicit using names equal to a configured string or starting with that
string followed by a dot. Matching is ordinal and case-sensitive; `global::` is
removed before comparison. Alias, global, and static directives are included.

```csharp
using System.IO;                      // Reported
using Files = global::System.IO.File;  // Reported
using static System.IO.File;          // Reported
using System.IOExtra;                 // Allowed: no dotted-prefix match
```

Findings start at the using directive. Fully qualified references in code without
a using directive are not checked. This rule does not resolve aliases or discover
implicit project imports; use semantic architecture policies for resolved source
references.

### dependencies.forbidden-packages

```csharp
config.Dependencies(d => d.Packages().ForbidNames("Unsafe.Package"));
```

Inspects `PackageReference` and `PackageVersion` elements in `.csproj` and
`Directory.Packages.props` files. Reads `Include`, falling back to `Update` when
`Include` is absent, and compares the complete package ID without case sensitivity.

```xml
<PackageReference Include="unsafe.package" Version="1.0.0" /> <!-- Reported -->
<PackageVersion Update="Unsafe.Package" Version="1.0.0" />    <!-- Reported -->
<PackageReference Include="Unsafe.PackageExtra" />           <!-- Allowed -->
```

Reports each matching declaration at its XML location, regardless of version or
MSBuild condition. It does not inspect transitive packages, restore assets, or
package declarations in other files such as `Directory.Build.props`. See the
[XML boundaries](#project-xml-boundaries).

## Architecture

Both architecture policies require [validated project semantics](#semantic-input-requirements).

### architecture.namespace-boundaries

```csharp
config.Architecture(a => a.Namespaces("Domain")
    .ForbidReferencesTo("Infrastructure", "Persistence"));
```

Reports resolved name references whose containing namespace matches `from` and
whose target namespace matches any `to` entry. Both comparisons include dotted
descendants and are ordinal and case-sensitive. `Domain.Child` is included;
`DomainExtra` is not. Namespace declaration names themselves are skipped.

```csharp
namespace Infrastructure { public class Store {} }
namespace Domain
{
    class Order
    {
        Infrastructure.Store store = new(); // Reported reference
    }
}
```

References from `Application` to `Infrastructure.Store` pass this configuration.
Fully qualified names and resolved alias uses are checked. A using directive
inside a matching namespace can also produce a finding. The analyzer checks
outermost name syntax and reports at that name; it does not build a complete
dependency graph or infer dependencies from every operation. Namespace spelling
and target resolution come from Roslyn's semantic model.

### architecture.forbidden-inheritance

```csharp
config.Architecture(a => a.Inheritance()
    .ForbidTypes("System.Exception", "System.IDisposable"));
```

Reports explicit base class and interface entries whose resolved type display
name exactly matches an entry in `types`, using ordinal comparison.

```csharp
class OrderException : System.Exception {}    // Reported base type
interface IResource : System.IDisposable {}   // Reported interface
class Order {}                               // Allowed
```

Names must match Roslyn's `ToDisplayString()` representation, including generic
type arguments when applicable. The rule does not perform prefix matching or
walk transitive inheritance. A class deriving from an intermediate class is not
reported solely because that intermediate class derives from a forbidden type.

## Reliability

### reliability.empty-catch

```csharp
config.Reliability(r => r.CatchBlocks().ForbidEmpty());
```

Reports catch clauses whose block contains zero statements. Whitespace and
comments do not count; catch declarations and filters do not exempt an empty
block. Findings start at the catch clause.

```csharp
try { Save(); }
catch { /* ignored */ } // Reported
```

```csharp
try { Save(); }
catch { throw; } // Allowed
```

Any statement, including an empty statement, satisfies this syntax check. It
does not establish that exceptions are handled meaningfully.

### reliability.rethrow-caught-exception

```csharp
config.Reliability(r => r.Rethrows().RequirePreservedStackTrace());
```

Reports a throw statement whose expression is a bare identifier with the same
name as the nearest enclosing catch variable. The diagnostic recommends `throw;`
to preserve the caught exception's stack trace.

```csharp
try { Save(); }
catch (System.Exception ex) { throw ex; } // Reported at throw
```

```csharp
try { Save(); }
catch (System.Exception ex) { throw; } // Allowed
```

This is a syntax check. Throwing another variable, `throw (ex);`, and throw
expressions are outside the match. It stops searching for the catch at a lambda,
anonymous method, or local function boundary, so a throw inside such a function
is not attributed to an outer catch. It does not resolve variable identity.

## Performance

### performance.empty-array-allocation

```csharp
config.Performance(p => p.Arrays().ForbidEmptyAllocation());
```

Reports explicit array allocations with a single first-rank size that is a zero
integer literal. Handles `0`, `0U`, `0L`, and `0UL`, including jagged arrays.

```csharp
var values = new int[0];       // Reported
var rows = new int[0][];       // Reported
var shared = System.Array.Empty<int>(); // Allowed
var matrix = new int[0, 0];    // Allowed: rectangular array
var computed = new int[1 - 1]; // Allowed: computed size
```

Findings start at the allocation and recommend considering `Array.Empty<T>()`
when shared identity is acceptable. No constant folding is performed. Implicit
arrays, omitted sizes, and collection expressions are outside this check. Reusing
an empty array changes reference identity, so the recommendation is not an
automatic replacement.

### performance.explicit-boxing

```csharp
config.Performance(p => p.Conversions().ForbidExplicitBoxing());
```

Requires [validated project semantics](#semantic-input-requirements). Reports
explicit cast expressions when Roslyn classifies the conversion between the
operand type and target type as boxing, including nullable value types.

```csharp
object value = (object)42;      // Reported at cast
int? count = 1;
object maybe = (object)count;   // Reported at cast
object text = (object)"order"; // Allowed: reference conversion
object implicitBox = 42;       // Outside this check: no explicit cast
```

The rule classifies conversions statically; it does not measure allocation cost
or report implicit boxing in assignments, arguments, or other expressions.

## Project

### project.nullable-enabled

```csharp
config.Project(p => p.Nullable().RequireEnabled());
```

Requires each `.csproj` to contain at least one `Nullable` element. Each
declaration must have trimmed text exactly equal to `enable`. Reports a missing
declaration at the project root or an invalid value at that element.

```xml
<Nullable>disable</Nullable>  <!-- Reported -->
<Nullable>annotations</Nullable> <!-- Reported -->
<Nullable> enable </Nullable> <!-- Allowed -->
```

Conditional declarations are checked individually, even if another declaration
enables nullable analysis. A value inherited solely from `Directory.Build.props`
does not satisfy this explicit-declaration policy. Source `#nullable` directives
are not inspected.

### project.target-frameworks

```csharp
config.Project(p => p.TargetFrameworks().RequireAllowed("net10.0", "net10.0-windows"));
```

Requires an explicit `TargetFramework` or `TargetFrameworks` element in every
`.csproj`. Splits each declaration on semicolons, trims each entry, and reports
every entry outside the ordinal, case-sensitive allowlist at the element's
location. A missing declaration reports at the project root.

```xml
<TargetFrameworks>net10.0;net9.0</TargetFrameworks> <!-- Reports net9.0 -->
<TargetFramework>net10.0-windows</TargetFramework> <!-- Allowed -->
```

Empty entries, including one introduced by a trailing semicolon, are reported.
The list restricts permitted frameworks; it does not require every allowed
framework to appear. Conditional declarations are checked individually.

### Project XML boundaries

Package and project policies recursively inspect literal XML under the requested
directory, skipping `bin`, `obj`, `.git`, `.sharplint`, and linked directories.
They can run without C# source files. They read `.csproj` and
`Directory.Packages.props`; only `.csproj` files receive nullable and framework
checks. Malformed XML fails analysis explicitly.

These rules do not evaluate MSBuild conditions, imports, inherited properties,
or substitutions such as `$(TargetFramework)`. They do not calculate effective
build settings. Keep that distinction in mind when enabling explicit project
declaration policies in a repository that centralizes settings.

## Semantic input requirements

`architecture.namespace-boundaries`, `architecture.forbidden-inheritance`, and
`performance.explicit-boxing` require host-provided project compilations and
`AnalysisContext.CompilationHasErrors == false`. Each analyzed tree must belong
to a supplied compilation. Direct Core callers must provide these inputs;
`Analyzer.AnalyzeAsync` parses files but does not load an MSBuild workspace.

The LSP diagnostic service does not run these legacy policies or supply this
AnalysisContext. Hosts embedding the retained Core library must supply their own
compilations and handle AnalysisFailure entries when validity is unknown.

## Diagnostics and verification

Each fixed policy emits its full ID, configured severity, and matching module
and category IDs. Naming emits its generated IDs with module/category `naming`.
Findings carry a message and a file location with one-based line and column.
Recommendations described above appear in the diagnostic message; these built-in
policies do not provide source edits or autofixes. This is the legacy library
model; the active CLI/MCP service uses the zero-based LSP model described in the
[service contract](lsp-service.md). The old incremental host and daemon were removed.

Implementation and regression coverage:

- [BuiltInPolicies.cs](../src/SharpLint.Core/Rules/BuiltInPolicies.cs) defines
  fixed IDs, scopes, source checks, and semantic checks.
- [ProjectPolicies.cs](../src/SharpLint.Core/Rules/ProjectPolicies.cs) implements
  literal package and project checks.
- [BuiltInModuleTests.cs](../tests/SharpLint.Tests/Analysis/BuiltInModuleTests.cs)
  exercises configuration, compliant and violating inputs, emitted diagnostics,
  option validation, and policy boundaries.
- [Naming tests](../tests/SharpLint.Tests/Naming) and
  [AnalyzerTests.cs](../tests/SharpLint.Tests/Analysis/AnalyzerTests.cs) cover
  naming evaluation and integration.

For external Roslyn analyzers and module registration, see the
[extension contract](spec/taxonomy.md#external-modules-use-roslyn-analyzers).
