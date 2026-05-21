# SharpLint Configuration DSL — Roadmap & Design Notes

> **This document is the planning and roadmap doc**, not the official
> specification. Adopted features have graduated to per-category specs
> under [`docs/spec/`](docs/spec/):
>
> | Category | Status | Document |
> | --- | --- | --- |
> | Naming | Draft v1 (adopted) | [docs/spec/naming.md](docs/spec/naming.md) |
> | Everything else | Roadmap (this file) | — |
>
> Use this file to explore the full surface and trace the rationale for
> design decisions. Use [`docs/spec/`](docs/spec/) when you need to know
> what's actually committed for v1.
>
> Status: **design draft.** Nothing here is implemented yet. The current
> codebase ships a minimal sliver of this surface (`config.Async`,
> `config.Architecture`); this spec is the target it grows toward.

---

## 1. Goals

The DSL is the primary user-facing surface of SharpLint. It must be:

1. **Fully typed.** No magic strings in the API surface. Anything SharpLint
   defines (presets, rule IDs, conventions, severities, accessibilities,
   pattern kinds) is an `enum` or a typed catalog. The compiler — not
   runtime — catches typos.
2. **Discoverable.** Every rule is reachable via autocomplete from
   `config.`. Categories and sub-builders surface their full grammar.
3. **Intent-expressing.** `config.Naming(n => n.Interfaces().RequirePrefix("I"))`,
   not `config.Enable("SL2007")`.
4. **Layered.** Categories (`Naming`, `Style`, `Async`, `Architecture`,
   ecosystem packs) own their own mini-grammar; a small set of shared
   primitives (severity, selectors, patterns, exemptions) compose across all
   of them.
5. **Predictable.** Order of calls never matters for correctness. Last write
   wins for severity overrides; selectors are additive.

The DSL is **category-first**: each category exposes typed selectors scoped
to the kinds of symbols it actually cares about.

### 1.1 What "fully typed" means in practice

Some text is genuinely user data — the literal characters of a prefix the
user wants to enforce, a regex they author, a file glob, a layer name they
invented. These cannot be enum'd by SharpLint because SharpLint doesn't
know them in advance.

For all such user data, the DSL exposes **typed factory primitives**:

- `NamePattern.StartsWith("I")` — not `.StartsWith("I")` taking a raw string parameter on every method.
- `PathPattern.Glob("**/*.Generated.cs")` — not `paths.Glob("...")` as an overload.
- `NamespacePattern.Equals("System.Console")` — typed wrapper.
- User-defined `enum Layer { Domain, Application, ... }` — passed as a generic type parameter to architecture rules.
- `Rules.Async.CancellationTokenRequired` — typed catalog members in place of `"async.cancellation-token-required"`.

The principle: **strings only appear inside typed factory calls and only
when they represent genuine user data.** The DSL itself never asks the user
to type a string that SharpLint already knows about.

---

## 2. Top-level shape

```csharp
#:package SharpLint.Configuration@1.0.0

using SharpLint.Configuration;

public enum Layer { Domain, Application, Infrastructure, Web }

Lint.Configure(config =>
{
    // 1. Inherit a single base preset (enum-keyed).
    config.UsePreset(Preset.Recommended);

    // 2. Categories — typed, discoverable, IntelliSense-friendly.
    config.Naming(n => { /* … */ });
    config.Style(s => { /* … */ });
    config.Async(a => { /* … */ });
    config.Architecture<Layer>(arch => { /* user-defined layer enum */ });
    config.DependencyInjection(di => { /* … */ });
    config.Testing(t => { /* … */ });

    // 3. Ecosystem packs — built-in, first-class, no extra deps.
    config.EntityFrameworkCore(ef => { /* … */ });
    config.AspNetCore(api => { /* … */ });
    config.Xunit(x => { /* … */ });

    // 4. Global filters / exemptions (typed pattern factories).
    config.Ignore(paths =>
    {
        paths.Add(PathPattern.Glob("**/*.Generated.cs"));
        paths.Add(PathPattern.Directory("**/Migrations"));
    });

    // 5. Escape hatches — typed rule catalog, not raw IDs.
    config.Rule(Rules.Async.CancellationTokenRequired).AsError();
    config.Custom(symbols => { /* arbitrary predicates */ });
}).Apply();
```

### Top-level surface (`ConfigBuilder`)

| Member | Purpose |
| --- | --- |
| `UsePreset(Preset preset)` | Inherit a single base preset. Enum-typed. |
| `Naming(Action<NamingRulesBuilder>)` | Identifier/format rules. |
| `Style(Action<StyleRulesBuilder>)` | Shape rules (parameter count, primary ctors, expression-bodied, etc.). |
| `Async(Action<AsyncRulesBuilder>)` | Async-correctness rules. |
| `Architecture<TLayer>(Action<ArchitectureRulesBuilder<TLayer>>)` | Layer/module boundaries. `TLayer` is a user-defined enum. |
| `DependencyInjection(Action<DiRulesBuilder>)` | DI misuse (service locator, captive deps). |
| `Testing(Action<TestingRulesBuilder>)` | Test-project-only rules. |
| `EntityFrameworkCore(Action<EfCoreRulesBuilder>)` | EF Core pack. |
| `AspNetCore(Action<AspNetCoreRulesBuilder>)` | ASP.NET Core pack. |
| `Xunit(Action<XunitRulesBuilder>)` | xUnit pack. |
| `Ignore(Action<PathFilterBuilder>)` | Global path filters. |
| `Rule(RuleId id)` | Typed-catalog escape hatch for a single rule. |
| `Custom(Action<SymbolQueryBuilder>)` | Power-user predicates. |

