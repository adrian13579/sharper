# Publishing Rust releases

Cargo.toml is the version source of truth. Commit Cargo.lock and a matching docs/release-notes/vVERSION.md. Tag vVERSION only after Windows/Linux builds, controlled/native MCP tests, real-server qualification and package checks pass.

The release workflow checks out the requested tag, validates its version/notes/readiness, builds native Rust executables on Windows/Linux, attaches checksums and assembles the two-platform npm tarball. It publishes GitHub release assets; npm publication is opt-in through NPM_PUBLISH_ENABLED and trusted publishing. Source templates/scripts never publish on their own.

Pre-alpha tags remain prereleases and do not imply production approval. Stable versions require docs/release-readiness.json (or an explicit evidence file) with matching version/commit and passing platform, client, stability, benchmark, coverage and mutation evidence. Mutation score must be at least 90%; stopped/missing measurements fail. Real-server counts must be positive with zero skips on both platforms. Stability requires at least 60 minutes and 1,000 documents per workspace, and benchmarks need at least 20 warm samples per scenario. The Rust migration has not met these gates.

No NuGet publishing or .NET pack step remains. Cargo registry publication is not configured; check name ownership and authorization before enabling it. See [native installation](native-installation.md), [npm](npm.md) and [readiness](production-readiness.md).
