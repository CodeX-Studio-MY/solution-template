# Adds a module (see scripts/new-module.sh).
#   scripts/new-module.ps1 <module-key> [--name "Display Name"] [--from-plan <plan>] [--no-frontend] [--skip-compose]
#                                        [--resume] [--no-catalog-migration]
# Native commands report problems on stderr and by exit code. 'Stop' would end the script at the first stderr line and hide
# the rest of the message, so it stays 'Continue'; the exit code is passed on.
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..') -ErrorAction Stop
# The generator builds into its own folder, so building the solution never has to replace files it is running from.
dotnet run --project backend/tools/Generator "--property:OutDir=$PWD/backend/tools/Generator/bin/run/" -- new-module @args
exit $LASTEXITCODE
