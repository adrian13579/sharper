# Example: a custom architecture enforcer

This example illustrates a service you could build or install separately. Sharper does not include an architecture analyzer, dependency policy or architecture rules. It connects an external LSP service's findings to your agent.

Suppose your team has written an LSP enforcer for its own architecture guidelines. Connecting that service lets the agent receive precise diagnostics, correct violations and recheck through Sharper.

## From guidelines to findings

Suppose your project separates domain, application and infrastructure code. Your architecture enforcer could check rules such as:

- Domain code must not import infrastructure modules.
- HTTP handlers must call application services instead of accessing persistence directly.
- A module may use another module only through its public API.
- New dependencies must follow your project's allowed dependency graph.

These are examples of rules your enforcer implements. Sharper does not ship these checks or define their configuration. You own the architecture policy, its exceptions and the program that evaluates it.

## Example finding

Your service might return `ARCH001: Domain must not depend on Infrastructure` at an offending import. The agent can change the dependency and recheck using the [Overview workflow](overview.md#workflow). This diagnostic code and policy are illustrative; they are defined by your service.

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

Connect the profile using [configuration](custom-language-servers.md#connect-your-agent), or launch MCP through npx:

```sh
npx --yes @adrian13579/sharper@next mcp --lsp-config /absolute/path/servers.json
```

See [server profiles](custom-language-servers.md) for all configuration fields.

## What your enforcer must speak

Use Sharper's [server requirements](custom-language-servers.md#server-requirements): stdio framing, initialization, UTF-16 positions, document synchronization and `textDocument/diagnostic` pulls. Return standard diagnostic codes, severities, messages and source ranges. A push-only server cannot provide verified checks through Sharper.

Give findings stable codes and actionable messages. Include related locations when they help explain a dependency or boundary. Advertise only supported capabilities; a rule-only service does not need to offer navigation. Treat cancellation and analysis completion accurately.

If an architecture rule depends on several files, your enforcer must track those dependencies and recompute affected findings. A clean check of one document does not prove the whole dependency graph is valid. Choose a scope that covers the change and qualify the enforcer's cross-file behavior.

Sharper reports the service's findings; it does not force the agent to call a tool or block commits. Use CI and repository policies if you need mandatory enforcement.

## Validate the policy

Start with a known forbidden dependency, confirm its code and location, then fix it and recheck. Test allowed imports and explicit exceptions too. Repeat across representative workspaces and dependency changes. The confidence comes from your rules and their coverage, together with the [check results](overview.md#read-the-result) returned by Sharper.
