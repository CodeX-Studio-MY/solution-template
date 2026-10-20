#!/usr/bin/env sh
# Adds a module: the entries in platform.yaml, the backend (module, host, migrations for every database provider, tests) and the
# frontend module from the clean templates, then generates everything that follows (constants, gateway route, AppHost, compose, API
# client). No other file needs a manual edit.
#
#   scripts/new-module <module-key> [--name "Display Name"] [--no-frontend] [--skip-compose]
set -eu
cd "$(dirname "$0")/.."
PATH="$HOME/.local/bin:$PATH"
# The generator builds into its own folder, so building the solution never has to replace files it is running from.
exec dotnet run --project backend/tools/Generator "--property:OutDir=$(pwd)/backend/tools/Generator/bin/run/" -- new-module "$@"
