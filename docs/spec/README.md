# SharpLint Specification

This directory is the **source of truth** for the SharpLint configuration DSL.
Each file here defines one category of the DSL at a level of detail sufficient
to implement, document, and stabilize that category for v1.

Anything not yet adopted lives in [`/SPEC.md`](../../SPEC.md) — the planning
and roadmap document. Features graduate from `SPEC.md` into this folder
once their shape is committed.

## Adopted categories

| Category | Status | Document |
| --- | --- | --- |
| Naming | Draft v1 | [naming.md](naming.md) |
| Syntax, Organization, Design, Dependencies, Architecture, Reliability, Performance, Project | Taxonomy adopted; no built-in policies | [taxonomy.md](taxonomy.md) |

[Core taxonomy and module contracts](taxonomy.md) define category boundaries,
stable IDs, scopes, ownership, registration, and generic rule configuration.

## Not yet adopted (see [SPEC.md](../../SPEC.md))

- Additional category-specific policy DSLs (including Syntax and Architecture)
- Dependency Injection policies
- Testing
- Entity Framework Core pack
- ASP.NET Core pack
- xUnit pack
- Filters / exemptions (global)
- Custom predicates / escape hatches
- Diagnostic schema (wire format)

## Conventions used in these documents

- **Naming rule IDs** in the form `<category>.<kind>.<predicate?>.<verb>` are
  stable identifiers emitted in diagnostics. Naming builders generate them;
  module policies use their registered IDs with the generic `config.Rule(...)` API.
- **Status markers** at the top of each document name the v1 scope and list
  deferred features explicitly.
- **Worked examples** are executable `sharplint.config.cs` content unless explicitly
  labeled as conceptual examples or host integration fragments.
