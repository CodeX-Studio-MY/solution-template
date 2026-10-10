# Stops the stack started by scripts/dev-up.ps1.
#   scripts/dev-down.ps1 [-Reset]    -Reset also deletes the data volumes and the saved invitation links, for a clean start
param([switch]$Reset)
Set-Location (Join-Path $PSScriptRoot '..')
if (Test-Path .dev/apphost.pid) { Stop-Process -Id (Get-Content .dev/apphost.pid) -ErrorAction SilentlyContinue; Remove-Item .dev/apphost.pid }
# Stop-Process ends the AppHost without a graceful shutdown, so the service hosts it started are stopped as well.
Get-Process Acme.* -ErrorAction SilentlyContinue | Stop-Process -ErrorAction SilentlyContinue
if ($Reset) {
  Start-Sleep 5
  docker volume ls -q | Where-Object { $_ -like 'acme-*' } | ForEach-Object { docker volume rm $_ *> $null }
  Remove-Item .dev/invitations.json, .dev/invitations.json.done, .dev/operator.json -ErrorAction SilentlyContinue
  Write-Host 'Stopped and reset.'
} else {
  Write-Host 'Stopped.'
}
