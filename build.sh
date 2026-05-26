#!/usr/bin/env bash
# Builds the solution and produces the local NuGet packages
# (SharpLint.Core, SharpLint.Configuration) into ./packages.
#
# The package version is fixed at 1.0.0-dev, so NuGet keeps an extracted copy
# in the global cache and won't notice rebuilt-but-same-version content. This
# script evicts those cached copies first, guaranteeing that a following
# `sharplint check` (which restores the config's package) sees fresh bits.
#
# Extra args are forwarded to `dotnet build`, e.g. ./build.sh -c Release
set -euo pipefail

cd "$(dirname "$0")"

echo "Evicting cached SharpLint packages..."
rm -rf "${NUGET_PACKAGES:-$HOME/.nuget/packages}/sharplint.core" \
       "${NUGET_PACKAGES:-$HOME/.nuget/packages}/sharplint.configuration"

echo "Building solution and packing..."
dotnet build SharpLint.slnx "$@"

echo
echo "Packages in ./packages:"
ls -1 packages/*.nupkg
