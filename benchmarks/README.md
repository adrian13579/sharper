# Performance evidence

The JSON results in this directory are historical .NET measurements. They do not describe the Rust implementation. No Rust latency, throughput, memory or stability claim is established by them.

Use the Rust benchmark example with an explicit LSP profile and workspace to collect cold/warm samples. Record sample counts, median/P95, statuses, tool versions, hardware/runtime context and startup behavior. Run on an idle validation environment, separately from builds/tests. Representative workspace/client and soak qualification remains a production-readiness gap.
