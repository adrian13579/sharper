# Production readiness

**Sharper is not ready for production.** The Rust migration is pre-alpha and requires new qualification. Do not carry previous implementation test, coverage, mutation, benchmark or soak numbers into Rust claims.

Current validation must distinguish controlled LSP fixtures, actual MCP executable checks and real external servers. A server advertising a capability is not sufficient evidence that its project semantics, rule configuration and synchronization satisfy the intended workflow.

Before a stable release, require:

- Clean Rust build, format, Clippy and tests on Windows and Linux, including explicit real-server runs and installed native/npm behavior.
- Representative configured language/rule-server qualification, including unsupported/push-only behavior, provenance, mixed failures and external-input limits.
- Actual Codex, Claude Code, OpenCode and Copilot client evidence at recorded versions.
- Fresh Rust line/branch coverage and mutation quality. Require at least 90% mutation score; never reduce thresholds or exclude production code just to improve scores. Mutation testing remains stopped at the user's request, so this gate is unfulfilled.
- Representative cold/warm benchmarks, sample counts, median/P95, hardware/runtime context and stability/soak evidence. No universal latency claim or unmeasured performance target.
- Reviewed packaging, dependency/licensing/security and release evidence bound to the exact commit/version.

The release workflow permits pre-alpha packages, but stable publication requires completed readiness evidence. Document diagnostics never certify builds/tests or every rule's execution. See [contract](lsp-service.md), [compatibility](compatibility.md) and [publishing](publishing.md).
