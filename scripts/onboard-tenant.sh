#!/usr/bin/env sh
# Creates a second (third, ...) isolated tenant in the running local stack. See scripts/onboard-tenant.mjs.
#   scripts/onboard-tenant <slug> [--plan standard|plus] [--name "Display Name"] [--admin-email you@example.com]
set -eu
cd "$(dirname "$0")/.."
exec node scripts/onboard-tenant.mjs "$@"
