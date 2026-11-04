# Real Node language-server test tools

These are test-only npm dependencies, not dependencies of the shipped .NET package. The lockfile pins TypeScript Language Server 6.0.3, TypeScript 5.9.3, vscode-langservers-extracted 4.10.0, ESLint 10.14.0 and typescript-eslint 8.72.1. Use Node 22.22.2 or newer; CI uses Node 24.

From the repository root in PowerShell:

```powershell
npm ci --prefix tests/NodeTools --ignore-scripts
$env:SHARPER_TEST_NODE_TOOLS = (Resolve-Path tests/NodeTools).Path
dotnet build Sharper.slnx -m:1
dotnet test Sharper.slnx -m:1
```

On POSIX shells, set `SHARPER_TEST_NODE_TOOLS="$(pwd)/tests/NodeTools"`. Also set `SHARPER_TEST_ROSLYN` to run the real C# tests; both sets explicitly skip when their server environment is absent. CI enables both and rejects skips.

Tests create disposable workspaces under `fixtures/` so native module resolution finds these parent dependencies. They verify actual TypeScript and ESLint responses, source edits, native config changes, missing lint config, and MCP backend isolation. Source remains unchanged by checks. Fixtures are removed on disposal; a crash may leave ignored fixture directories for inspection. npm is run only as explicit test setup, never by Sharper against a caller's project.
