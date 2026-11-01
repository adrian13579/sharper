# Daemon benchmarks

Build Debug, then run from the repository root:

```sh
dotnet build SharpLint.slnx -m:1
python3 benchmarks/daemon.py --sizes 100 1000 10000 --iterations 10 --warmup 3
```

Python 3 is the only additional dependency. Fixtures and an isolated package cache
live in a temporary directory. Every CLI invocation runs as a new process; timing
uses `time.perf_counter` around process launch through complete output collection.
All spawned fixture daemons are stopped in `finally` blocks. Use `--output` to
preserve a separate JSON run instead of replacing the checked-in sample.

Scenarios cover cold automatic startup/initial configuration/workspace, unchanged
warm checks, one changed file, ten changed files, changed rule configuration,
changed project/build inputs, four concurrent CLI clients, and explicit daemon
stop/start/first check. Each size has one extra source document: its executable
configuration. Configuration changes alternate between compliant and violating
prefix requirements. Project-structure tests change Directory.Build.props,
causing MSBuild reloads and conservative compiler-cache invalidation.

Cold and restart runs use a fresh process and workspace per sample. Cold checks
include any initial restore cost; later samples may reuse restored packages and
.NET file-app build products. Warm checks follow three untimed warmups. Restart
timing includes the stop command, explicit start command and subsequent check.
Concurrent latency is measured per client, including coordinator queue time.
P95 uses the nearest-rank estimator. Peak memory is the daemon process lifetime
`/proc/<pid>/status` VmHWM on Linux (null on other platforms), excluding CLI and
configuration subprocesses. It is cumulative within a daemon lifetime, not an
isolated allocation delta or a combined process-tree peak.

## Historical syntax-only sample

This baseline predates compiler integration. Recorded on 2026-10-31 with .NET SDK 10.0.202, Debug, Linux/WSL2 x86_64
(`6.18.54.1-microsoft-standard-WSL2`), 20 logical CPUs, Python 3.10.12.
Five measured samples per scenario, three warmup checks per size. This is a
shared development host. These measurements
are a reproducible baseline, not statistically precise guarantees. The full
metrics and environment are in [results.json](results.json).

| Files | Scenario | Median ms | P95 ms | Daemon peak MiB | Cache hit % | Analyzed | Reused |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 100 | cold | 1319.03 | 4443.11 | 109.7 | 0.00 | 101 | 0 |
| 100 | warm | 225.01 | 245.79 | 118.0 | 100.00 | 0 | 101 |
| 100 | single-file | 498.58 | 539.95 | 121.4 | 99.01 | 1 | 100 |
| 100 | multiple-files | 511.02 | 517.75 | 123.2 | 90.10 | 10 | 91 |
| 100 | configuration | 551.87 | 561.15 | 124.4 | 0.00 | 101 | 0 |
| 100 | project-structure | 1544.23 | 1615.75 | 124.6 | 0.00 | 101 | 0 |
| 100 | concurrent-clients | 205.52 | 206.17 | 124.7 | 100.00 | 0 | 101 |
| 100 | restart | 1718.13 | 1747.75 | 109.2 | 0.00 | 101 | 0 |
| 1,000 | cold | 1410.13 | 2837.15 | 118.6 | 0.00 | 1001 | 0 |
| 1,000 | warm | 223.93 | 236.70 | 132.8 | 100.00 | 0 | 1001 |
| 1,000 | single-file | 517.41 | 531.78 | 132.3 | 99.90 | 1 | 1000 |
| 1,000 | multiple-files | 518.50 | 536.80 | 136.1 | 99.00 | 10 | 991 |
| 1,000 | configuration | 599.69 | 616.72 | 142.3 | 0.00 | 1001 | 0 |
| 1,000 | project-structure | 1698.73 | 1720.65 | 145.7 | 0.00 | 1001 | 0 |
| 1,000 | concurrent-clients | 277.62 | 313.90 | 147.9 | 100.00 | 0 | 1001 |
| 1,000 | restart | 1815.64 | 1839.24 | 120.4 | 0.00 | 1001 | 0 |
| 10,000 | cold | 2280.98 | 3918.04 | 158.8 | 0.00 | 10001 | 0 |
| 10,000 | warm | 410.50 | 452.56 | 205.8 | 100.00 | 0 | 10001 |
| 10,000 | single-file | 694.05 | 702.19 | 210.1 | 99.99 | 1 | 10000 |
| 10,000 | multiple-files | 684.08 | 694.84 | 210.9 | 99.90 | 10 | 9991 |
| 10,000 | configuration | 984.29 | 1018.27 | 242.7 | 0.00 | 10001 | 0 |
| 10,000 | project-structure | 2102.59 | 2163.08 | 274.9 | 0.00 | 10001 | 0 |
| 10,000 | concurrent-clients | 782.55 | 1143.37 | 310.8 | 100.00 | 0 | 10001 |
| 10,000 | restart | 3036.28 | 3077.49 | 175.9 | 0.00 | 10001 | 0 |

The preliminary <50 ms warm and <100 ms single-file end-to-end goals were **not
met**. Unchanged checks reuse every Naming result, and one-file edits analyze
exactly one document. Warm CLI startup/handshake/serialization and two complete
content observations still cost time; reconciliation scales with file count.
The conservative executable-config reload on edits adds process startup even
when the emitted rule configuration stays unchanged. Project/build changes also
trigger file-app rebuilds. JSON output cost grows when configuration changes
produce thousands of diagnostics. Semantic-rule latency is not measured because
the current host supplies syntax inputs only.

Next performance work should isolate Release process startup from daemon phase
timings, consider a declarative configuration cache with explicit dependency
tracking, reduce content-capture allocation/I/O while preserving missed-event
correctness, and evaluate sharing pending validation work for concurrent clients.
Do not replace content validation with timestamp-only or watcher-only reuse.
Compiler integration adds the project-loading and compilation costs described below;
broader scaling measurements remain follow-up work.

## Compiler-enabled sample

[compiler-results.json](compiler-results.json) records a 100-file run with three
samples and one warmup, using Roslyn 5.3 and the isolated compiler-feature snapshot.
Projects are restored before checks; executable-configuration restore can still
affect cold checks. The [engineering report](../docs/compiler-integration-results.md)
compares all scenarios and documents the validation boundary.

Warm median was 311.62 ms with every compiler group/document reused. One-file
edits took 576.60 ms median and analyzed one document and one project. Cold median
was 4802.32 ms; project reload median was 3095.37 ms. These small shared-host samples
have different restore/cache histories from the historical baseline. Project load,
dependency discovery and immutable metadata capture add cost; latency goals remain
unmet. Project counts now accompany document counts in each result.
