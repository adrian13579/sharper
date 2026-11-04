# Native Rust installation

Sharper compiles to a native executable. There is no .NET runtime, SDK, Native AOT publishing step or Node runtime dependency in the host. Individual LSP servers can require their own runtimes and project dependencies.

## Build from source

Use rust-toolchain.toml (Rust 1.99.1), Cargo.lock and a native linker. Windows needs Visual Studio C++ tools and a Windows SDK; Linux needs a C linker and glibc.

```sh
cargo build --locked --release
cargo install --path . --locked
sharper --version
sharper setup
```

PowerShell users can run `./build.ps1 --release`; it resolves MSVC import libraries for a normal shell. `./build.sh --release` is the Unix equivalent. The release executable is target/release/sharper (sharper.exe on Windows). Configure LSP services explicitly before using tools.

## Distribution

Release workflows build Windows x64 and Linux x64 native binaries, test them, upload checksummed release assets and assemble the npm package. Check [GitHub releases](https://github.com/adrian13579/sharper/releases) for actual availability. Source candidates are not automatically published.

NuGet/.NET-tool distribution was retired in the Rust migration. Use Cargo from a checkout, a published native release or the published npm wrapper. See [migration](migration-to-rust.md), [npm](npm.md) and [publishing](publishing.md).
