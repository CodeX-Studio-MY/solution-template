# Regenerates everything derived from platform.yaml and the configuration schema (see docs/adr).
# Usage: scripts/generate.ps1 [--check] [--skip-compose]
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project backend/tools/Generator -- generate @args
exit $LASTEXITCODE
