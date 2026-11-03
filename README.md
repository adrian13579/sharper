# Sharplint

![Sharplint logo](https://raw.githubusercontent.com/adrian13579/sharplint/main/logo.png)

[![Build status](https://github.com/adrian13579/sharplint/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/adrian13579/sharplint/actions/workflows/dotnet.yml)
[![Test coverage](https://raw.githubusercontent.com/adrian13579/sharplint/badges/coverage.svg)](https://github.com/adrian13579/sharplint/actions/workflows/dotnet.yml)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache--2.0-blue)](https://github.com/adrian13579/sharplint/blob/main/LICENSE)
[![Open issues](https://img.shields.io/github/issues/adrian13579/sharplint)](https://github.com/adrian13579/sharplint/issues)

Sharplint is a local MCP server that gives coding agents C# compiler and analyzer feedback from Microsoft's official Roslyn language server.

The agent selects the repository it is working on, checks a file or directory, uses diagnostic codes and source locations to guide an edit, and checks again. Sharplint keeps Roslyn sessions warm across these tool calls, including when the agent switches between repositories.

The `sharplint` executable starts the MCP server and configures agent connections.

## Requirements and installation

Sharplint is available as a .NET tool in pre-alpha.

- Install the .NET SDK selected by [global.json](https://github.com/adrian13579/sharplint/blob/main/global.json) to install and run the tool.
- Install the [official Roslyn language server](https://github.com/dotnet/roslyn/blob/main/src/LanguageServer/roslyn-language-server/README.md) with support for the protocol described in [the service contract](https://github.com/adrian13579/sharplint/blob/main/docs/lsp-service.md).
- Restore the target solution's dependencies and build custom analyzer projects before checking.

```sh
dotnet tool install --global sharplint --prerelease
dotnet tool install --global roslyn-language-server --prerelease
sharplint --version
```

### Build and install from source

Use the SDK selected by `global.json`. From a checkout:

```sh
dotnet build SharpLint.slnx -m:1
dotnet pack src/SharpLint.Cli -c Release -o artifacts/packages
dotnet tool install --global sharplint --prerelease --add-source ./artifacts/packages
```

On Windows, you can also build with `.\build.ps1` from PowerShell. Each repository `.sh` script has a matching `.ps1` script with the same arguments.

Use trusted projects: loading projects through Roslyn evaluates MSBuild and may run project build logic.

## Set up GitHub Copilot

Register Sharplint for both Copilot CLI and VS Code:

```sh
sharplint setup copilot
```

To configure one client:

```sh
sharplint setup copilot --client cli
sharplint setup copilot --client vscode
```

Setup merges the Sharplint entry into the clients' user configuration, preserving other servers and settings. It registers the executable you are running. Restart the client's MCP connection after setup, then ask the agent to use Sharplint for its working repository.

For custom profiles or portable installations, use `--copilot-config` or `--vscode-config` to select the configuration file. `--server` selects a custom Roslyn executable or DLL.

## MCP configuration

For other MCP clients, add a server using this configuration:

```json
{
  "mcpServers": {
    "sharplint": {
      "command": "sharplint",
      "args": ["mcp"]
    }
  }
}
```

The same server connection can serve multiple repositories. The agent passes its repository directory in each tool call, or an absolute source path to discover the repository. Clients advertising MCP workspace roots can also supply the context.

- `sharplint_check(workspace?, path?, solution?)` returns C# document diagnostics for the selected repository, file or directory.
- `sharplint_status(workspace?, solution?)` reports the selected repository's Roslyn session, readiness and capabilities.

`workspace` is an absolute repository directory. `path` can be absolute or relative to that repository; an omitted path checks its C# documents. `solution` selects a solution when a repository contains several.

The server keeps up to four repository/solution contexts warm and releases the least recently used context when it needs another. Source edits reuse the session; changes to project inputs or source file membership reload it. Diagnostics include scope, completion and freshness metadata.

## Start the server

```sh
sharplint mcp
```

Running `sharplint` without arguments also starts MCP stdio. `--server` selects Roslyn explicitly; otherwise Sharplint discovers it through `SHARPLINT_ROSLYN_PATH`, `PATH`, and the .NET global tool directory. `--timeout` sets the tool request deadline.

During development, launch the built `sharplint.dll` with `dotnet`. Logs go to stderr; stdout carries MCP messages.

Sharplint is licensed under [Apache 2.0](https://github.com/adrian13579/sharplint/blob/main/LICENSE).
