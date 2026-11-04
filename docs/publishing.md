# Publishing Rust releases

Cargo.toml is the version source of truth. Commit Cargo.lock and a matching docs/release-notes/vVERSION.md. Tag vVERSION only after Windows/Linux builds, controlled/native MCP tests, real-server qualification and package checks pass.

The release workflow checks the requested tag and readiness metadata, builds and tests both platform executables, then assembles a complete npm package. It publishes only to npm using trusted publishing with provenance. Prereleases use `next`; stable releases use `latest`. Native executables are internal CI artifacts, not separate public downloads. Configure the npm package's trusted publisher for this repository and release workflow before releasing. A publication failure fails the workflow.

Pre-alpha tags remain prereleases and do not imply production approval. Stable versions require docs/release-readiness.json (or an explicit evidence file) with matching version/commit and passing platform, client, stability, benchmark, coverage and mutation evidence. Mutation score must be at least 90%; stopped/missing measurements fail. Real-server counts must be positive with zero skips on both platforms. Stability requires at least 60 minutes and 1,000 documents per workspace, and benchmarks need at least 20 warm samples per scenario. The Rust migration has not met these gates.

See [npm installation](npm.md) and [readiness](production-readiness.md).
