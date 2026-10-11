# Adds PostgreSQL back: restores its migrations, catches them up to the current model and verifies them (see docs/adr/0011).
# Usage: scripts/add-postgresql.ps1 [--skip-compose] [--skip-api]
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project backend/tools/Generator -- add-postgresql @args
exit $LASTEXITCODE
