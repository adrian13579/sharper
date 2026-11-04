# Rapid rule feedback for your agent

Sharper brings findings from your compiler, analyzers and custom LSP services into the agent's editing loop. Check affected code, fix violations and verify the result through one MCP interface, reusing warm local server sessions.

## Get started

1. [Install Sharper with npm or npx](npm.md).
2. [Create your server profile and connect your agent](custom-language-servers.md).
3. Follow the workflow below while making changes.

## Your languages. Your rules.

Sharper speaks Language Server Protocol (LSP). Your configured services supply language analysis and rules; Sharper returns their findings with scope, completion and freshness evidence. Language support follows your server profiles and file mappings.

A custom service could check project-specific constraints. For an illustration, see the [architecture enforcer example](architecture-rules.md). That enforcer and its rules are supplied by you, not built into Sharper.

## Workflow

1. **Baseline:** check relevant files before editing to identify existing findings.
2. **Edit:** use the agent's normal tools and write changes to disk.
3. **Check:** call `sharper_check` on affected files or directories, including dependent code when shared interfaces change.
4. **Fix:** correct introduced violations using their codes, severities and locations, then check again.
5. **Verify:** confirm current, completed analysis, inspect remaining findings and run relevant builds and tests separately.

For example, pass this to `sharper_check`:

```json
{ "workspace": "/work/app", "path": "src" }
```

Omitting `path` covers mapped source documents across the workspace. Every matching configured service contributes feedback.

## Read the result

Require `status: "completed"`, `complete: true` and `freshness: "verified"` before treating analysis as current. These fields describe the request; they do not mean every rule passed. Inspect diagnostics and severities, since completed analysis can contain errors. Partial, stale, failed or cancelled requests do not establish a clean result.

Sharper reports findings and never applies source edits or blocks commits. Empty diagnostics do not prove a build or test suite passed. Keep toolchains and external dependencies stable during a session, and restart after changing them. `sharper_status` reports availability, not verification.

## Agent guidance

Add a short instruction to your project's agent guidance:

> Establish a baseline before editing. Check affected code after changes, fix introduced violations, and recheck. Before finishing, confirm analysis is completed and current, report remaining findings, and run relevant builds and tests separately.
