#!/usr/bin/env sh
# Fails if any dependency needs a paid commercial license or has a license that isn't on the allow-list (see check-licenses.mjs).
# Run after `dotnet restore` and `pnpm install`.
set -eu
cd "$(dirname "$0")/.."
exec node scripts/check-licenses.mjs
