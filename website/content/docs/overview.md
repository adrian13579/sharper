# Rapid rule feedback for your agent

Sharper brings findings from your compiler, analyzers and custom LSP services into the agent's editing loop. Check affected code, fix violations and verify the result through one MCP interface, reusing warm local server sessions.

## Why early feedback matters

A full build or test run can do much more work than is needed to catch a rule violation in the code an agent just edited. Sharper reuses warm LSP services to request focused findings, which can make repeated feedback cheaper and faster than running those broader checks after every edit.

The value is the short correction loop. Find a violation early, fix it while the change is small and still in context, and avoid building more work on top of the mistake. The earlier the feedback, the better. Latency depends on your services, rules and workspace.

LSP findings cover the analysis your services provide. Builds and tests provide complementary evidence about compilation and runtime behavior.

## Get started

1. [Install Sharper with npm or npx](npm.md).
2. [Create your server profile and connect your agent](custom-language-servers.md).
3. Follow the workflow below while making changes.

## Your languages. Your rules.

Sharper speaks Language Server Protocol (LSP). Your configured services supply language analysis and rules; Sharper returns their findings with scope, completion and freshness evidence. Language support follows your server profiles and file mappings.

A custom service could check project-specific constraints. For an illustration, see the [architecture enforcer example](architecture-rules.md). That enforcer and its rules are supplied by you, not built into Sharper.

## Workflow

1. **Edit:** use the agent's normal tools and write changes to disk.
2. **Check:** after editing, call `sharper_check` on affected files or directories, including dependent code when shared interfaces change.
3. **Fix:** correct introduced violations using their codes, severities and locations, then check again.
4. **Verify:** confirm current, completed analysis, inspect remaining findings.

## Read the result

Require `status: "completed"`, `complete: true` and `freshness: "verified"` before treating analysis as current. These fields describe analysis completion and freshness. Read diagnostics and severities to assess rule compliance. For a partial, stale, failed or cancelled request, address the reported condition and check again.

Sharper returns findings for your agent to act on, together with evidence about the checked scope. Keep toolchains and external dependencies stable during a session, and restart after changing them. Use `sharper_status` to inspect server availability and capabilities.

See [Tool response format](responses.md) for JSON examples, field definitions and error handling.

## Agent guidance

Add a short instruction to your project's agent guidance:

> After editing files, check the affected code, fix introduced violations, and recheck. Before finishing, confirm analysis is completed and current, and report remaining findings.
