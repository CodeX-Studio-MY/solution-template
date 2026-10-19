#!/usr/bin/env sh
# Stops the stack started by scripts/dev-up: the AppHost, the service hosts and the stack's containers.
#   scripts/dev-down [--reset]    --reset also deletes the data volumes and the saved invitation links, for a clean start
set -eu
cd "$(dirname "$0")/.."
if [ -f .dev/apphost.pid ]; then
  pid=$(cat .dev/apphost.pid)
  kill "$pid" 2>/dev/null || true
  rm -f .dev/apphost.pid
fi
pkill -f "Saaskit.AppHost" 2>/dev/null || true

# Without a graceful shutdown Aspire doesn't remove its containers either. The stack's containers are the ones made by the same
# Aspire process as those using this product's data volumes (that also finds RabbitMQ and Mailpit, which have no volume).
label=com.microsoft.developer.usvc-dev.creatorProcessId
volumes=$(docker volume ls -q | grep '^saaskit-' || true)
creators=$(for volume in $volumes; do docker ps -a -q --filter "volume=$volume"; done | sort -u |
  while read -r id; do [ -n "$id" ] && docker inspect -f "{{index .Config.Labels \"$label\"}}" "$id"; done | sort -u)
for creator in $creators; do
  docker ps -a -q --filter "label=$label=$creator" | xargs -r docker rm -f >/dev/null 2>&1 || true
done

if [ "${1:-}" = "--reset" ]; then
  for volume in $volumes; do docker volume rm "$volume" >/dev/null 2>&1 || true; done
  rm -f .dev/invitations.json .dev/invitations.json.done .dev/operator.json
  left=$(docker volume ls -q | grep '^saaskit-' || true)
  if [ -n "$left" ]; then
    echo "Stopped, but these volumes are still in use and were kept: $left. Stop what uses them (docker ps) and run this again." >&2
    exit 1
  fi
  echo "Stopped and reset."
else
  echo "Stopped."
fi
