# Example: a custom architecture enforcer

This example connects a team-owned architecture enforcer to an agent through Sharper. The external service defines the architecture rules; Sharper delivers its findings through the same feedback tools used for language analysis.

Suppose your team has written an LSP enforcer for its own architecture guidelines. Connecting that service lets the agent receive precise diagnostics, correct violations and recheck through Sharper.

## From guidelines to findings

Suppose your project separates domain, application and infrastructure code. Your architecture enforcer could check rules such as:

- Domain code must not import infrastructure modules.
- HTTP handlers must call application services instead of accessing persistence directly.
- A module may use another module only through its public API.
- New dependencies must follow your project's allowed dependency graph.

Your enforcer implements these example rules and owns their configuration, exceptions and evaluation.

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

`architecture-lsp` represents your own separately installed service in this example. Replace the command, arguments and mappings with your enforcer's actual interface. Add this entry alongside your language service to receive both sets of findings. Configure architecture rules using the enforcer's own configuration format. Sharper passes initialization options and settings to the service, which interprets its own rule configuration.

Connect the profile using [configuration](custom-language-servers.md#connect-your-agent), or launch MCP through npx:

```sh
npx --yes @adrian13579/sharper@next mcp --lsp-config /absolute/path/servers.json
```

See [server profiles](custom-language-servers.md) for all configuration fields.

## What your enforcer must speak

Use Sharper's [server requirements](custom-language-servers.md#server-requirements): stdio framing, initialization, UTF-16 positions, document synchronization and `textDocument/diagnostic` pulls. Return standard diagnostic codes, severities, messages and source ranges. Document-pull reports provide the completion evidence used for verified checks.

Give findings stable codes and actionable messages. Include related locations when they help explain a dependency or boundary. Advertise the capabilities your service implements; a rule-only service can focus on diagnostics. Treat cancellation and analysis completion accurately.

If an architecture rule depends on several files, your enforcer must track those dependencies and recompute affected findings. Choose a scope that covers the change and qualify the enforcer's cross-file behavior to assess the relevant dependency graph.

Sharper gives the agent actionable findings during editing. CI and repository policies can use the same rules for mandatory merge checks.

## Validate the policy

Start with a known forbidden dependency, confirm its code and location, then fix it and recheck. Test allowed imports and explicit exceptions too. Repeat across representative workspaces and dependency changes. The confidence comes from your rules and their coverage, together with the [check results](overview.md#read-the-result) returned by Sharper.
