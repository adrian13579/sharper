#!/usr/bin/env python3
"""End-to-end CLI benchmark. Build Debug first; writes its fixtures only to a temporary directory."""
import argparse
import concurrent.futures
import json
import math
import os
from pathlib import Path
import platform
import re
import statistics
import subprocess
import tempfile
import time

REPO = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--sizes", type=int, nargs="+", default=[100, 1000, 10000])
parser.add_argument("--iterations", type=int, default=10)
parser.add_argument("--warmup", type=int, default=3)
parser.add_argument("--output", type=Path, default=REPO / "benchmarks" / "results.json")
args = parser.parse_args()
if args.iterations < 2 or args.warmup < 1 or any(size < 1 for size in args.sizes):
    parser.error("Use at least two iterations, one warmup, and positive file counts")
cli = REPO / "src/SharpLint.Cli/bin/Debug/net10.0/sharplint.dll"
if not cli.exists():
    parser.error("Build SharpLint.slnx in Debug first")
report = {"environment": {"platform": platform.platform(), "python": platform.python_version(),
    "dotnet": subprocess.check_output(["dotnet", "--version"], text=True).strip(),
    "cpus": os.cpu_count(), "iterations": args.iterations, "warmup": args.warmup,
    "configuration": "Debug", "sourceTemplate": "namespace Bench; public class Expected{index} { public void DoWork() { } }"}, "results": []}

with tempfile.TemporaryDirectory(prefix="sharplint-benchmark-") as temporary:
    base = Path(temporary)
    environment = dict(os.environ, NUGET_PACKAGES=str(base / "nuget-cache"), NO_COLOR="1")
    for size in args.sizes:
        root = base / str(size)
        root.mkdir()
        (root / "global.json").write_text((REPO / "global.json").read_text())
        (root / "NuGet.config").write_text(f'''<configuration><packageSources><clear />
<add key="local" value="{REPO / 'packages'}" />
<add key="nuget" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>''')
        (root / "Benchmark.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        subprocess.run(["dotnet", "restore", str(root / "Benchmark.csproj"), "--ignore-failed-sources"],
            cwd=root, env=environment, check=True, capture_output=True, text=True, timeout=300)
        def config(prefix):
            (root / "sharplint.config.cs").write_text(f'''#:package SharpLint.Configuration@1.0.0-dev
using SharpLint.Configuration;
Lint.Configure(c => c.Naming(n => n.Classes().RequirePrefix("{prefix}").AsError())).Apply();
''')
        config("Expected")
        for i in range(size):
            (root / f"Source{i:05}.cs").write_text(f"namespace Bench; public class Expected{i} {{ public void DoWork() {{ }} }}\n")
        def run(*command):
            started = time.perf_counter()
            process = subprocess.run(["dotnet", str(cli), *command], cwd=root, env=environment,
                capture_output=True, text=True, timeout=300)
            elapsed = (time.perf_counter() - started) * 1000
            if process.returncode not in (0, 1):
                raise RuntimeError(process.stderr + process.stdout)
            return elapsed, process.stdout
        def check():
            elapsed, output = run("check", "--stats", "--format", "json")
            return elapsed, json.loads(output)["statistics"]
        def memory():
            _, output = run("daemon", "status")
            match = re.search(r"PID (\d+)", output)
            if not match:
                return None
            status = Path(f"/proc/{match[1]}/status")
            if status.exists():
                peak = re.search(r"VmHWM:\s+(\d+)", status.read_text())
                return int(peak[1]) / 1024 if peak else None
            return None
        def record(scenario, values):
            latencies = sorted(item[0] for item in values)
            data = [item[1] for item in values]
            row = {"files": size, "scenario": scenario, "samples": len(values),
                "medianMs": round(statistics.median(latencies), 2),
                "p95Ms": round(latencies[math.ceil(len(latencies) * .95) - 1], 2),
                "daemonPeakMiB": memory(),
                "cacheHitRate": statistics.mean(x["cacheHits"] / max(1, x["documentsAnalyzed"] + x["documentsReused"]) for x in data),
                "documentsAnalyzed": statistics.mean(x["documentsAnalyzed"] for x in data),
                "documentsReused": statistics.mean(x["documentsReused"] for x in data),
                "projectsAnalyzed": statistics.mean(x["projectsAnalyzed"] for x in data),
                "projectsReused": statistics.mean(x["projectsReused"] for x in data)}
            report["results"].append(row)
            print(json.dumps(row), flush=True)
        try:
            # Cold includes auto-start, workspace creation, config execution and any first restore cost.
            cold = []
            for _ in range(args.iterations):
                run("daemon", "stop")
                cold.append(check())
            record("cold", cold)
            for _ in range(args.warmup):
                check()
            record("warm", [check() for _ in range(args.iterations)])
            for scenario, count in (("single-file", 1), ("multiple-files", min(10, size))):
                values = []
                for iteration in range(args.iterations):
                    for i in range(count):
                        (root / f"Source{i:05}.cs").write_text(f"namespace Bench; public class Expected{i} {{ public void DoWork{iteration}() {{ }} }}\n")
                    values.append(check())
                record(scenario, values)
            values = []
            for iteration in range(args.iterations):
                config("Other" if iteration % 2 == 0 else "Expected")
                values.append(check())
            record("configuration", values)
            values = []
            for iteration in range(args.iterations):
                (root / "Directory.Build.props").write_text(f"<Project><!-- {iteration} --></Project>")
                values.append(check())
            record("project-structure", values)
            with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
                record("concurrent-clients", list(pool.map(lambda _: check(), range(args.iterations))))
            values = []
            for _ in range(args.iterations):
                started = time.perf_counter()
                run("daemon", "stop")
                run("daemon", "start")
                _, stats = check()
                values.append(((time.perf_counter() - started) * 1000, stats))
            record("restart", values)
        finally:
            run("daemon", "stop")
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, indent=2) + "\n")
print(f"Results: {args.output}")
