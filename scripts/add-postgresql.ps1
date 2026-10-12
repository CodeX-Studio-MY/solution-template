# Adds PostgreSQL back: restores its migrations, catches them up to the current model and verifies them (see docs/adr/0011).
# Usage: scripts/add-postgresql.ps1 [--skip-compose] [--skip-api]
# Native commands report problems on stderr and by exit code. 'Stop' would end the script at the first stderr line and hide
# the rest of the message, so it stays 'Continue'; the exit code is passed on.
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..') -ErrorAction Stop
dotnet run --project backend/tools/Generator -- add-postgresql @args
exit $LASTEXITCODE
