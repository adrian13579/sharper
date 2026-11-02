# Built-in policy fluent DSL

Status: adopted for the 16 built-in policies outside Naming. These builders
author the existing rule IDs and JSON options; analysis behavior and input
requirements remain as defined in [taxonomy.md](taxonomy.md). See the
[Core rule reference](../core-rules.md) for source examples and limitations.

## API shape

Every category follows Naming's callback → selector → requirement → modifier
shape. Category methods accept a callback over a category builder and return the
containing `ConfigBuilder`, so categories can be chained. Selectors return typed rule chains;
selecting a target alone adds no rule. Only supported requirements are exposed
on each selector's return type.

```csharp
#:package SharpLint.Configuration@1.0.0-dev

using SharpLint.Configuration;

Lint.Configure(config => config
    .Syntax(s =>
    {
        s.GotoStatements().Forbid().AsError();
        s.Namespaces().RequireFileScoped();
    })
    .Organization(o => o.Files()
        .RequireOneType().AsWarning()
        .RequireMatchingTypeName().AsError())
    .Design(d =>
    {
        d.Parameters().RequireMaximumCount(5);
        d.TypeMembers().RequireMaximumCount(20);
    })
    .Dependencies(d =>
    {
        d.Usings().ForbidNamespaces("System.IO");
        d.Packages().ForbidNames("Unsafe.Package");
    })
    .Architecture(a =>
    {
        a.Namespaces("Domain").ForbidReferencesTo("Infrastructure");
        a.Inheritance().ForbidTypes("System.Exception");
    })
    .Reliability(r =>
    {
        r.CatchBlocks().ForbidEmpty();
        r.Rethrows().RequirePreservedStackTrace();
    })
    .Performance(p =>
    {
        p.Arrays().ForbidEmptyAllocation();
        p.Conversions().ForbidExplicitBoxing();
    })
    .Project(p =>
    {
        p.Nullable().RequireEnabled();
        p.TargetFrameworks().RequireAllowed("net10.0");
    }))
    .Apply();
```

## Selector and requirement mapping

The builder types live in `SharpLint.Configuration.Dsl`. Configuration authors
only need `using SharpLint.Configuration` to use these callbacks.

| Category callback | Selector and requirement | Emitted rule ID | Options |
| --- | --- | --- | --- |
| `Syntax` | `s.GotoStatements().Forbid()` | `syntax.no-goto` | `{}` |
| `Syntax` | `s.Namespaces().RequireFileScoped()` | `syntax.file-scoped-namespaces` | `{}` |
| `Organization` | `o.Files().RequireOneType()` | `organization.one-type-per-file` | `{}` |
| `Organization` | `o.Files().RequireMatchingTypeName()` | `organization.match-type-file-name` | `{}` |
| `Design` | `d.Parameters().RequireMaximumCount(int maximum = 5)` | `design.maximum-parameters` | `maximum` |
| `Design` | `d.TypeMembers().RequireMaximumCount(int maximum = 20)` | `design.maximum-type-members` | `maximum` |
| `Dependencies` | `d.Usings().ForbidNamespaces(params string[] namespaces)` | `dependencies.forbidden-usings` | `namespaces` |
| `Dependencies` | `d.Packages().ForbidNames(params string[] packages)` | `dependencies.forbidden-packages` | `packages` |
| `Architecture` | `a.Namespaces(string from).ForbidReferencesTo(params string[] namespaces)` | `architecture.namespace-boundaries` | `from`, `to` |
| `Architecture` | `a.Inheritance().ForbidTypes(params string[] types)` | `architecture.forbidden-inheritance` | `types` |
| `Reliability` | `r.CatchBlocks().ForbidEmpty()` | `reliability.empty-catch` | `{}` |
| `Reliability` | `r.Rethrows().RequirePreservedStackTrace()` | `reliability.rethrow-caught-exception` | `{}` |
| `Performance` | `p.Arrays().ForbidEmptyAllocation()` | `performance.empty-array-allocation` | `{}` |
| `Performance` | `p.Conversions().ForbidExplicitBoxing()` | `performance.explicit-boxing` | `{}` |
| `Project` | `p.Nullable().RequireEnabled()` | `project.nullable-enabled` | `{}` |
| `Project` | `p.TargetFrameworks().RequireAllowed(params string[] allowed)` | `project.target-frameworks` | `allowed` |

Entries containing parameter types describe method signatures; omit the types
at call sites, as in the executable example above.

## Modifiers and chaining

Each requirement adds a warning by default. `.AsError()`, `.AsWarning()`,
`.AsInfo()`, and `.Disabled()` return the same typed chain and modify only its
most recent requirement. Earlier requirements retain their severity. A retained
chain still targets its own most recent requirement after other chains add rules.
Calling a modifier before any requirement throws `InvalidOperationException`.

Callbacks can be repeated to accumulate distinct policies. Unlike Naming's
selector precedence, fixed policy IDs remain unique: configuring the same ID
twice throws `ArgumentException`, including when mixing fluent, `Module`, and
generic `Rule` APIs. Put alternative namespaces, package IDs, types, or frameworks
in a single call. No general attribute exemption or predicate-filter API is
introduced for these policies.

## Validation and serialization

- Counts must be positive integers; zero and negative values throw
  `ArgumentOutOfRangeException` when the requirement is declared.
- List arguments must be non-null and contain at least one nonblank string.
  The source namespace passed to `Architecture.Namespaces` must be nonblank.
  Invalid values throw `ArgumentException` or its `ArgumentNullException`
  subtype during configuration. No values are trimmed or normalized.
- Null category callbacks throw `ArgumentNullException`.
- Fluent validation happens before a later `.Disabled()` modifier can apply.
- Options are serialized and snapshotted when each requirement is declared;
  later mutation of an input array cannot change the configuration. Build-time
  snapshots likewise remain independent of later builder or modifier calls.

The builders produce the same `ConfiguredRule` records used by `Module` and
`Rule`, with existing IDs, JSON option names, default severity, diagnostic
categories, and module identities. They introduce no new wire fields, registered
analyzers, or implicit rules. The generic APIs remain supported, including for
[external modules](taxonomy.md#external-modules-use-roslyn-analyzers).
