#!/usr/bin/env bash
# Build the service and retained compatibility libraries without evicting user package caches.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p packages
dotnet build SharpLint.slnx -m:1 "$@"
