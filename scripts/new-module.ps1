# Adds a module (see scripts/new-module.sh).
#   scripts/new-module.ps1 <module-key> [--name "Display Name"] [--no-frontend]
# Native commands report problems on stderr and by exit code. 'Stop' would end the script at the first stderr line and hide
# the rest of the message, so it stays 'Continue'; the exit code is passed on.
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..') -ErrorAction Stop
dotnet run --project backend/tools/Generator -- new-module @args
exit $LASTEXITCODE
