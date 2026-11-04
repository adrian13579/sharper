# Example server profiles

These are integration examples, not a supported-language limit. Replace commands and absolute script paths, install the server/runtime, configure native project rules and test compatibility. The generic profile works with any matching implementation of the supported LSP contract. Merge servers into one servers array to combine findings.

- generic.json: standard pull diagnostics and arbitrary language mappings.
- roslyn.json: an example explicit readiness notification/settings profile.
- typescript.json: the optional closed synchronous diagnostic adapter.
- eslint.json: standard document pulls and native configured rules.

No server or project dependencies are installed by loading a profile.
