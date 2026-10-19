#!/usr/bin/env sh
# Adds PostgreSQL back: restores its migrations, catches them up to the current model and verifies them (see docs/adr/0011).
# Usage: scripts/add-postgresql.sh [--skip-compose] [--skip-api]
set -eu
cd "$(dirname "$0")/.."
# The generator builds into its own folder, so building the solution never has to replace files it is running from.
exec dotnet run --project backend/tools/Generator "--property:OutDir=$(pwd)/backend/tools/Generator/bin/run/" -- add-postgresql "$@"
