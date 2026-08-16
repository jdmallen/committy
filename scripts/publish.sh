#!/usr/bin/env bash
# Thin wrapper around the shared JDMallen publish engine (publish-dotnet.sh,
# vendored from https://github.com/jdmallen/toolbox). All arguments (runtimes,
# -v/--version, -h/--help) are handled by the engine.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

project="src/Committy/Committy.csproj"
exe_name="committy"
default_runtimes=(linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64)

# Single source of truth for the version is <Version> in Directory.Build.props (it
# applies to every project, including tests), not the csproj. Pre-set `version` so
# the engine uses it instead of reading the csproj; a -v/--version arg still wins.
version="$(grep -oP '(?<=<Version>)[^<]+' "$repo_root/Directory.Build.props" | head -n1 || true)"

source "$repo_root/scripts/publish-dotnet.sh"
