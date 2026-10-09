# Stops the stack started by scripts/dev-up.ps1.
Set-Location (Join-Path $PSScriptRoot '..')
if (Test-Path .dev/apphost.pid) { Stop-Process -Id (Get-Content .dev/apphost.pid) -ErrorAction SilentlyContinue; Remove-Item .dev/apphost.pid }
Get-Process Acme.AppHost -ErrorAction SilentlyContinue | Stop-Process
Write-Host 'Stopped.'
