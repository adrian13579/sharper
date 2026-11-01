#!/usr/bin/env bash
# Publish the native CLI with its managed daemon, then exercise the native commands.
set -euo pipefail

cd "$(dirname "$0")"
aot_rid="${1:-$(dotnet --info | awk '/RID:/ { print $2; exit }')}"
aot_output="$PWD/artifacts/aot-test/$aot_rid"
aot_executable="$aot_output/sharplint"
if [[ "$aot_rid" == win-* ]]; then aot_executable+=".exe"; fi

dotnet build SharpLint.slnx -m:1
dotnet publish src/SharpLint.Cli -c Release -r "$aot_rid" -o "$aot_output" -m:1
# Analysis still needs the managed host and the nested MSBuild helper processes.
test -f "$aot_output/daemon/SharpLint.Daemon.dll"
test -f "$aot_output/daemon/SharpLint.Daemon.runtimeconfig.json"
test -f "$aot_output/daemon/BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll"
SHARPLINT_TEST_EXECUTABLE="$aot_executable" dotnet test tests/SharpLint.Tests/SharpLint.Tests.csproj \
    --no-build --no-restore --filter 'FullyQualifiedName~SharpLint.Tests.Cli'
