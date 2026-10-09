#!/usr/bin/env sh
# Adds a module: the entries in platform.yaml, the backend (module, host, migrations for every database provider, tests) and the
# frontend module from the clean templates, then generates everything that follows (constants, gateway route, AppHost, compose, CI
# workflow, API client). No other file needs a manual edit.
#
#   scripts/new-module <module-key> [--name "Display Name"] [--no-frontend]
set -eu
cd "$(dirname "$0")/.."
PATH="$HOME/.local/bin:$PATH"
exec dotnet run --project backend/tools/Generator -- new-module "$@"
