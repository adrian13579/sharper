# SharpLint Specification

This directory is the **source of truth** for the SharpLint configuration DSL.
Each file here defines an adopted part of the DSL at a level of detail sufficient
to implement, document, and stabilize that part for v1.

Anything not yet adopted lives in [`/SPEC.md`](../../SPEC.md) — the planning
and roadmap document. Features graduate from `SPEC.md` into this folder
once their shape is committed.

## Adopted modules

| Module | Status | Document |
| --- | --- | --- |
| Naming | Draft v1 | [naming.md](naming.md) |
| Syntax, Organization, Design, Dependencies, Architecture, Reliability, Performance, Project | Initial built-in policies adopted | [taxonomy.md](taxonomy.md) |

[Core taxonomy and module contracts](taxonomy.md) define the built-in rule modules,
initial policies, stable IDs, scopes, and external Roslyn analyzer configuration.
External users create their own modules; the built-in taxonomy does not constrain them.

## Not yet adopted (see [SPEC.md](../../SPEC.md))

- Additional module policies and specialized fluent builders
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
