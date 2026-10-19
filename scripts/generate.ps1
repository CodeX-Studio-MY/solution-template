# On a fresh copy of the template (the .template marker is there): scaffolds the product, asking for its name and defaults.
# Afterwards: regenerates everything derived from platform.yaml and the configuration schema (see docs/adr).
#   scripts/generate.ps1 [--product <key>] [--modules a,b] [--yes] [...]    first run, on the template (see docs/getting-started.md)
#   scripts/generate.ps1 [--check] [--skip-compose] [--skip-api]           every run after that
#   scripts/generate.ps1 --template [--check] [...]                         regenerate the template itself (template maintainers)
# Native commands report problems on stderr and by exit code. 'Stop' would end the script at the first stderr line and hide
# the rest of the message, so it stays 'Continue'; the exit code is passed on.
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..') -ErrorAction Stop
$rest = @($args | Where-Object { $_ -ne '--template' })
$command = if ((Test-Path .template) -and ($args -notcontains '--template') -and ($args -notcontains '--check')) { 'init' } else { 'generate' }
dotnet run --project backend/tools/Generator -- $command @rest
exit $LASTEXITCODE
