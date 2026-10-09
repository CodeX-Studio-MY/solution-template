# Adds a module (see scripts/new-module.sh).
#   scripts/new-module.ps1 <module-key> [--name "Display Name"] [--no-frontend]
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project backend/tools/Generator -- new-module @args
