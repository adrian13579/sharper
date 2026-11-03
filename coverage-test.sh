#!/usr/bin/env bash
# Run the full suite and report in-process diagnostic service coverage.
set -euo pipefail

repo_root="$(cd "$(dirname "$0")" && pwd)"
cd "$repo_root"
dotnet tool restore
mkdir -p TestResults/coverage
coverage_run_dir="$(mktemp -d "$repo_root/TestResults/coverage/run-XXXXXX")"

dotnet test SharpLint.slnx -m:1 --collect:"XPlat Code Coverage" \
    --settings "$repo_root/coverage.runsettings" \
    --results-directory "$coverage_run_dir" "$@"

dotnet tool run reportgenerator \
    "-reports:$coverage_run_dir/**/coverage.cobertura.xml" \
    "-targetdir:$repo_root/CoverageReport" \
    "-reporttypes:Html;TextSummary;JsonSummary;Cobertura;Badges"

cat "$repo_root/CoverageReport/Summary.txt"
echo "HTML report: $repo_root/CoverageReport/index.html"
