#!/usr/bin/env bash
# Build the shared diagnostic service, CLI/MCP host and tests.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build SharpLint.slnx -m:1 "$@"
