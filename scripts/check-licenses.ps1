# Fails if any dependency needs a paid commercial license or has a license that isn't on the allow-list (see check-licenses.mjs).
# Native commands report problems on stderr and by exit code. 'Stop' would end the script at the first stderr line and hide
# the rest of the message, so it stays 'Continue'; the exit code is passed on.
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..') -ErrorAction Stop
node scripts/check-licenses.mjs
exit $LASTEXITCODE
