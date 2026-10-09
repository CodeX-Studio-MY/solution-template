# Fails if any dependency needs a paid commercial license or has a license that isn't on the allow-list (see check-licenses.mjs).
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
node scripts/check-licenses.mjs
