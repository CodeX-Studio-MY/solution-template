#!/usr/bin/env sh
# Adds PostgreSQL back: restores its migrations, catches them up to the current model and verifies them (see docs/adr/0011).
# Usage: scripts/add-postgresql.sh [--skip-compose] [--skip-api]
set -eu
cd "$(dirname "$0")/.."
exec dotnet run --project backend/tools/Generator -- add-postgresql "$@"
