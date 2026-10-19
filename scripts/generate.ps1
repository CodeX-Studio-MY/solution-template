# On a fresh copy of the template (the .template marker is there): scaffolds the product, asking for its name and defaults.
# Afterwards: regenerates everything derived from platform.yaml and the configuration schema (see docs/adr).
#   scripts/generate.ps1 [--product <key>] [--modules a,b] [--yes] [...]    first run, on the template (see docs/getting-started.md)
#   scripts/generate.ps1 [--check] [--skip-compose] [--skip-api]           every run after that
#   scripts/generate.ps1 --template [--check] [...]                         regenerate the template itself (template maintainers)
# Native commands report problems on stderr and by exit code. 'Stop' would end the script at the first stderr line and hide
# the rest of the message, so it stays 'Continue'; the exit code is passed on.
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..') -ErrorAction Stop
# PowerShell turns an unquoted list such as basic,pro into an array; pass it on as the comma-separated value it was typed as.
$rest = @(foreach ($arg in $args) { if ($arg -is [array]) { $arg -join ',' } elseif ($arg -ne '--template') { $arg } })
$command = if ((Test-Path .template) -and ($args -notcontains '--template') -and ($args -notcontains '--check')) { 'init' } else { 'generate' }
# The generator builds into its own folder, so building the solution never has to replace files it is running from.
dotnet run --project backend/tools/Generator "--property:OutDir=$PWD/backend/tools/Generator/bin/run/" -- $command @rest
exit $LASTEXITCODE
