# Sharplint

<p align="center">
  <img src="https://raw.githubusercontent.com/adrian13579/sharplint/main/logo.png" alt="Sharplint logo" width="600" />
</p>

<p align="center">
  <a href="https://github.com/adrian13579/sharplint/actions/workflows/dotnet.yml"><img src="https://github.com/adrian13579/sharplint/actions/workflows/dotnet.yml/badge.svg?branch=main" alt="Build status" /></a>
  <a href="https://github.com/adrian13579/sharplint/actions/workflows/dotnet.yml"><img src="https://raw.githubusercontent.com/adrian13579/sharplint/badges/coverage.svg" alt="Test coverage" /></a>
  <a href="https://github.com/adrian13579/sharplint/blob/main/LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-blue" alt="License: Apache 2.0" /></a>
  <a href="https://github.com/adrian13579/sharplint/issues"><img src="https://img.shields.io/github/issues/adrian13579/sharplint" alt="Open issues" /></a>
</p>

Sharplint connects AI coding agents to C# compiler and analyzer diagnostics from Microsoft's official Roslyn language server. It provides a local, read-only MCP server and CLI backed by the same diagnostic service.

An agent can check a file or directory, use diagnostic codes and source locations to guide an edit, then check again for feedback. Sharplint synchronizes files from disk, requests diagnostics over LSP, and returns structured results with completion and freshness information.

- **MCP tools** give agents access to diagnostics and language-server status.
- **CLI commands** provide readable or JSON output for terminals and scripts.
- **Persistent MCP sessions** reuse Roslyn across source edits.
- **Native analyzer configuration** uses your project's NuGet packages, analyzer references and `.editorconfig`.

## Requirements and installation

Sharplint is available as a .NET tool. The current release is **[0.2.0-prealpha.1](https://github.com/adrian13579/sharplint/releases/tag/v0.2.0-prealpha.1)**.

- Install the .NET **10 SDK** to install and run the tool.
- Install the [official Roslyn language server](https://github.com/dotnet/roslyn/blob/main/src/LanguageServer/roslyn-language-server/README.md). The tested version is pinned below; other versions must support the protocol described in [the service contract](https://github.com/adrian13579/sharplint/blob/main/docs/lsp-service.md).
- Restore the target solution's dependencies and build custom analyzer projects before checking.

```sh
dotnet tool install --global sharplint --version 0.2.0-prealpha.1
dotnet tool install --global roslyn-language-server --version 5.13.0-1.26514.10
sharplint --version
```

<details>
<summary>Build and install from source</summary>

Use the SDK selected by `global.json` (**10.0.202**, with `latestMinor` roll-forward). From a checkout:

```sh
dotnet build SharpLint.slnx -m:1
dotnet pack src/SharpLint.Cli -c Release -o artifacts/packages
dotnet tool install --global sharplint --version 0.2.0-prealpha.1 --add-source ./artifacts/packages
```

On Windows, you can also build with `.\build.ps1` from PowerShell. Each repository `.sh` script has a matching `.ps1` script with the same arguments.

</details>

Use trusted projects: loading projects through Roslyn evaluates MSBuild and may run project build logic.

## MCP

Configure your MCP client with an absolute workspace path:

```json
{
  "mcpServers": {
    "sharplint": {
      "command": "sharplint",
      "args": ["mcp", "--workspace", "/absolute/path/to/repository"],
      "env": {
        "SHARPLINT_ROSLYN_PATH": "/absolute/path/to/roslyn-language-server"
      }
    }
  }
}
```

During development, use `"command": "dotnet"` and prepend the absolute path to `src/SharpLint.Cli/bin/Debug/net10.0/sharplint.dll` to `args`. Logs go to stderr; stdout carries MCP messages.

There are two tools:

- `sharplint_check(path?)`: reconcile disk and request diagnostics. An omitted path checks C# documents recursively under the workspace root, excluding generated sources in `bin` and `obj`. A path selects a file or directory inside that root.
- `sharplint_status()`: initialize or inspect Roslyn availability, project loading, diagnostic capabilities, session/process identity, errors and limitations.

The MCP process keeps one Roslyn session warm. Source edits reuse that session; changes to project inputs or the source file set reload the session. Checks support cancellation and a configurable timeout.

## CLI

```sh
sharplint check
sharplint check src/Example.cs
sharplint check --path ./src --format json
sharplint check src/Example.cs --workspace /path/to/repo --solution Example.slnx
sharplint status --format json
sharplint mcp --workspace /path/to/repo --timeout 120
```

`--server` selects an executable or language-server DLL. Otherwise discovery uses `SHARPLINT_ROSLYN_PATH`, `PATH`, then the default .NET global tool directory. `--workspace` defaults to the current directory; for compatibility, a directory passed to `check --path` also becomes the workspace if none is explicit. With one root solution, Roslyn auto-loads it; use `--solution` when several exist. A root without a solution uses Roslyn's automatic C# project discovery (maximum 500 projects).

Each CLI invocation starts its own Roslyn session. Use MCP for repeated checks in a persistent session.

Exit codes: **0** = complete document check with zero error-severity diagnostics, **1** = complete document check with diagnostic errors, **2** = incomplete, timed out, failed or invalid request, **130** = cancelled. `status` uses 0 for an available diagnostic session and 2 for failures.

## Ordinary and custom analyzers

Use normal .NET project configuration and `.editorconfig`:

```xml
<PropertyGroup>
  <EnableNETAnalyzers>true</EnableNETAnalyzers>
  <AnalysisLevel>latest</AnalysisLevel>
</PropertyGroup>
<!-- For a custom analyzer built as part of your solution: -->
<ItemGroup>
  <ProjectReference Include="../MyAnalyzer/MyAnalyzer.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

```ini
[*.cs]
dotnet_diagnostic.CA1822.severity = warning
```

Roslyn loads analyzer NuGet packages through the same native project integration. Build custom analyzer projects and restore packages before checking. Sharplint forwards the resulting diagnostics using their original codes and severities.

Sharplint is licensed under [Apache 2.0](https://github.com/adrian13579/sharplint/blob/main/LICENSE).