All categories return `ConfigBuilder` so calls remain chainable. All
sub-builders return themselves so calls within a category chain.

---

## 3. Core enums and typed primitives

This section catalogs every type the DSL exposes as a building block.
Everything else is built from these.

### 3.1 `Severity`

```csharp
public enum Severity { Hidden, Info, Warning, Error }
```

- `Hidden` — disables the rule entirely.
- `Info` — surfaces in CLI/JSON but never fails CI.
- `Warning` — default for style/naming. Fails CI if `--max-warnings 0`.
- `Error` — fails CI unconditionally.

Every rule expression terminates with one of the uniform severity modifiers:

```csharp
.AsError()
.AsWarning()
.AsInfo()
.Disabled()        // shorthand for severity = Hidden
```

These are the **only** way to express severity in user code. There is no
`SetSeverity("rule.id", "error")` in the discoverable path.

### 3.2 `Preset`

```csharp
public enum Preset
{
    Recommended,
    Strict,
    CleanArchitecture,
    AspNet,
    LibraryAuthor,
    AiAgent
}
```

| Preset | Intent |
| --- | --- |
| `Recommended` | Sane defaults. Most rules at `Warning`. |
| `Strict` | Same rules at `Error`, plus stricter style. |
| `CleanArchitecture` | Layered architecture rules turned on. |
| `AspNet` | API/controller defaults. |
| `LibraryAuthor` | Tighter API hygiene; `ConfigureAwait(false)`, no public mutable state. |
| `AiAgent` | Optimized for AI iteration — every diagnostic carries `suggestion` text. |

### 3.3 `NamingConvention`

```csharp
public enum NamingConvention
{
    PascalCase,       // FooBar
    CamelCase,        // fooBar
    SnakeCase,        // foo_bar
    UpperSnakeCase,   // FOO_BAR
    KebabCase,        // foo-bar  (file/namespace rules only)
    UpperCase,        // FOOBAR
    LowerCase         // foobar
}
```

### 3.4 `Accessibility`

```csharp
[Flags]
public enum Accessibility
{
    None              = 0,
    Public            = 1 << 0,
    Internal          = 1 << 1,
    ProtectedInternal = 1 << 2,
    Protected         = 1 << 3,
    PrivateProtected  = 1 << 4,
    Private           = 1 << 5,

    Any               = ~0,
    AnyExternal       = Public | ProtectedInternal | Protected,
    AnyInternal       = Internal | PrivateProtected | Private
}
```

Selectors expose both single-flag helpers (`m.Public()`, `m.Private()`) and
a flag-set form (`m.With(Accessibility.Public | Accessibility.Internal)`).

### 3.5 `NamePattern` (typed user-text wrapper)

`NamePattern` replaces every place a user would otherwise pass a raw
identifier string. Every constructor is a static factory on `NamePattern`,
so users get autocomplete on **kind of match** before typing the literal.

```csharp
public abstract record NamePattern
{
    public static NamePattern Convention(NamingConvention c);
    public static NamePattern Equals(string text);
    public static NamePattern StartsWith(string prefix);
    public static NamePattern EndsWith(string suffix);
    public static NamePattern Contains(string substring);
    public static NamePattern OneOf(params string[] names);

    // Sugar — equivalent to Any(StartsWith(x), StartsWith(y), …)
    public static NamePattern StartsWithAny(params string[] prefixes);
    public static NamePattern EndsWithAny(params string[] suffixes);
    public static NamePattern ContainsAny(params string[] substrings);

    public static NamePattern Regex(string pattern);          // first-class, anchored ^…$ if absent
    public static NamePattern Regex(Regex pattern);           // pre-compiled overload

    // Combinators
    public static NamePattern All(params NamePattern[] patterns);
    public static NamePattern Any(params NamePattern[] patterns);
    public static NamePattern Not(NamePattern pattern);
}
```

Composition examples:

```csharp
// "starts with _ AND camelCase body"
NamePattern.All(
    NamePattern.StartsWith("_"),
    NamePattern.Convention(NamingConvention.CamelCase))

// "matches one of a small set"
NamePattern.OneOf("Equals", "GetHashCode", "ToString")

// "PascalCase but not these forbidden words"
NamePattern.All(
    NamePattern.Convention(NamingConvention.PascalCase),
    NamePattern.Not(NamePattern.OneOf("Manager", "Helper", "Util")))
```

All factory members are equally first-class. `Convention` / `StartsWith` /
`EndsWith` / `OneOf` are usually more readable for common cases;
`Regex(...)` is the right tool when the user already thinks in regex or
needs anchored character-class precision. Use whichever expresses intent
most clearly — they all compose via `All` / `Any` / `Not`.

