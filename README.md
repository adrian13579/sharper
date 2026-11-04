# Sharper

![Sharper logo](https://raw.githubusercontent.com/adrian13579/sharper/main/logo.svg)

[![Tests](https://github.com/adrian13579/sharper/actions/workflows/tests.yml/badge.svg?branch=main)](https://github.com/adrian13579/sharper/actions/workflows/tests.yml)
[![Build](https://github.com/adrian13579/sharper/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/adrian13579/sharper/actions/workflows/build.yml)
[![Latest release](https://img.shields.io/github/v/release/adrian13579/sharper?include_prereleases&label=release)](https://github.com/adrian13579/sharper/releases)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

**Rapid rule feedback for coding agents. Check, fix, verify after every edit.**

Sharper gives your agent actionable compiler and rule findings through MCP, with precise locations and evidence about the checked scope. [Get started](https://adrian13579.github.io/sharper/docs/overview/) by connecting your language services and custom LSP rule enforcers to keep feedback close to each edit. Warm, focused LSP checks can provide feedback faster and at lower cost than repeatedly running a full build or test suite. Catch violations while the agent works: the earlier the feedback, the less work builds on a mistake.

A custom enforcer could check forbidden dependencies or layer boundaries. [See an example](https://adrian13579.github.io/sharper/docs/architecture-rules/).

## Build from source

Install the [pinned toolchain](rust-toolchain.toml) and a native linker, then:

```sh
git clone https://github.com/adrian13579/sharper.git
cd sharper
cargo build --locked --release
```

The executable is `target/release/sharper` (`sharper.exe` on Windows). Run it with `setup` for guided configuration.

On Windows, install the Visual Studio C++ build tools and Windows SDK, and run the build from a Developer PowerShell. Linux requires a C linker.

## Changelog

See the [changelog](https://adrian13579.github.io/sharper/docs/changelog/) for updates.

## Contributing

Contributions welcome. See the [contributing guide](CONTRIBUTING.md).

## License

[Apache License 2.0](LICENSE).
