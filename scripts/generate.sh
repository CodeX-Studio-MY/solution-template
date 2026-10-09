#!/usr/bin/env sh
# Regenerates everything derived from platform.yaml and the configuration schema (see docs/adr).
# Usage: scripts/generate.sh [--check] [--skip-compose] [--skip-api]
set -eu
cd "$(dirname "$0")/.."
exec dotnet run --project backend/tools/Generator -- generate "$@"
