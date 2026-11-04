# Enforce your project's architecture

An agent can read architecture guidelines and still introduce a forbidden dependency. Turn the rules you can check mechanically into a custom rules enforcer that speaks LSP. Sharper brings its findings into the agent's editing loop, so a violation becomes precise feedback to fix and recheck.

## From guidelines to findings

Suppose your project separates domain, application and infrastructure code. Your architecture enforcer could check rules such as:

- Domain code must not import infrastructure modules.
- HTTP handlers must call application services instead of accessing persistence directly.
- A module may use another module only through its public API.
- New dependencies must follow your project's allowed dependency graph.

These are examples of rules your enforcer implements. Sharper does not ship these checks or define their configuration. You own the architecture policy, its exceptions and the program that evaluates it.

## How the feedback loop works

1. The agent edits code with its usual tools.
2. It calls `sharper_check` for the affected scope.
3. Sharper synchronizes matching documents and requests diagnostics from your configured services.
4. Your enforcer returns a finding such as `ARCH001: Domain must not depend on Infrastructure`, at the offending import.
5. The agent changes the dependency, then checks again until the intended violations are resolved and analysis is completed and current.

The same check can collect compiler findings and architecture violations from multiple matching services. The agent uses one MCP interface and does not choose a separate tool for each rules engine.

## Connect a custom enforcer

Install or build your enforcer separately. For example, a team-owned program called `architecture-lsp` might analyze Python source files. Register it in a Sharper profile:

```json
{
  "servers": [
    {
      "id": "architecture",
      "command": "/opt/team/bin/architecture-lsp",
      "arguments": ["--stdio"],
      "languages": { ".py": "python" },
      "navigation": false
    }
  ]
}
```

`architecture-lsp` is an illustrative program, not a package supplied by Sharper. Replace the command, arguments and mappings with your enforcer's actual interface. Add this entry alongside your language service to receive both sets of findings. Configure architecture rules using the enforcer's own configuration format. Sharper can pass its documented initialization options or settings; it does not interpret a rules DSL.

Connect the profile using [agent setup](agent-setup.md), or launch MCP through npx:

```sh
npx --yes @adrian13579/sharper@next mcp --lsp-config /absolute/path/servers.json
```

See [server profiles](custom-language-servers.md) for all configuration fields.

## What your enforcer must speak

Use Sharper's [supported LSP contract](lsp-service.md): stdio framing, initialization, UTF-16 positions, document synchronization and `textDocument/diagnostic` pulls. Return standard diagnostic codes, severities, messages and source ranges. A push-only server cannot provide verified checks through Sharper.

Give findings stable codes and actionable messages. Include related locations when they help explain a dependency or boundary. Advertise only supported capabilities; a rule-only service does not need to offer navigation. Treat cancellation and analysis completion accurately.

If an architecture rule depends on several files, your enforcer must track those dependencies and recompute affected findings. A clean check of one document does not prove the whole dependency graph is valid. Choose a scope that covers the change and qualify the enforcer's cross-file behavior.

## Make checks part of the agent's workflow

Add an instruction like this to your project's agent guidance:

> Establish a Sharper baseline before editing. After changes, check affected code and dependent modules. Fix introduced compiler and architecture violations. Before finishing, confirm analysis is completed and current, report remaining findings, and run the relevant builds and tests separately.

This creates a feedback loop the agent can follow. Sharper reports violations; it does not force an agent to call a tool, apply fixes or block commits. Use CI checks and repository policies when you need a mandatory merge gate, ideally backed by the same architecture rules.

## Validate the policy

Start with a known forbidden dependency, confirm its code and location, then fix it and recheck. Test allowed imports and explicit exceptions too. Repeat across representative workspaces and dependency changes. The confidence comes from your rules and their coverage, together with the [scope and freshness evidence](feedback.md) returned by Sharper.
