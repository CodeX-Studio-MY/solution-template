# Regenerates everything derived from platform.yaml and the configuration schema (see docs/adr).
# Usage: scripts/generate.ps1 [--check] [--skip-compose] [--skip-api]
# Native commands report problems on stderr and by exit code. 'Stop' would end the script at the first stderr line and hide
# the rest of the message, so it stays 'Continue'; the exit code is passed on.
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..') -ErrorAction Stop
dotnet run --project backend/tools/Generator -- generate @args
exit $LASTEXITCODE
