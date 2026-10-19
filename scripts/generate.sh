#!/usr/bin/env sh
# On a fresh copy of the template (the .template marker is there): scaffolds the product, asking for its name and defaults.
# Afterwards: regenerates everything derived from platform.yaml and the configuration schema (see docs/adr).
#   scripts/generate.sh [--product <key>] [--modules a,b] [--yes] [...]    first run, on the template (see docs/getting-started.md)
#   scripts/generate.sh [--check] [--skip-compose] [--skip-api]           every run after that
#   scripts/generate.sh --template [--check] [...]                         regenerate the template itself (template maintainers)
set -eu
cd "$(dirname "$0")/.."
PATH="$HOME/.local/bin:$PATH"
command=generate
if [ -f .template ]; then
  command=init
  for arg in "$@"; do
    case "$arg" in --template | --check) command=generate ;; esac
  done
fi
# Drop --template: it only chooses the command.
for arg in "$@"; do
  shift
  [ "$arg" = "--template" ] || set -- "$@" "$arg"
done
exec dotnet run --project backend/tools/Generator -- "$command" "$@"