**Most users never type `NamePattern.X` directly.** The Naming selectors
expose terse shorthand verbs (`RequireConvention`, `RequirePrefix`,
`RequireSuffix`, `ForbidSuffix`, …) that cover ~90% of rules. See
[§5](#5-naming-category). `NamePattern` is the underlying grammar for
composition, custom rules, and the cases the shorthands don't cover.

### 3.6 `PathPattern`

```csharp
public abstract record PathPattern
{
    public static PathPattern Glob(string glob);              // **/*.cs
    public static PathPattern Directory(string globOrPath);
    public static PathPattern Extension(FileExtension ext);
    public static PathPattern Filename(NamePattern name);
}

public enum FileExtension { Cs, Csx, Razor, Cshtml, Aspx }
```

### 3.7 `NamespacePattern`

```csharp
public abstract record NamespacePattern
{
    public static NamespacePattern Equals(string ns);
    public static NamespacePattern StartsWith(string prefix);
    public static NamespacePattern Glob(string glob);
    public static NamespacePattern Regex(string pattern);
}
```

### 3.8 `ProjectPattern`

```csharp
public abstract record ProjectPattern
{
    public static ProjectPattern Named(string name);          // exact .csproj name
    public static ProjectPattern Glob(string glob);
    public static ProjectPattern OfType(ProjectKind kind);
}

public enum ProjectKind { Library, Executable, Test, Web, Console }
```

### 3.9 `RuleId` and the `Rules` catalog

`RuleId` is a closed-construction value type — users cannot construct one
directly. They reference members of the `Rules` static catalog.

```csharp
public readonly struct RuleId
{
    internal RuleId(string canonical);
    public override string ToString();   // returns the canonical id (used in JSON output)
}

public static class Rules
{
    public static class Async
    {
        public static RuleId SuffixRequired { get; }
        public static RuleId CancellationTokenRequired { get; }
        public static RuleId BlockingCallsForbidden { get; }
        public static RuleId AsyncVoidForbidden { get; }
        public static RuleId ConfigureAwaitRequired { get; }
    }

    public static class Naming
    {
        public static class Method
        {
            public static RuleId Public { get; }
            public static RuleId Private { get; }
            public static RuleId Internal { get; }
            public static RuleId Async { get; }
            public static RuleId Test { get; }
        }
        public static class Class { /* ... */ }
        public static class Interface { /* ... */ }
        public static class Field { /* ... */ }
        public static class Property { /* ... */ }
        public static class Parameter { /* ... */ }
        public static class LocalVariable { /* ... */ }
        public static class Namespace { /* ... */ }
        public static class File { /* ... */ }
    }

    public static class Style
    {
        public static class Method
        {
            public static RuleId MaxParameters { get; }
            public static RuleId MaxComplexity { get; }
            public static RuleId MaxLines { get; }
        }
        public static class Constructor { /* ... */ }
        public static class Class { /* ... */ }
        public static class File { /* ... */ }
    }

    public static class Architecture
    {
        public static RuleId ForbiddenLayerReference { get; }
        public static RuleId ForbiddenNamespace { get; }
        public static RuleId LayerCycle { get; }
    }

    public static class EfCore { /* ... */ }
    public static class AspNetCore { /* ... */ }
    public static class Xunit { /* ... */ }
    public static class Di { /* ... */ }
}
```

**The wire-format id (`async.cancellation-token-required`) is still emitted
to JSON/SARIF output** — that's the diagnostic contract for AI agents and
CI tooling. But user code never types it; the catalog is the only typed
entry point.

### 3.10 Other small enums

```csharp
public enum BuiltInTypeGroup
{
    Primitives, Numerics, Strings, Collections, Tasks, Spans
}

public enum ConfigureAwaitValue { False, True }

public enum CtorStyle { Primary, Explicit }

public enum NullabilityRequirement { Annotated, Required, Optional }
```

---

## 4. Selectors

Selectors are how a rule narrows from "all things" to "things this rule
applies to". They are scoped per category and **entirely method-typed** —
no string predicates.

### 4.1 Shared selector verbs

| Verb | Type |
| --- | --- |
| `.InProject(ProjectPattern)` | Restrict to matching project. |
| `.InNamespace(NamespacePattern)` | Restrict to matching namespace. |
| `.InPath(PathPattern)` | Restrict to matching file path. |
| `.WithAttribute<T>()` | Restrict to symbols carrying attribute `T` (generic). |
| `.With(Accessibility flags)` | Flag-set accessibility match. |
| `.Except(Func<TSymbol, bool>)` | Carve out an exception (typed predicate). |

### 4.2 Method selector

```csharp
n.Methods(m => m
    .Public()                              // accessibility helpers
    .Async()
    .Static()
    .Override()
    .Returns<Task>()                       // typed return-type
    .InClass(NamePattern.EndsWith("Service")))
```

Accessibility predicates AND together. To express OR, use the flag-set
form: `m.With(Accessibility.Public | Accessibility.Internal)`.

### 4.3 Other selectors

- `Classes()` — `Sealed()`, `Abstract()`, `Record()`, `Static()`, `Partial()`.
- `Interfaces()` — `Generic()`, `Marker()` (no members).
- `Structs()`, `Records()`, `Enums()`.
- `Constructors()` — `Primary()`, `Parameterless()`.
- `Fields()` — `Private()`, `Static()`, `Const()`, `Readonly()`.
- `Properties()` — `Auto()`, `ExpressionBodied()`, `Init()`.
- `Parameters()` — for parameter-level rules.
- `Namespaces()`, `Files()`.

Every selector is a typed builder. None take raw strings for predicates.

---

## 5. Naming category

Answers: **what are identifiers allowed to look like?**

```csharp
config.Naming(n =>
{
    // Positive enforcement — "must be named like this".
    n.Methods(m => m.Public()).RequireConvention(NamingConvention.PascalCase).AsError();
    n.Methods(m => m.Private()).RequireRegex("^_?[a-z][A-Za-z0-9]*$").AsWarning();
    n.Methods(m => m.Async()).RequireSuffix("Async");
    n.Methods(m => m.Returns<Task>()).RequireSuffix("Async");
    n.Methods(m => m.WithAttribute<TestAttribute>()).RequirePrefix("Should_");

    // Negative enforcement — "must NOT be named like this".
    n.Methods(m => m.Public()).ForbidName("DoStuff", "Process", "Handle");
    n.Classes().ForbidSuffix("Manager", "Helper", "Util");
    n.Interfaces().ForbidPrefix("Abstract");        // interfaces aren't abstract classes

    // Classes / interfaces / records / enums
    n.Classes().RequireConvention(NamingConvention.PascalCase);
    n.Classes(c => c.Abstract()).RequirePrefix("Abstract").AsInfo();
    n.Classes(c => c.WithAttribute<AttributeUsageAttribute>()).RequireSuffix("Attribute");
    n.Interfaces()
        .RequirePrefix("I")
        .RequireConvention(NamingConvention.PascalCase);    // chain — both must hold
    n.Records().RequireConvention(NamingConvention.PascalCase);
    n.Enums().RequireConvention(NamingConvention.PascalCase);
    n.Enums(e => e.Members()).RequireConvention(NamingConvention.PascalCase);

    // Fields / properties / locals / parameters
    n.Fields(f => f.Private())
        .RequirePrefix("_")
        .RequireConvention(NamingConvention.CamelCase);
    n.Fields(f => f.Const()).RequireConvention(NamingConvention.UpperSnakeCase);
    n.Properties().RequireConvention(NamingConvention.PascalCase);
    n.Parameters().RequireConvention(NamingConvention.CamelCase);
    n.LocalVariables().RequireConvention(NamingConvention.CamelCase);

    // Namespaces & files
    n.Namespaces().RequirePrefix("Acme.");
    n.Files()
        .RequireConvention(NamingConvention.PascalCase)
        .RequireExtension(FileExtension.Cs);
    n.Files().MustMatchType();              // FooService.cs declares FooService

    // Length constraints
    n.Methods().MinLength(3).MaxLength(60);

    // Acronym handling
    n.WithAcronyms("IO", "URL", "API");

    // Full NamePattern composition — rare, for cases the shorthands don't cover.
    n.Methods(m => m.Public())
        .Require(NamePattern.Any(
            NamePattern.EndsWith("Async"),
            NamePattern.OneOf("Dispose", "DisposeAsync")));
});
```

### 5.0 Shorthand verbs (the common path)

Every Naming selector exposes a small, typed verb set that covers
~90% of real rules without ever typing `NamePattern.X`:

| Positive | Negative | Meaning |
| --- | --- | --- |
| `RequireConvention(NamingConvention)` | `ForbidConvention(NamingConvention)` | Name must (not) follow a convention. |
| `RequirePrefix(params string[])` | `ForbidPrefix(params string[])` | Name must (not) start with any of these. |
| `RequireSuffix(params string[])` | `ForbidSuffix(params string[])` | Name must (not) end with any of these. |
| `RequireContains(params string[])` | `ForbidContains(params string[])` | Name must (not) contain any of these. |
| `RequireName(params string[])` | `ForbidName(params string[])` | Name must (not) equal any of these exactly. |
| `RequireRegex(string)` | `ForbidRegex(string)` | Name must (not) match this regex. |
| `Require(NamePattern)` | `Forbid(NamePattern)` | General form — any composed `NamePattern`. |

`params string[]` overloads mean a single value works without ceremony:

```csharp
n.Interfaces().RequirePrefix("I");                    // one
n.Classes().ForbidSuffix("Manager", "Helper", "Util"); // many
```

Multiple shorthand calls on the same selector AND together:

```csharp
n.Fields(f => f.Private())
    .RequirePrefix("_")
    .RequireConvention(NamingConvention.CamelCase);
// equivalent to:
n.Fields(f => f.Private())
    .Require(NamePattern.All(
        NamePattern.StartsWith("_"),
        NamePattern.Convention(NamingConvention.CamelCase)));
```

The chained form is the recommended style — `NamePattern.All(...)` is
only needed when an AND lives inside an `Any` / `Not` combinator.

### 5.1 Require vs. Forbid

Every naming rule expresses one of two intents, and the verb prefix makes
the intent unambiguous. For each shorthand there is a `Require…` /
`Forbid…` pair; for the full-grammar form there is `Require(NamePattern)`
/ `Forbid(NamePattern)`. Pick the verb that reads most naturally:

```csharp
// "Public methods must be PascalCase"
n.Methods(m => m.Public()).RequireConvention(NamingConvention.PascalCase);

// "Public methods cannot be named Handle/Process/DoStuff"
n.Methods(m => m.Public()).ForbidName("Handle", "Process", "DoStuff");

// "Classes cannot end in Manager/Helper/Util"
n.Classes().ForbidSuffix("Manager", "Helper", "Util");
```

The same rule can be expressed either way via `NamePattern.Not(...)`, but
the `Forbid…` shorthand always reads better than `Require(Not(...))`:

```csharp
// Same meaning — prefer the first form.
n.Classes().ForbidSuffix("Manager");
n.Classes().Require(NamePattern.Not(NamePattern.EndsWith("Manager")));
```

Multiple `Require…` / `Forbid…` calls on the same selector AND together —
a symbol must satisfy every `Require…` and zero `Forbid…`s to pass.

`MinLength` / `MaxLength` / `RequireExtension` / `MustMatchType` /
`WithAcronyms` are separate constraints, not name-pattern matches. They
are positive by nature (`MinLength` means "must be at least"); no
`Forbid` variant is needed.

### 5.2 Symmetry with other categories

Every category in the DSL uses an explicit positive/negative verb pair —
naming is no longer the odd one out:

| Category | Positive verb | Negative verb |
| --- | --- | --- |
| Naming | `Require(NamePattern)` | `Forbid(NamePattern)` |
| Style | `Require(...)` / `Prefer(...)` / `Max...` / `Min...` | `Disallow()` / `Forbid...()` |
| Async | `Require...()` | `Forbid...()` |
| Architecture | `CanReference` / `CanOnlyReference` | `CannotReference` / `ForbidReference<T>` / `ForbidNamespace` |
| DI | `Require...()` | `Disallow...()` / `Forbid...()` |

If a category exposes only a negative verb (e.g. `Async.ForbidBlockingCalls()`),
that's because the inverse ("require blocking calls") is never sensible.
When both directions are sensible, both verbs exist.

---

## 6. Style category

Shape rules — things that aren't *what something is named*, but *how it's
structured*. Already fully method-typed; no strings to replace.

```csharp
config.Style(s =>
{
    // Method shape
    s.Methods.MaxParameters(4).AsWarning();
    s.Methods(m => m.Public()).MaxParameters(6);
    s.Methods.MaxCyclomaticComplexity(10);
    s.Methods.MaxLines(60);
    s.Methods.ForbidOut().AllowOutIn(OutContext.TryPatterns);
    s.Methods.ForbidRef();
    s.Methods.RequireExpressionBody().When(m => m.LineCount <= 1);

    // Constructors
    s.Constructors.Require(CtorStyle.Primary).When(c => c.IsRecord);
    s.Constructors.Require(CtorStyle.Primary).When(c => c.IsClass && c.IsSealed);
    s.Constructors.Require(CtorStyle.Explicit).When(c => c.IsClass && !c.IsSealed);
    s.Constructors.MaxParameters(5);

    // Classes
    s.Classes.RequireSealed().Except(c => c.IsAbstract);
    s.Classes.MaxLines(400);
    s.Classes.MaxPublicMembers(20);
    s.Classes.ForbidStaticMutableState();

    // Fields / properties
    s.Fields(f => f.Public()).Disallow();
    s.Fields(f => f.Static().Mutable()).Disallow();
    s.Properties.PreferInit().When(p => p.IsRecord);

    // Files
    s.Files.OneTopLevelTypePerFile();
    s.Files.PreferFileScopedNamespaces();
    s.Files.MaxLines(800);

    // Usings / imports
    s.Usings.RequireSorted();
    s.Usings.ForbidGlobalUsingOutside(PathPattern.Filename(NamePattern.Equals("GlobalUsings.cs")));

    // Modern C# preferences (enum-keyed)
    s.Prefer(LanguageFeature.TargetTypedNew);
    s.Prefer(LanguageFeature.PatternMatching);
    s.Prefer(LanguageFeature.CollectionExpressions);
    s.ForbidVarFor(BuiltInTypeGroup.Primitives);
});
```

Where the previous draft had `s.PreferTargetTypedNew()`, the typed version
uses an enum:

```csharp
public enum LanguageFeature
{
    TargetTypedNew,
    PatternMatching,
    CollectionExpressions,
    NullCoalescingAssignment,
    SwitchExpressions,
    PrimaryConstructors,
    FileScopedNamespaces
}
```

This collapses N nearly-identical methods into one typed verb. New
features only require an enum addition, not a new API.

---

## 7. Async category

```csharp
config.Async(a =>
{
    a.RequireAsyncSuffix();
    a.RequireCancellationTokens();
    a.RequireCancellationTokens().Except(m => m.IsEventHandler());
    a.ForbidAsyncVoid().Except(m => m.IsEventHandler());
    a.ForbidBlockingCalls();
    a.ForbidTaskRun().In(ProjectPattern.OfType(ProjectKind.Library));
    a.RequireConfigureAwait(ConfigureAwaitValue.False)
        .In(ProjectPattern.OfType(ProjectKind.Library));
    a.PreferValueTask().When(m => m.IsHotPath);
});
```

---

## 8. Architecture category — user-defined enum layers

The single biggest source of magic strings in the previous draft was
`arch.Layer("Domain")`. Replaced with **user-defined enums passed as a
generic type parameter**.

```csharp
public enum Layer { Domain, Application, Infrastructure, Web }

config.Architecture<Layer>(arch =>
{
    // Define each layer's membership.
    arch.Define(Layer.Domain)
        .Includes(NamespacePattern.StartsWith("Acme.Domain"))
        .Includes(ProjectPattern.Named("Acme.Domain"));

    arch.Define(Layer.Application).Includes(NamespacePattern.StartsWith("Acme.Application"));
    arch.Define(Layer.Infrastructure).Includes(NamespacePattern.StartsWith("Acme.Infrastructure"));
    arch.Define(Layer.Web).Includes(NamespacePattern.StartsWith("Acme.Web"));

    // Layer rules — all keys are enum values, fully typed.
    arch.Layer(Layer.Domain)
        .CannotReference(Layer.Infrastructure)
        .CannotReference(Layer.Web)
        .CanOnlyReference(Layer.Domain);

    arch.Layer(Layer.Application)
        .CannotReference(Layer.Web)
        .CanReference(Layer.Domain);

    // Type-level forbidden references (generic, not stringly-typed).
    arch.ForbidReference<HttpClient>().OutsideLayer(Layer.Infrastructure);
    arch.ForbidReference<System.Console>()
        .Except(file => file.NameEquals("Program.cs"));
    arch.ForbidNamespace(NamespacePattern.StartsWith("Newtonsoft.Json"));

    // Cycles (varargs of enum values).
    arch.NoCyclesBetween(Layer.Domain, Layer.Application, Layer.Infrastructure);
});
```

Benefits of the enum approach:

1. **Compiler-checked layer names.** Typos surface at build time.
2. **Refactor-safe.** Rename `Domain` → `Core` in one place; every rule
   updates.
3. **Discoverable.** `Layer.<Tab>` enumerates the available layers.
4. **No string namespace pollution.** Two repos with their own
   `enum Layer` don't collide.

For users who genuinely have an open-ended layer set (rare), the
non-generic `Architecture(...)` overload still exists and accepts
`NamespacePattern` directly — but the generic-enum form is the documented
path.

---

## 9. Dependency injection category

```csharp
config.DependencyInjection(di =>
{
    di.DisallowServiceLocator();
    di.DisallowCaptiveDependencies();
    di.RequireConstructorInjection();
    di.ForbidNewingInjectables();
});
```

---

## 10. Testing category

```csharp
config.Testing(t =>
{
    t.RequireDeterministicAsserts();
    t.ForbidThreadSleep();
    t.RequireArrangeActAssertComments().AsInfo();
    t.ForbidTestsThatNeverAssert();
    t.Methods().RequirePrefix("Should_");
});
```

Test-project detection is automatic via standard signals (`IsTestProject`
MSBuild property, references to xunit/nunit/MSTest, `*Tests.csproj` filename).

---

## 11. Ecosystem packs

Built-in, first-class typed builders. Initial packs:

- `EntityFrameworkCore`
- `AspNetCore`
- `Xunit`

Every method on every pack is enum/method-typed — no string IDs leak through.

### 11.1 Entity Framework Core

```csharp
config.EntityFrameworkCore(ef =>
{
    ef.DbContext.RequirePooled();
    ef.DbContext.ForbidStaticAccess();
    ef.DbContext.RequireLifetime(ServiceLifetime.Scoped);

    ef.Queries.ForbidLazyLoading();
    ef.Queries.RequireAsNoTrackingForReads();
    ef.Queries.ForbidClientEvaluation();
    ef.Queries.ForbidSelectStar();
    ef.Queries.ForbidNPlusOne();
    ef.Queries.RequireCancellationToken();
    ef.Queries.ForbidSaveChangesInLoop();

    ef.Migrations.RequireExplicitColumnTypes();
    ef.Migrations.RequireOnDeleteBehavior();
    ef.Entities.RequireKeyDeclared();
    ef.Entities.ForbidPublicSetters().Except(e => e.IsOwned);
});
```

`ServiceLifetime` is the standard
`Microsoft.Extensions.DependencyInjection.ServiceLifetime` enum, so users
already know it.

### 11.2 ASP.NET Core

```csharp
config.AspNetCore(api =>
{
    api.Controllers.RequireApiControllerAttribute();
    api.Controllers.RequireRouteAttribute();
    api.Controllers.ForbidSyncActions();
    api.Controllers.RequireProducesResponseType();

    api.Endpoints.ForbidSyncOverAsync();
    api.Endpoints.RequireCancellationToken();
    api.Endpoints.RequireAuthentication(AuthorizationDefault.Required);

    api.MinimalApi.RequireTypedResults();
    api.MinimalApi.GroupByFeature();

    api.Configuration.ForbidIConfigurationInjection();
    api.Configuration.RequireOptionsPattern();
});
```

```csharp
public enum AuthorizationDefault { Anonymous, Required }
```

### 11.3 xUnit

```csharp
config.Xunit(x =>
{
    x.RequireFactOrTheory();
    x.ForbidTestsWithoutAssertions();
    x.ForbidSharedMutableState();
    x.PreferTheoryOverParameterizedFact();
    x.RequireDisplayName().AsInfo();
});
```

---

## 12. Filters, exemptions, and path scoping

### 12.1 Global

```csharp
config.Ignore(p =>
{
    p.Add(PathPattern.Glob("**/*.Generated.cs"));
    p.Add(PathPattern.Directory("**/Migrations"));
    p.Add(ProjectPattern.Named("Acme.LegacyShim"));
});
```

### 12.2 Per-rule

Every rule modifier supports the universal escape:

```csharp
n.Classes().RequireConvention(NamingConvention.PascalCase)
    .Except(c => c.HasAttribute<GeneratedCodeAttribute>())
    .IgnoredIn(PathPattern.Glob("**/*.Designer.cs"));
```

### 12.3 Inline source suppression

```csharp
// sharplint-disable-next-line async.cancellation-token-required
public async Task FooAsync() { }

// sharplint-disable async.cancellation-token-required
public async Task FooAsync() { }
public async Task BarAsync() { }
// sharplint-enable async.cancellation-token-required
```

Comment-based suppression uses the canonical wire-format rule ID — the
same string that appears in JSON output. **In C# config code, the user
never types these.** The comment-form ID is documented per rule in the
generated rule catalog page.

---

## 13. Severity precedence

```
preset defaults  <  category builder  <  config.Rule(RuleId)  <  inline suppression
```

User overrides applied after `UsePreset(...)` always win.

---

## 14. Escape hatches

### 14.1 By typed rule catalog

```csharp
config.Rule(Rules.Async.CancellationTokenRequired).AsError();
config.Rule(Rules.Style.Method.MaxParameters).Disabled();
```

No string IDs. `RuleId` cannot be constructed outside SharpLint.

### 14.2 Custom predicates (power user)

```csharp
config.Custom(symbols =>
{
    symbols.Methods()
        .Where(m => m.HasAttribute<ObsoleteAttribute>())
        .Where(m => m.IsPublic)
        .Report(
            id: CustomRuleId.Of("custom.public-obsolete"),
            severity: Severity.Warning,
            message: "Public obsolete API. Plan for removal.",
            suggestion: "Remove the API or downgrade visibility.");
});
```

`CustomRuleId.Of(...)` is the **only** place in the DSL where a user
authors a rule ID string — and they're declaring a new rule, not
referencing an existing one. SharpLint enforces an `^[a-z][a-z0-9.-]*$`
shape and a reserved-prefix list (`naming.`, `style.`, `async.`,
`architecture.`, `efcore.`, `aspnetcore.`, `xunit.`, `di.`,
`testing.`) so users can't shadow built-in rules.

Predicates run against SharpLint's stable typed symbol model — **not**
raw Roslyn `SyntaxNode`. This is a deliberate boundary: if a user needs
raw Roslyn, they should write a Roslyn analyzer, not a SharpLint config.

---

## 15. Diagnostic schema (wire format)

The on-disk / over-the-wire format keeps rule IDs as strings — that's
the cross-language contract for AI agents, CI tools, and SARIF consumers.
The typed C# DSL is the user-facing surface; JSON is the machine surface.

```json
{
  "ruleId": "style.method.max-parameters",
  "category": "style",
  "severity": "warning",
  "message": "Method 'Foo' has 7 parameters (max 4).",
  "file": "src/Foo.cs",
  "line": 42,
  "column": 17,
  "endLine": 42,
  "endColumn": 20,
  "suggestion": "Group related parameters into a record or options object.",
  "fixAvailable": false,
  "tags": ["readability"]
}
```

Rule IDs follow `<category>.<subgroup>.<name>` and are stable. Renames
go through a deprecation cycle (old id emitted alongside new id for one
minor version).

---

## 16. Worked example (fully typed)

```csharp
#:package SharpLint.Configuration@1.0.0

using SharpLint.Configuration;

public enum Layer { Domain, Application, Infrastructure, Web }

Lint.Configure(config =>
{
    config.UsePreset(Preset.Recommended);

    config.Naming(n =>
    {
        n.Methods(m => m.Public()).RequireConvention(NamingConvention.PascalCase).AsError();
        n.Methods(m => m.Private()).RequireRegex("^_?[a-z]");
        n.Methods(m => m.Async()).RequireSuffix("Async");

        n.Interfaces()
            .RequirePrefix("I")
            .RequireConvention(NamingConvention.PascalCase);

        n.Classes(c => c.WithAttribute<AttributeUsageAttribute>()).RequireSuffix("Attribute");
        n.Classes().ForbidSuffix("Manager", "Helper", "Util");

        n.Fields(f => f.Private())
            .RequirePrefix("_")
            .RequireConvention(NamingConvention.CamelCase);

        n.Namespaces().RequirePrefix("Acme.");
    });

    config.Style(s =>
    {
        s.Methods.MaxParameters(4);
        s.Methods(m => m.Public()).MaxParameters(6);
        s.Methods.MaxCyclomaticComplexity(10);
        s.Constructors.Require(CtorStyle.Primary).When(c => c.IsRecord);
        s.Files.OneTopLevelTypePerFile();
        s.Files.PreferFileScopedNamespaces();
        s.Prefer(LanguageFeature.TargetTypedNew);
        s.Prefer(LanguageFeature.CollectionExpressions);
    });

    config.Async(a =>
    {
        a.RequireAsyncSuffix();
        a.RequireCancellationTokens().Except(m => m.IsEventHandler());
        a.ForbidBlockingCalls();
        a.ForbidAsyncVoid().Except(m => m.IsEventHandler());
    });

    config.Architecture<Layer>(arch =>
    {
        arch.Define(Layer.Domain).Includes(NamespacePattern.StartsWith("Acme.Domain"));
        arch.Define(Layer.Application).Includes(NamespacePattern.StartsWith("Acme.Application"));
        arch.Define(Layer.Infrastructure).Includes(NamespacePattern.StartsWith("Acme.Infrastructure"));
        arch.Define(Layer.Web).Includes(NamespacePattern.StartsWith("Acme.Web"));

        arch.Layer(Layer.Domain).CanOnlyReference(Layer.Domain);
        arch.Layer(Layer.Application).CannotReference(Layer.Web);

        arch.ForbidReference<System.Console>()
            .Except(file => file.NameEquals("Program.cs"));
    });

    config.EntityFrameworkCore(ef =>
    {
        ef.DbContext.RequirePooled();
        ef.Queries.ForbidLazyLoading();
        ef.Queries.RequireAsNoTrackingForReads();
        ef.Queries.RequireCancellationToken();
        ef.Migrations.RequireExplicitColumnTypes();
    });

    config.AspNetCore(api =>
    {
        api.Controllers.RequireApiControllerAttribute();
        api.Endpoints.ForbidSyncOverAsync();
        api.Configuration.RequireOptionsPattern();
    });

    config.Ignore(paths =>
    {
        paths.Add(PathPattern.Glob("**/*.Generated.cs"));
        paths.Add(PathPattern.Directory("**/Migrations"));
    });

    config.Rule(Rules.Async.CancellationTokenRequired).AsError();
}).Apply();
```

**Audit of strings appearing in user code above:**

| String | Why it's user data, not API |
| --- | --- |
| `"^_?[a-z]"` | User-authored regex (first-class via `NamePattern.Regex(...)`). |
| `"Async"` | The literal suffix the user wants enforced. |
| `"I"` | The interface prefix the user wants enforced. |
| `"Attribute"` | The class suffix the user wants enforced. |
| `"_"` | The field prefix the user wants enforced. |
| `"Acme.*"` | The user's actual namespace root. |
| `"**/*.Generated.cs"` | The user's actual file glob. |
| `"Program.cs"` | The user's actual filename. |

Zero strings name SharpLint concepts — every `Preset`, `Severity`,
`NamingConvention`, `LanguageFeature`, `RuleId`, `Layer`, accessibility,
and ecosystem rule is enum or typed-catalog.

---

## 17. Non-goals (for v1)

- Multi-preset layering (`UsePresets(a, b, c)`).
- A plugin pack SDK / public extension API.
- Raw Roslyn `SyntaxNode` access in user configs.
- Cross-file/architecture rules expressed as arbitrary graph queries
  beyond the `Layer`/`Reference`/`Namespace` primitives.
- Per-file overrides via YAML/JSON sidecars. The single
  `sharplint.config.cs` is the source of truth.
- Formatter responsibilities (`dotnet format` covers this).

---

## 18. Open design questions

1. **Predicate symbol model surface.** How rich is `MethodInfo` /
   `ClassInfo` in `.When(...)` / `.Except(...)` predicates? Proposal:
   start small (accessibility, attributes, names, simple type predicates)
   and grow with demand.
2. **Layer resolution.** When a type lives in two layer definitions
   (e.g. both `NamespacePattern` and `ProjectPattern` match), which
   wins? Proposal: project membership beats namespace; first declared
   `Includes` wins on tie.
3. **Suppression scope.** Does `// sharplint-disable` block-form suppress
   until the next `enable` only, or until end-of-file? Proposal: until
   `enable` or end-of-file, whichever comes first.
4. **`When` vs `Except` overlap.** Both express conditions. Proposal:
   `When` is the inclusion filter, `Except` is the exclusion filter; both
   may be chained.
5. **Pack default severities.** Should EF Core / ASP.NET rules ship
   `Warning` or `Hidden` by default? Proposal: `Hidden`; opt in by
   calling each rule, or by selecting an opinionated pack preset.
6. **Generic `Architecture<TLayer>` ergonomics.** Does the generic
   parameter add too much ceremony? Proposal: keep generic as the
   documented path; provide a non-generic overload with `NamespacePattern`
   keys for power users.

---

## 19. Glossary

- **Category** — a top-level builder on `ConfigBuilder` (`Naming`, `Style`,
  `Async`, `Architecture`, `DependencyInjection`, `Testing`).
- **Pack** — a built-in category specific to an ecosystem library
  (`EntityFrameworkCore`, `AspNetCore`, `Xunit`).
- **Selector** — a typed lambda that narrows a rule's scope
  (`m => m.Public().Async()`).
- **Modifier** — a chainable method that adjusts a rule
  (`.AsError()`, `.Except(...)`, `.IgnoredIn(...)`).
- **Preset** — a curated bundle of rule selections and severities,
  inherited via `UsePreset(Preset.X)`.
- **Rule catalog** — the `Rules` static class. The only typed entry point
  for referring to a specific rule by identity.
- **Typed pattern primitive** — `NamePattern`, `PathPattern`,
  `NamespacePattern`, `ProjectPattern`. Wraps unavoidable user text
  (regex/glob/literal) in a typed factory so the DSL surface stays
  string-free.
- **Rule ID** — the stable string identifier emitted to JSON/SARIF
  output. Never typed by users in C# config code; referenced via
  `Rules.X.Y` instead. Format: `<category>.<subgroup>.<name>`.
