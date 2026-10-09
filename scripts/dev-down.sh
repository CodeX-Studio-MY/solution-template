#!/usr/bin/env sh
# Stops the stack started by scripts/dev-up (the AppHost stops its containers too).
#   scripts/dev-down [--reset]    --reset also deletes the data volumes and the saved invitation links, for a clean start
set -eu
cd "$(dirname "$0")/.."
if [ -f .dev/apphost.pid ]; then
  pid=$(cat .dev/apphost.pid)
  kill "$pid" 2>/dev/null || true
  rm -f .dev/apphost.pid
fi
pkill -f "Acme.AppHost" 2>/dev/null || true
if [ "${1:-}" = "--reset" ]; then
  sleep 5
  docker volume ls -q | grep '^acme-' | xargs -r docker volume rm >/dev/null 2>&1 || true
  rm -f .dev/invitations.json .dev/invitations.json.done .dev/operator.json
  echo "Stopped and reset."
else
  echo "Stopped."
fi
