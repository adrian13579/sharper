# Core rule taxonomy

Status: adopted category and module contracts. Naming is the only category with
built-in policies. The other eight categories are classifications available to
rule providers; accessing them enables no validation.

Categories describe engineering concerns, independently of implementation:

| Catalog entry | Stable ID | Purpose and boundary |
| --- | --- | --- |
| `Rules.Naming` | `naming` | Identifier conventions, prefixes, suffixes, exact names, and patterns. See [implemented Naming rules](naming.md); struct and enum-member selectors remain deferred. |
| `Rules.Syntax` | `syntax` | Permitted or preferred C# constructs, such as `var`, expression bodies, patterns, and language features. No rewriting. |
| `Rules.Organization` | `organization` | Source-file and declaration structure: primary types, file names, namespace/directory correspondence, and placement. Excludes whitespace formatting. |
| `Rules.Design` | `design` | Local type, member, and API constraints: parameter/dependency counts, complexity, inheritance, coupling, cohesion, and visibility. Application-wide boundaries belong to Architecture. |
| `Rules.Dependencies` | `dependencies` | Explicit package, assembly, project, namespace, or symbol relationships, including versions and cycles. Logical responsibilities belong to Architecture. |
| `Rules.Architecture` | `architecture` | Logical layers, module isolation, component boundaries, dependency direction, and inversion policies. Classification does not depend on how references are analyzed. |
| `Rules.Reliability` | `reliability` | Statically detectable exception, disposal, async blocking, cancellation, synchronization, concurrency, and dangerous API patterns. Findings must state only guarantees supported by the analysis. |
| `Rules.Performance` | `performance` | Static allocation, boxing, copying, LINQ, and efficiency policies. No benchmarking or target application execution. |
| `Rules.Project` | `project` | Target frameworks, nullable settings, compiler/language options, central package management, and build conventions. Reference relationships belong to Dependencies. |

`SharpLint.Core.Rules.Rules` is the centralized, read-only catalog.
`RuleCategory` is an immutable value with an ID and a display name. IDs are
ordinal and case-sensitive; display names are labels and do not determine identity.
Extension categories need no changes to an enum, catalog, or execution switch.

## Vocabulary

- **Category:** what engineering concern a rule enforces. A rule has one canonical
  category ID, even if its implementation shares infrastructure with other categories.
- **Rule:** the precise policy, identified by `RuleDefinition.Id`.
- **Scope:** where the policy applies, recorded by `RuleDefinition.Scope`:
  source file, symbol/member, type, namespace, project, or repository/solution.
  Scope is metadata, independent of category and invocation strategy.
- **Module:** who provides the rule. `RuleModule.Id` owns its declared categories
  and rules. Modules may use categories owned by another module.
- **Analyzer:** how a policy is evaluated, registered as an `IRuleAnalyzer`.
  One implementation may serve different rules and categories.
- **Configuration:** `ConfiguredRule` selects an ID, severity, and JSON options.
  The registered analyzer interprets and validates rule-specific options.

## Configuration

Existing executable Naming configuration remains supported:

```csharp
#:package SharpLint.Configuration@1.0.0-dev

using SharpLint.Configuration;
using SharpLint.Core.Naming;

Lint.Configure(config => config.Naming(n =>
    n.Classes().RequireConvention(NamingConvention.PascalCase))).Apply();
```

The conceptual category surface is `Rules.Naming`, `Rules.Syntax`,
`Rules.Organization`, `Rules.Design`, `Rules.Dependencies`, `Rules.Architecture`,
`Rules.Reliability`, `Rules.Performance`, and `Rules.Project`, imported from
`SharpLint.Core.Rules`. These are metadata values. Category-specific authoring
continues to use the existing callback convention, `config.Naming(...)`.
There are no placeholder `config.Syntax(...)` or other unimplemented policy methods.
Future category builders can follow Naming's convention when actual policies ship.

A custom host can configure a registered module policy with the generic DSL:

