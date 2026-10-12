# Creates another isolated tenant in the running local stack. See scripts/onboard-tenant.mjs.
# Native commands report problems on stderr and by exit code. 'Stop' would end the script at the first stderr line and hide
# the rest of the message, so it stays 'Continue'; the exit code is passed on.
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..') -ErrorAction Stop
node scripts/onboard-tenant.mjs @args
exit $LASTEXITCODE
