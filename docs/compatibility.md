# Compatibility and qualification

Sharper's Rust core is language-agnostic. Configure server commands, extension mappings and exact LSP language IDs; capability support determines which tools can run. There is no built-in supported-language list and no claim that every LSP server is compatible.

## Required protocol

Standard stdio LSP, UTF-16 positions, open/close, full/incremental synchronization and document-pull diagnostics are required for verified feedback. Push-only services are incomplete for checks. Navigation additionally requires the advertised standard operation. Readiness notifications, initialization options and settings can be configured. See [profiles](custom-language-servers.md) and [contract](lsp-service.md).

Optional server-specific adapters are isolated configuration choices, not hardcoded language routing. The TypeScript compatibility mode uses only its synchronous syntax/semantic diagnostic bridge. Other services normally use standard pulls.

## Automated evidence

- Rust controlled-server tests cover arbitrary extension/language IDs, multiple rule services, protocol framing, synchronization, metadata, navigation, reloads, failures and bounded sessions.
- Actual Rust MCP executable tests cover JSON-RPC, roots, structured responses, timeouts, cancellation, process-tree shutdown and agent registration.
- Optional real-server tests qualify configured Roslyn, TypeScript and ESLint examples; these are examples of integration, not the boundary of language support.

The pinned Node tools in tests/language-tools use TypeScript 5.9.3, TypeScript Language Server 6.0.3, vscode-langservers-extracted 4.10.0 and ESLint 10.14.0. Roslyn qualification uses official roslyn-language-server 5.13.0-1.26514.10 and .NET 10 target fixtures. Those runtimes are test/server dependencies, not Rust host dependencies. Node 24 is used for these tests and website/npm tooling.

```sh
npm ci --prefix tests/language-tools --ignore-scripts
# Set SHARPER_TEST_NODE_TOOLS to the absolute tests/language-tools directory.
# Set SHARPER_TEST_ROSLYN to an installed official executable at a short path.
cargo test --locked --test real_servers -- --ignored --test-threads=1
```

Real-server tests are explicitly ignored by default and require the configured dependencies. Report ignored tests and distinguish controlled protocol evidence from real integrations. Windows and Linux CI run the same Rust suites; native/release and client qualification remain separate evidence.

## Migration limits

Version 0.3 changes runtime, profiles and distribution. See [migration](migration-to-rust.md). The previous .NET suite, coverage, mutation, benchmark and soak results do not qualify this Rust implementation. Source text must currently be UTF-8. Symlinks and inputs outside the declared reconciliation boundary remain unsupported.
