# Working on Sharper

Sharper is a Rust MCP connector for fast language-agnostic feedback, rule checks and scoped verification through configured LSP servers. Run commands from the repository root.

## Distribution

Publish Sharper only through npm, for npm/npx consumers. Keep standalone binaries as internal CI artifacts. Do not mention legacy runtime/distribution tooling in current consumer documentation.

## Product messaging

Lead public messaging with rapid feedback on rule violations and the check, fix, verify workflow. Language-agnostic LSP connectivity enables that value. Keep implementation-language details out of the root README. Avoid unmeasured latency claims.

## Contracts and architecture

- Establish lifecycle, synchronization and freshness behavior from current code and tests. External servers own language semantics and rules. Sharper owns transport, synchronization, normalization, cancellation and evidence.
- Language support comes from explicit `--lsp-config` profiles, never hardcoded extension routing or repository-discovered executable configuration. Multiple matching servers contribute diagnostics. Do not add a rule engine, plugin registry, compiler workspace or unrestricted execute-command proxy.
- Expose exactly two MCP tools: `sharper_check` and `sharper_status`. All source operations are read-only. Never apply workspace edits or install project dependencies. MCP stdout is newline-delimited JSON-RPC only; logs go to stderr.
- Keep bounded warm sessions per workspace/server, evict/dispose contexts and stop child process groups/jobs on cancellation and shutdown. Initialization, queueing and analysis share a request deadline.
- Treat each Git worktree as its own workspace. Keep nested checkouts out of the parent workspace's synchronization and freshness boundary; never collapse worktrees onto their shared Git directory.
- Hash disk before/after checks. Watchers are hints and invalidate in-flight results conservatively. Never equate clean document diagnostics with a successful build/test run. Completion/freshness and findings are separate facts.
- Preserve diagnostic codes, unknown severities, source, data, tags and related information. Paths are absolute; positions are zero-based UTF-16. Keep deterministic numeric location ordering and preserve distinct findings/provenance.
- Agent setup preserves unrelated JSONC bytes/comments and validates before writing. Codex delegates registration to its native CLI. Registration never fixes a workspace.

## Layout

- `src/`: Rust CLI, MCP, LSP, profiles, normalization, workspace reconciliation and agent setup.
- `tests/core.rs`, `tests/mcp.rs`: controlled protocol, lifecycle and actual executable tests.
- `tests/fixtures/fake_lsp.rs`: controlled stdio server, built as the `fake-lsp` example.
- `tests/real_servers.rs`: explicitly ignored real-server qualification tests, run separately with configured dependencies.
- `tests/language-tools/`: pinned optional Node tools for integration testing only, not runtime dependencies.
- `assets/`: canonical embedded MCP icons. Preserve original dimensional neutral branding.
- `website/`: Next.js/TypeScript static GitHub Pages site. Cargo.toml supplies its version.
- `packaging/npm/`: dependency-free native command launcher. Release packages contain Rust executables.

## Validation

Use the toolchain in `rust-toolchain.toml`. Windows needs MSVC C++ tools and a Windows SDK; `./build.ps1` resolves import libraries for normal PowerShell sessions. Linux needs a C linker. Worktree regression tests need Git on `PATH`.

```sh
cargo fmt --check
cargo clippy --locked --all-targets -- -D warnings
cargo build --locked --example fake-lsp
cargo test --locked -- --test-threads=4
cargo build --locked --release
node --test packaging/npm/test/launcher.test.cjs
```

Run real-server tests separately with `cargo test --locked --test real_servers -- --ignored --test-threads=1`; set `SHARPER_TEST_NODE_TOOLS` and `SHARPER_TEST_ROSLYN` with SHARPER_TEST_NODE_TOOLS pointing to tests/language-tools and SHARPER_TEST_ROSLYN pointing to the server executable. Report ignored tests explicitly; controlled fixtures are not real integration evidence. `SHARPER_TEST_HOST` selects a release executable for MCP tests. The optional official JavaScript MCP SDK smoke test is `npm ci --prefix tests/mcp-client --ignore-scripts` followed by `node tests/mcp-client/smoke.mjs`. Run build/test/coverage/benchmarks sequentially. Mutation testing remains stopped at the user's request; do not restart it.

Website: from `website`, run `npm ci --ignore-scripts`, `npm run build`, then `npm test` after installing Playwright Chromium (or set `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH`). Review desktop/mobile screenshots. No Rust tests are necessary for docs-only changes.

Do not claim old .NET coverage, mutation or benchmark results apply to Rust. Pre-alpha status remains visible; stable releases need fresh cross-platform, client, stability, coverage and mutation evidence.

## Hygiene

Use a dedicated codex/ branch/worktree, preserve unrelated edits, do not hand-edit generated target/node_modules/build artifacts. Update documentation alongside public behavior. Make coherent Conventional Commits with imperative descriptions and a BREAKING CHANGE footer for contract changes.

Include `Co-authored-by: Codex <noreply@openai.com>` in commits created with Codex.

Consumer documentation lives in website/content/docs and is published under /docs/. Keep public copy focused on rapid rule feedback; omit implementation language and production-readiness pages. The canonical changelog is website/content/docs/changelog.md; add a matching version heading before a release. Stable-release evidence is supplied through READINESS_PATH.

## CI responsibilities

Build validates optimized Windows/Linux executables with the official MCP SDK and installed npm package. Tests runs runtime/MCP behavior and real-server integrations on both platforms; formatting and platform-independent Node tests run once. Release reuses those workflows for the selected tag and publishes a complete npm package only after validation succeeds. Website checks and Pages deployment run independently for website inputs. Keep these checks focused and avoid rerunning the entire MCP suite against an executable already covered by the release smoke checks.
