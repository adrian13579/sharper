# Naming Rules — Specification

> **Status:** Draft v1. Adopted feature set. This document is the source of
> truth for the naming category. Features outside the scope below remain in
> the roadmap ([SPEC.md](../../SPEC.md)) and may be added in future versions.

The naming category answers a single question:

> *What are identifiers allowed to look like?*

It enforces conventions, prefixes, suffixes, exact-name allow/deny lists,
and regex matches on every named element in a C# program.

---

## 1. Scope

### 1.1 In scope (v1)

- Selectors for **methods, classes, interfaces, records, enums, properties,
  fields, parameters, local variables, namespaces**.
- Accessibility predicates (`Public`, `Internal`, `Protected`, `Private`) on
  methods, classes, interfaces, records, enums, and properties.
- Modifier predicates: `Async` (methods), `Const` (fields),
  `Static`/`Abstract`/`Sealed` (classes), `Abstract`/`Sealed` (records),
  `Static` (properties).
- Five verb pairs: `Convention`, `Prefix`, `Suffix`, `Name`, `Regex` — each
  with a `Require…` and a `Forbid…` form.
- Four naming conventions: `PascalCase`, `CamelCase`, `SnakeCase`,
  `UpperSnakeCase`.
- Severity model: `Hidden` / `Info` / `Warning` / `Error` with uniform
  modifiers `.AsError()`, `.AsWarning()`, `.AsInfo()`, `.Disabled()`.
- Chained `Require…` / `Forbid…` calls on a single selector (AND).
- Attribute-based exemption: `.ExceptWithAttribute<T>()`.

### 1.2 Out of scope (deferred to a later version)

The following ideas exist in the roadmap but are **not part of v1**.
Configs that reference them will fail to compile.

- File-name rules (`Files().RequireExtension(...)`, `MustMatchType()`).
- Struct naming and enum-member naming.
- Length constraints (`MinLength`, `MaxLength`).
- Acronym handling (`WithAcronyms("IO", "URL")`).
- The full `NamePattern` factory with `All` / `Any` / `Not` composition.
  Composition for v1 is limited to chained `Require…` / `Forbid…` calls
  (AND only).
- `RequireContains` / `ForbidContains` substring verbs.
- `KebabCase`, `UpperCase`, `LowerCase` conventions.
- `WithAttribute<T>()` as a *selector* predicate (only `ExceptWithAttribute<T>()`
  is in v1).
- Method modifiers beyond `Async`: `Static`, `Override`, `Returns<T>()`.
- Accessibility flag-sets (`Accessibility.Public | Accessibility.Internal`).
  v1 has single-accessibility selectors only.
- Predicate exemptions over a rich symbol model (`.Except(m => ...)`).

---

## 2. Vocabulary

### 2.1 `NamingConvention`

```csharp
public enum NamingConvention
{
    PascalCase,       // FooBar
    CamelCase,        // fooBar
    SnakeCase,        // foo_bar
    UpperSnakeCase    // FOO_BAR
}
```

### 2.2 `Severity`

```csharp
public enum Severity { Hidden, Info, Warning, Error }
```

| Value | CLI behavior |
| --- | --- |
| `Hidden` | Rule disabled. Not emitted. |
| `Info` | Emitted. Never fails CI. |
| `Warning` | Emitted. Fails CI when `--max-warnings 0`. |
| `Error` | Emitted. Always fails CI. |

The four uniform severity modifiers — `.AsError()`, `.AsWarning()`,
`.AsInfo()`, `.Disabled()` — terminate any naming rule expression.
They are the only way to override severity in user code.

---

## 3. Configuration entry point

All naming rules live inside the `Naming` category builder:

```csharp
Lint.Configure(config =>
{
    config.Naming(n =>
    {
        // rules go here
    });
}).Apply();
```

`config.Naming(...)` may be called multiple times in the same config; calls
accumulate. Within a selector chain, constraints compose with AND. Across separate
selectors, declaration order controls overrides as described in §5.2.

Naming belongs to the [core taxonomy](taxonomy.md), identified by
`Rules.Naming.Id` (`naming`) and provided by module `sharplint`.
`NamingRule.Definition` exposes its rule ID, category, and scope (type,
namespace, or symbol/member). This metadata does not alter selector precedence
or the existing configuration JSON. Naming diagnostics expose the category
as structured metadata; CLI JSON includes `"category": "naming"`.

### 3.1 Presets

