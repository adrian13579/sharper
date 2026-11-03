#!/usr/bin/env bash
# Measure the diagnostic service; CLI and real-server subprocess tests run separately.
set -euo pipefail
repo_root="$(cd "$(dirname "$0")" && pwd)"
cd "$repo_root"
dotnet tool restore
dotnet build SharpLint.slnx -m:1
cd tests/SharpLint.Tests
dotnet stryker --output "$repo_root/StrykerOutput/Analysis" "$@"
