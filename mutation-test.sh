#!/usr/bin/env bash
# Measure each library separately; Stryker mutates one referenced project per run.
set -euo pipefail

repo_root="$(cd "$(dirname "$0")" && pwd)"
target="${1:-all}"
case "$target" in
    all) projects=(Core Configuration) ;;
    Core|Configuration) projects=("$target") ;;
    *) echo "Usage: $0 [all|Core|Configuration]" >&2; exit 2 ;;
esac
if (( $# > 0 )); then shift; fi

cd "$repo_root"
dotnet tool restore
dotnet build SharpLint.slnx -m:1
cd tests/SharpLint.Tests
for project in "${projects[@]}"; do
    dotnet stryker --project "SharpLint.$project.csproj" \
        --output "$repo_root/StrykerOutput/$project" "$@"
done