```csharp
using System.Text.Json;
using SharpLint.Configuration;
using SharpLint.Core.Analysis;
using SharpLint.Core.Diagnostics;
using SharpLint.Core.Rules;

// `policyAnalyzer` is an IRuleAnalyzer implemented by the provider.
var definition = new RuleDefinition("acme.parameter-limit", Rules.Design.Id, RuleScope.Symbol);
var registry = new RuleRegistry([
    new RuleModule("acme", [], [new RuleRegistration(definition, policyAnalyzer)])
]);
var config = Lint.Configure(c => c.Rule(definition.Id,
    JsonSerializer.SerializeToElement(new { maximum = 5 }), Severity.Error));
var result = await new Analyzer().AnalyzeAsync(new AnalysisContext
{
    WorkingDirectory = sourceDirectory,
    NamingRules = config.NamingRules,
    Rules = config.Rules ?? [],
    Registry = registry,
});
```

This is a host integration example, not a shipped parameter-count policy.
For an extension category, supply `new RuleCategory("acme.security", "Security")`
in the module's category list and use its ID in the rule definition. The registry
registers all categories before resolving rules, so module input order does not
restrict category sharing.

## Registration and execution guarantees

`RuleRegistry` snapshots explicit in-process module registrations. It registers
all nine core categories under module `sharplint`, sorts module/category/rule
registration ordinally, and rejects duplicate module, category, or rule IDs,
unknown categories, missing analyzers, and empty identifiers. The `sharplint`
module ID and existing `naming.*` rule IDs are reserved. Duplicate IDs are rejected
even if definitions appear identical. No assembly scanning or plugin loading occurs.

Naming retains its specialized selector-chain resolution and generated IDs.
`NamingRule.Definition` and `ModuleId` expose its category, scope, and provider
without changing Naming's configuration JSON. Repeated Naming declarations still
follow [Naming precedence](naming.md); they are not duplicate module registrations.

Module rules run only when explicitly configured. Duplicate configured module
rule IDs are rejected. Missing registrations fail even for Hidden rules and empty
source directories. Hidden rules with valid registrations do not invoke analyzers.
An empty category produces no diagnostics and makes no claim that its example
policies have been checked. `FilesAnalyzed` counts parsed files, not policy coverage.

The shared analyzer passes a read-only list of parsed syntax trees, the working
directory, JSON options, and cancellation token to each enabled module policy once
per analysis. The host attaches registered identity/category and configured
severity to returned violations. Category and scope do not create separate engines.
Source files are processed in ordinal path order; results are stably ordered by
file, line, and column, retaining declaration/emission order for ties. Providers
must emit tied findings deterministically and honor cancellation and read-only
operation. Contracts do not sandbox provider code.

Diagnostics expose `CategoryId` in Core; CLI JSON adds `category` alongside existing
fields. Naming IDs, severity, messages, locations, suggestions, and pretty output
retain their meanings. Elapsed time is operational metadata and is not deterministic.

Configuration JSON transports configured IDs/options/severity, never executable
analyzers. The stock CLI has the default registry and cannot load custom modules;
a custom host must supply the corresponding registry. Unsupported configured rules
fail explicitly. Module providers still receive syntax inputs, without semantic models. The
coordinator separately loads project compilations for compiler diagnostics;
execution scope does not itself request semantic data. No example
policies outside Naming are implemented, and no source modification, formatting,
code fixes, AI inference, or execution of target applications is introduced.

`RuleDefinition.InputRequirement` defaults to `Syntax`. Providers can declare
`ErrorFreeCompilation`; a host unable to validate an error-free compilation reports
an explicit failure for that rule, retains safe findings, and marks the analysis
incomplete. Hidden rules do not execute. See [compiler integration](../compiler-diagnostics.md).

The [incremental analysis host](../daemon.md) maintains a persistent project workspace
and caches built-in Naming results per document. Custom module policies still run
once over the entire captured syntax snapshot on every check, including unchanged
checks. Scope metadata is not a dependency declaration and never authorizes narrowing
cache invalidation. Unknown module dependencies therefore remain conservative.