A preset is **not** a separate execution path. It is an `Action<ConfigBuilder>`
that calls the same DSL described in this document. `UsePreset` is sugar:

```csharp
public ConfigBuilder UsePreset(Action<ConfigBuilder> preset)
{
    preset(this);
    return this;
}
```

Calling `config.UsePreset(Preset.Recommended)` is exactly equivalent to
inlining the body of `Preset.Recommended` at that point in the config. "Go to
definition" on `Preset.Recommended` lands on the DSL calls it bundles —
nothing else happens behind the scenes, and nothing is persisted on the
config besides the rules the preset adds.

This means:

- Presets and inline rules can be freely mixed; calls accumulate.
- A user can define their own `Action<ConfigBuilder>` and pass it to
  `UsePreset` — there is no special preset registry.
- Presets compose: `config.UsePreset(Preset.Recommended); config.UsePreset(MyTeamPreset);`
  simply runs both in order.

The shipped `Preset.Recommended` matches the worked example in [§9](#9-worked-example).

---

## 4. Selectors

A selector identifies the set of symbols a rule applies to. Each selector
exposes a small predicate vocabulary specific to its symbol kind.

### 4.1 Selector reference

| Selector | Predicates (v1) |
| --- | --- |
| `n.Methods(m => …)` | `Public()`, `Internal()`, `Protected()`, `Private()`, `Async()` |
| `n.Classes(c => …)` | `Public()`, `Internal()`, `Protected()`, `Private()`, `Static()`, `Abstract()`, `Sealed()` |
| `n.Interfaces(i => …)` | `Public()`, `Internal()`, `Protected()`, `Private()` |
| `n.Records(r => …)` | `Public()`, `Internal()`, `Protected()`, `Private()`, `Abstract()`, `Sealed()` |
| `n.Enums(e => …)` | `Public()`, `Internal()`, `Protected()`, `Private()` |
| `n.Properties(p => …)` | `Public()`, `Internal()`, `Protected()`, `Private()`, `Static()` |
| `n.Fields(f => …)` | `Public()`, `Private()`, `Const()` |
| `n.Parameters()` | *(no predicates in v1)* |
| `n.LocalVariables()` | *(no predicates in v1)* |
| `n.Namespaces()` | *(no predicates in v1)* |

Every selector also has a no-arg form that targets the kind regardless of
accessibility or modifiers: `n.Classes()`, `n.Properties()`, `n.Namespaces()`.

### 4.2 Predicate semantics

Multiple predicates on a single selector AND together:

```csharp
n.Methods(m => m.Public().Async())   // public AND async
```

To express OR across accessibilities, write two rules:

```csharp
n.Methods(m => m.Public()).RequireConvention(NamingConvention.PascalCase);
n.Methods(m => m.Internal()).RequireConvention(NamingConvention.PascalCase);
```

(Accessibility flag-sets are deferred — see [§1.2](#12-out-of-scope-deferred-to-a-later-version).)

### 4.3 Predicate definitions

| Predicate | Matches |
| --- | --- |
| `.Public()` | `public` accessibility |
| `.Internal()` | `internal` accessibility |
| `.Protected()` | `protected` accessibility |
| `.Private()` | `private` accessibility (and unqualified members where private is the default) |
| `.Async()` | method declared with the `async` keyword |
| `.Const()` | `const` field |
| `.Static()` | `static` class or property |
| `.Abstract()` | `abstract` class or record |
| `.Sealed()` | `sealed` class or record |

Accessibility on a type follows C#'s defaults when unqualified: top-level types
are `internal`, types nested in a class/struct/record are `private`, and types
nested in an interface are `public`.

---

## 5. Rule verbs

Every naming rule terminates a selector with a `Require…` (positive) or
`Forbid…` (negative) verb.

### 5.1 The five verb pairs

| Positive | Negative | Argument | Meaning |
| --- | --- | --- | --- |
| `RequireConvention(NamingConvention)` | `ForbidConvention(NamingConvention)` | one | Name must (not) follow the convention. |
| `RequirePrefix(params string[])` | `ForbidPrefix(params string[])` | one or more | Name must (not) start with any of these literals. |
| `RequireSuffix(params string[])` | `ForbidSuffix(params string[])` | one or more | Name must (not) end with any of these literals. |
| `RequireName(params string[])` | `ForbidName(params string[])` | one or more | Name must (not) equal any of these exactly. |
| `RequireRegex(string)` | `ForbidRegex(string)` | one | Name must (not) match this .NET regex. Anchored `^…$` if not already. |

`params string[]` overloads make the single-value case ceremony-free:

```csharp
n.Interfaces().RequirePrefix("I");
n.Classes().ForbidSuffix("Manager", "Helper", "Util");
```

`Regex` accepts a single string. To match any-of regexes, compose at the
regex level (`"^(Foo|Bar)"`).

### 5.2 AND composition via chaining

Multiple `Require…` / `Forbid…` calls on the same selector AND together —
a symbol must satisfy **every** `Require…` and **zero** `Forbid…`s to pass.
This includes repeated calls using the same verb, in either direction:

```csharp
n.Interfaces().RequirePrefix("I").ForbidPrefix("IInternal");
// Requires an I prefix AND rejects an IInternal prefix.
```

For each symbol and verb, the last applicable declaration selects the winning
selector chain. Every constraint of that verb from that chain applies. A later
separate selector replaces earlier constraints of the same verb, including preset
constraints, even if the earlier selector is more specific. Different verbs
resolve independently. A disabled constraint still participates in selecting the
winning chain, but emits no diagnostic; it does not disable active siblings in
the same chain. Severity and attribute exemptions remain per constraint.

```csharp
n.Fields(f => f.Private())
    .RequirePrefix("_")
    .RequireConvention(NamingConvention.CamelCase);
// Private fields must start with "_" AND be camelCase.
```

```csharp
n.Interfaces()
    .RequirePrefix("I")
    .RequireConvention(NamingConvention.PascalCase)
    .ForbidName("IDisposable");   // contrived, but legal
```

### 5.3 Require vs. Forbid: choose by readability

Where the same rule is expressible either way, prefer the form that reads
naturally:

```csharp
// Preferred — clearly negative intent.
n.Classes().ForbidSuffix("Manager", "Helper", "Util");

// Avoid — double-negative reading.
n.Classes().RequireRegex("^(?!.*(Manager|Helper|Util)$).*");
```

---

## 6. Severity modifiers

Every rule expression may be terminated with a severity modifier. Without
one, the rule takes its preset default (typically `Warning`).

```csharp
n.Methods(m => m.Public())
    .RequireConvention(NamingConvention.PascalCase)
    .AsError();

n.Classes(c => c.Abstract())
    .RequirePrefix("Abstract")
    .AsInfo();

n.LocalVariables()
    .RequireConvention(NamingConvention.CamelCase)
    .Disabled();
```

Modifiers attach to the verb call, not to the selector. They apply to that
specific rule instance — not to other rules on the same selector.

---

## 7. Exemptions

### 7.1 `ExceptWithAttribute<T>()`

A naming rule may be exempted for any symbol carrying a specified attribute.
This is the only per-rule exemption mechanism in v1.

```csharp
n.Classes()
    .RequireConvention(NamingConvention.PascalCase)
    .ExceptWithAttribute<GeneratedCodeAttribute>();

n.Methods(m => m.Public())
    .RequireConvention(NamingConvention.PascalCase)
    .ExceptWithAttribute<CompilerGeneratedAttribute>();
```

Multiple `ExceptWithAttribute<T>()` calls on a single rule OR together —
a symbol carrying *any* of the listed attributes is exempt.

Path-based exemption (`**/*.Generated.cs`, etc.) is handled by global
`config.Ignore(...)` — which is part of the global filters specification,
not the naming category. v1 of the naming spec does not define it.

---

## 8. Wire-format rule IDs

Each (selector × verb) tuple maps to a stable rule ID emitted to JSON/SARIF
output. Users **do not type these strings in C# config code**; they are the
contract for downstream tooling (CI, AI agents, suppression comments).

Serialized naming rules include a nullable integer `chainId` to preserve selector
composition when configurations are loaded. The builder assigns a unique ID on
the first verb call of each selector chain; subsequent calls on that chain reuse
it. IDs are local to a configuration and do not change rule IDs. Rules with missing
or null `chainId` are standalone declarations, preserving last-declaration-wins
behavior for older configurations.

### 8.1 Format

```
naming.<kind>.<predicate?>.<verb>
```

- `<kind>` ∈ `method` `class` `interface` `record` `enum` `property` `field` `parameter` `local` `namespace`
- `<predicate?>` ∈ accessibility (`public`/`internal`/`protected`/`private`)
  and the modifiers `async`/`const`/`static`/`abstract`/`sealed` — omitted when
  no predicate is used. When a selector combines predicates, the tokens appear
  accessibility-first (e.g. `naming.class.public.abstract.suffix`).
- `<verb>` ∈ `convention` `prefix` `suffix` `name` `regex`

### 8.2 Examples

| C# rule | Emitted rule ID |
| --- | --- |
| `n.Methods(m => m.Public()).RequireConvention(…)` | `naming.method.public.convention` |
| `n.Methods(m => m.Async()).RequireSuffix("Async")` | `naming.method.async.suffix` |
| `n.Classes().RequireConvention(…)` | `naming.class.convention` |
| `n.Classes().ForbidSuffix("Manager", …)` | `naming.class.suffix` |
| `n.Interfaces().RequirePrefix("I")` | `naming.interface.prefix` |
| `n.Classes(c => c.Abstract()).RequirePrefix("Abstract")` | `naming.class.abstract.prefix` |
| `n.Properties(p => p.Static()).RequireConvention(…)` | `naming.property.static.convention` |
| `n.Fields(f => f.Private()).RequirePrefix("_")` | `naming.field.private.prefix` |
| `n.Fields(f => f.Const()).RequireConvention(…)` | `naming.field.const.convention` |
| `n.Namespaces().RequirePrefix("Acme.")` | `naming.namespace.prefix` |

The ID is **direction-agnostic** — `Require` and `Forbid` on the same
selector × verb share an ID. The emitted diagnostic message states which
direction was violated. This keeps `config.Rule(Rules.Naming.Method.Public.Convention).Disabled()`
intuitive: disabling the ID disables every Require/Forbid pairing on that
selector × verb.

### 8.3 Inline source suppression

The wire-format ID is also the identifier used in suppression comments:

```csharp
// sharplint-disable-next-line naming.method.public.convention
public void notLikeThis() { }
```

---

## 9. Worked example

A complete, valid `sharplint.config.cs` exercising the naming category:

```csharp
#:package SharpLint.Configuration@1.0.0

using SharpLint.Configuration;

Lint.Configure(config =>
{
    config.UsePreset(Preset.Recommended);

    config.Naming(n =>
    {
        // Methods
        n.Methods(m => m.Public())
            .RequireConvention(NamingConvention.PascalCase)
            .AsError();

        n.Methods(m => m.Private())
            .RequireRegex("^_?[a-z][A-Za-z0-9]*$");

        n.Methods(m => m.Async()).RequireSuffix("Async");

        // Classes — PascalCase required, ban "stuff bag" suffixes.
        n.Classes()
            .RequireConvention(NamingConvention.PascalCase)
            .ExceptWithAttribute<GeneratedCodeAttribute>();

        n.Classes().ForbidSuffix("Manager", "Helper", "Util");

        // Abstract classes — nudge toward an "Abstract"/"Base" prefix.
        n.Classes(c => c.Abstract()).RequirePrefix("Abstract", "Base").AsInfo();

        // Interfaces — "I"-prefixed PascalCase.
        n.Interfaces()
            .RequirePrefix("I")
            .RequireConvention(NamingConvention.PascalCase);

        // Records / enums
        n.Records().RequireConvention(NamingConvention.PascalCase);
        n.Enums().RequireConvention(NamingConvention.PascalCase);

        // Properties
        n.Properties().RequireConvention(NamingConvention.PascalCase);

        // Fields
        n.Fields(f => f.Private())
            .RequirePrefix("_")
            .RequireConvention(NamingConvention.CamelCase);
        n.Fields(f => f.Const())
            .RequireConvention(NamingConvention.UpperSnakeCase);
        n.Fields(f => f.Public())
            .RequireConvention(NamingConvention.PascalCase);

        // Parameters & locals
        n.Parameters().RequireConvention(NamingConvention.CamelCase);
        n.LocalVariables().RequireConvention(NamingConvention.CamelCase);

        // Namespaces
        n.Namespaces().RequirePrefix("Acme.");
    });
}).Apply();
```

---

## 10. Stability

This document is normative for v1 of the naming category. Once `1.0.0` is
released:

- **No selector or verb defined here is removed** without a major version
  bump and a one-minor-version deprecation cycle.
- **Wire-format rule IDs are stable.** Renames go through a deprecation
  cycle where both old and new IDs are emitted side by side for one minor
  version.
- **Features in [§1.2 Out of scope](#12-out-of-scope-deferred-to-a-later-version)
  may land in any minor version.** They are additive and never break v1
  configs.

Changes to this document during the draft phase (pre-1.0.0) are tracked in
git history alone — no separate changelog is maintained until 1.0.0 ships.
