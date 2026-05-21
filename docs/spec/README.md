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

## Not yet adopted (see [SPEC.md](../../SPEC.md))

- Style
- Async
- Architecture
- Dependency Injection
- Testing
- Entity Framework Core pack
- ASP.NET Core pack
- xUnit pack
- Filters / exemptions (global)
- Custom predicates / escape hatches
- Diagnostic schema (wire format)

## Conventions used in these documents

- **Rule IDs** in the form `<category>.<kind>.<predicate?>.<verb>` are the
  stable wire-format identifiers emitted to JSON/SARIF output. Users do not
  type these in C# config; the typed `Rules` catalog is the only entry point.
- **Status markers** at the top of each document name the v1 scope and list
  deferred features explicitly.
- **Worked examples** are valid, copy-pasteable `sharplint.config.cs` content.
