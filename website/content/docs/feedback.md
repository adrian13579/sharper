# Fast feedback and rule verification

Sharper keeps compiler, analyzer and rule-enforcement feedback close to agent edits. Its host connects configured LSP services to MCP clients, reusing warm local sessions. Language support follows explicit profiles and protocol capabilities, not a built-in language list.

## Baseline, edit, check, fix, verify

1. **Baseline:** check relevant files before editing to identify existing findings.
2. **Edit:** use the agent's normal editor and write changes to disk.
3. **Check:** run sharper_check on the affected files/directory, including dependents for shared API changes.
4. **Fix:** correct introduced rule violations using codes, severities and precise locations; repeat the check.
5. **Verify:** confirm current completed analysis, inspect remaining findings, and run relevant builds/tests separately.

```json
{ "workspace": "/work/app", "path": "src" }
```

Pass this to sharper_check. An omitted path covers mapped source documents across the workspace. Every matching configured server contributes feedback, and all selected services must complete with current inputs for overall verification.

## Your languages and rules over LSP

Your compilers, analyzers and custom rule enforcers own the enforcement criteria. If a rule server implements Sharper's supported LSP document-pull contract, [configure it](custom-language-servers.md) and use the same feedback tools. Sharper does not implement a policy DSL, apply fixes or block commits. CI and repository policies own merge gates.

## Assess the evidence

Require status completed, complete true and freshness verified before treating feedback as current. These describe the request, not a rule-pass certificate. Inspect diagnostics and severities: completed analysis may contain errors. Partial, stale, failed or cancelled requests never establish a clean result. sharper_status reports availability/capabilities, not freshness.

Report checked scope, remaining findings and separate builds/tests run. Empty document diagnostics do not establish compilation, test success or exhaustive rule execution. Source changes must be on disk; unsaved buffers are not read. Excluded dependencies, external imports and toolchains must remain stable until restart. See [the service contract](lsp-service.md).

## Feedback performance

Warm sessions avoid launching a language server for every edit. Configuration and membership changes can reload contexts. Initialization, workspace size, rules and server implementation affect latency; there is no universal response-time guarantee.

Definitions, implementations and references are optional [navigation perks](code-navigation.md), useful for understanding a change's impact rather than verifying rule compliance.
