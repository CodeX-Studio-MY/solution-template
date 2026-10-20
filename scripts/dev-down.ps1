# Stops the stack started by scripts/dev-up.ps1: the AppHost, the service hosts and the stack's containers.
#   scripts/dev-down.ps1 [-Reset]    -Reset also deletes the data volumes and the saved invitation links, for a clean start
param([switch]$Reset)
Set-Location (Join-Path $PSScriptRoot '..')
if (Test-Path .dev/apphost.pid) { Stop-Process -Id (Get-Content .dev/apphost.pid) -ErrorAction SilentlyContinue; Remove-Item .dev/apphost.pid }
# Stop-Process ends the AppHost without a graceful shutdown, so the service hosts it started are stopped as well.
Get-Process Farm360.* -ErrorAction SilentlyContinue | Stop-Process -ErrorAction SilentlyContinue

# Without a graceful shutdown Aspire doesn't remove its containers either. The stack's containers are the ones made by the same
# Aspire process as those using this product's data volumes (that also finds RabbitMQ and Mailpit, which have no volume).
$creatorLabel = 'com.microsoft.developer.usvc-dev.creatorProcessId'
$volumes = @(docker volume ls -q | Where-Object { $_ -like 'farm360-*' })
$creators = @($volumes | ForEach-Object { docker ps -a -q --filter "volume=$_" } | Where-Object { $_ } |
  ForEach-Object { docker inspect -f "{{index .Config.Labels \`"$creatorLabel\`"}}" $_ } | Where-Object { $_ } | Sort-Object -Unique)
foreach ($creator in $creators) {
  docker ps -a -q --filter "label=$creatorLabel=$creator" | ForEach-Object { docker rm -f $_ *> $null }
}

if ($Reset) {
  foreach ($volume in $volumes) { docker volume rm $volume *> $null }
  Remove-Item .dev/invitations.json, .dev/invitations.json.done, .dev/operator.json -ErrorAction SilentlyContinue
  $left = @(docker volume ls -q | Where-Object { $_ -like 'farm360-*' })
  if ($left.Count -gt 0) {
    Write-Host "Stopped, but these volumes are still in use and were kept: $($left -join ', '). Stop what uses them (docker ps) and run this again."
    exit 1
  }
  Write-Host 'Stopped and reset.'
} else {
  Write-Host 'Stopped.'
}
