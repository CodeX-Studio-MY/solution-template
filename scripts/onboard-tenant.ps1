# Creates another isolated tenant in the running local stack. See scripts/onboard-tenant.mjs.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
node scripts/onboard-tenant.mjs @args
