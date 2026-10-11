#!/usr/bin/env sh
# Starts the whole stack locally with the Aspire AppHost, onboards the two demo tenants and prints how to sign in.
#
#   scripts/dev-up [--provider sqlserver|postgresql] [--all-in-one] [--no-build]
#
# It: makes a locally trusted HTTPS certificate for localhost and *.localhost (mkcert), builds the frontend, starts the AppHost
# (database, Valkey, RabbitMQ, Azurite, Mailpit, every service, the Gateway, the Scheduler), runs the migrations, onboards `demo`
# (Standard plan) and `demo-plus` (Plus plan, customised), seeds a few purchase requests, cost centers and budgets, and prints each
# tenant's admin invitation link. Run scripts/dev-down to stop. Everything it writes is in .dev/ (git-ignored).
set -eu
cd "$(dirname "$0")/.."

provider=sqlserver
layout=services
build=1
while [ $# -gt 0 ]; do
  case "$1" in
    --provider) provider="$2"; shift 2 ;;
    --all-in-one) layout=all-in-one; shift ;;
    --no-build) build=0; shift ;;
    -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
    *) echo "Unknown option $1 (see --help)" >&2; exit 2 ;;
  esac
done
case "$provider" in sqlserver|postgresql) ;; *) echo "--provider must be sqlserver or postgresql" >&2; exit 2 ;; esac
if [ "$provider" = postgresql ] && ! grep -Eq '^[[:space:]]*providers:.*postgresql' "$(dirname "$0")/../platform.yaml"; then
  echo "PostgreSQL migrations are not in this build (platform.yaml lists only sqlserver). Run scripts/add-postgresql first." >&2; exit 2
fi

need() { command -v "$1" >/dev/null 2>&1 || { echo "$1 is required. $2" >&2; exit 1; }; }
need dotnet "Install the .NET SDK (see global.json)."
need docker "Install Docker Desktop (on Apple Silicon, enable Rosetta emulation for SQL Server)."
need node "Install Node.js 24 or later."
need curl "Install curl."
if ! command -v pnpm >/dev/null 2>&1; then
  need corepack 'Run "npm install -g corepack" (Node.js 25 and later no longer include it).'
  echo "Enabling pnpm with corepack..."
  mkdir -p "$HOME/.local/bin"
  corepack enable pnpm --install-directory "$HOME/.local/bin"
  PATH="$HOME/.local/bin:$PATH"
fi
if ! command -v mkcert >/dev/null 2>&1; then
  if command -v brew >/dev/null 2>&1; then echo "Installing mkcert with Homebrew..."; brew install mkcert
  else echo "mkcert is required (https://github.com/FiloSottile/mkcert). Install it and run this again." >&2; exit 1; fi
fi

mkdir -p .dev/certs
root=$(pwd)

# __Host- cookies need HTTPS, so local development serves the Gateway over TLS with a certificate for localhost and every tenant host.
if [ ! -f .dev/certs/localhost.pem ] || [ ! -f .dev/certs/localhost-key.pem ]; then
  mkcert -cert-file .dev/certs/localhost.pem -key-file .dev/certs/localhost-key.pem localhost "*.localhost" 127.0.0.1 ::1
fi
if [ -t 0 ] && ! mkcert -check >/dev/null 2>&1; then
  echo "Trusting the local certificate authority (mkcert asks for your password once)..."
  mkcert -install || true
fi
if ! mkcert -check >/dev/null 2>&1; then
  echo "Note: browsers won't trust the certificate until you run: mkcert -install"
fi

if [ "$build" = 1 ]; then
  echo "Building the frontend..."
  (cd frontend && pnpm install --frozen-lockfile && pnpm build)
fi

rm -f .dev/invitations.json.done
echo "Starting the stack ($provider, $layout)... logs: .dev/apphost.log"
Database__Provider="$provider" AppHost__Layout="$layout" AppHost__Seed=true \
  Dev__CertificatePath="$root/.dev/certs/localhost.pem" Dev__CertificateKeyPath="$root/.dev/certs/localhost-key.pem" \
  Dev__FrontendPath="$root/frontend/dist" DevSeed__InvitationsFile="$root/.dev/invitations.json" \
  nohup dotnet run --project backend/src/AppHost --launch-profile http > .dev/apphost.log 2>&1 &
echo $! > .dev/apphost.pid

echo "Waiting for the Gateway (the first start pulls images and can take several minutes)..."
n=0
until curl -sk -m 5 -f -o /dev/null https://localhost:7443/health/ready; do
  n=$((n + 1)); [ $n -gt 240 ] && { echo "The Gateway did not become ready. See .dev/apphost.log" >&2; exit 1; }
  sleep 5
done
echo "Waiting for the demo tenants..."
n=0
until [ -f .dev/invitations.json.done ]; do
  n=$((n + 1)); [ $n -gt 120 ] && { echo "Seeding did not finish. See the dev-seed logs in the Aspire dashboard (http://localhost:15100)." >&2; exit 1; }
  sleep 5
done

cat <<'DONE'

The stack is up.

  demo       Standard plan, defaults              https://demo.localhost:7443/
  demo-plus  Plus plan, customised                https://demo-plus.localhost:7443/

DONE
if [ -f .dev/invitations.json ]; then
  echo "Each tenant's first admin accepts an invitation (open the link, choose a password):"
  sed -n 's/^ *"\([^"]*\)": *"\([^"]*\)".*/  \1: \2/p' .dev/invitations.json
else
  echo "The tenants already existed; their invitation links were printed the first time (.dev/invitations.json)."
fi
cat <<'DONE'

  Aspire dashboard (logs, traces, metrics)         http://localhost:15100
  Mailpit (every email the stack sends)            http://localhost:8025
  Stop everything                                  scripts/dev-down
DONE
