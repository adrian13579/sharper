# Install with npm or npx

Sharper is distributed only through npm as `@adrian13579/sharper`. Node.js 22.22.2 or newer is required. The package includes Windows x64 and Linux x64 (glibc) executables; the launcher forwards arguments and stdio without shell execution, downloads or install scripts. Language servers retain their own prerequisites.

Sharper is pre-alpha. Use the `next` tag when a prerelease is available:

```sh
npm install --global @adrian13579/sharper@next
sharper setup
```

Global installation is recommended for guided agent registration so the recorded executable path remains stable. Do not use `npx ... setup` to register a temporary cache path.

## Run through npx

Configure your MCP client to launch npx directly:

```json
{
  "mcpServers": {
    "sharper": {
      "command": "npx",
      "args": [
        "--yes",
        "@adrian13579/sharper@next",
        "mcp",
        "--lsp-config",
        "/absolute/path/servers.json"
      ]
    }
  }
}
```

Adapt the enclosing schema to your client. Pin an exact published version instead of `next` for reproducible installations. Check [npm](https://www.npmjs.com/package/@adrian13579/sharper) for available versions; a source version is not proof of registry publication.
