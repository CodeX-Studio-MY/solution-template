# Adds PostgreSQL back: restores its migrations, catches them up to the current model and verifies them (see docs/adr/0011).
# Usage: scripts/add-postgresql.ps1 [--skip-compose] [--skip-api]
# Native commands report problems on stderr and by exit code. 'Stop' would end the script at the first stderr line and hide
# the rest of the message, so it stays 'Continue'; the exit code is passed on.
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..') -ErrorAction Stop
# The generator builds into its own folder, so building the solution never has to replace files it is running from.
dotnet run --project backend/tools/Generator "--property:OutDir=$PWD/backend/tools/Generator/bin/run/" -- add-postgresql @args
exit $LASTEXITCODE
