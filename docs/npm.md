# npm distribution

The optional @adrian13579/sharper package wraps native Rust executables for Windows x64 and Linux x64 with glibc. Node is needed for the npm launcher; the Rust binary itself has no Node or .NET runtime dependency. The launcher forwards arguments and stdio unchanged, with no shell, downloads or install scripts. See releases/registry for actual availability; an unpublished source candidate is not installable from the registry.

## Build a local tarball

Build release binaries on their native platforms. Assemble both for a distributable package:

```sh
node scripts/package-npm.mjs --windows /path/sharper.exe --linux /path/sharper --output artifacts/npm
```

For local validation only, --allow-partial permits one platform binary. Never publish a partial package. The builder reads Cargo.toml's version and stamps the private source template; it does not publish. Source packaging/npm/package.json remains private.

```sh
npm install --global /absolute/path/adrian13579-sharper-VERSION.tgz
sharper setup
```

For clients launching through npx, use an available published package version directly with mcp --lsp-config; avoid registering a temporary-cache executable path. Global setup registrations should use the installed tool. Package publication is opt-in via NPM_PUBLISH_ENABLED and npm trusted publishing; native release assets are separate.
