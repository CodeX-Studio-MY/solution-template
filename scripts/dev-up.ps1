# Starts the whole stack locally with the Aspire AppHost, onboards the demo tenant and prints how to sign in.
#   scripts/dev-up.ps1 [-Provider sqlserver|postgresql] [-AllInOne] [-NoBuild]
param(
  [ValidateSet('sqlserver', 'postgresql')][string]$Provider = 'sqlserver',
  [switch]$AllInOne,
  [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

# Programs such as mkcert and pnpm write progress and notices to stderr. Windows PowerShell 5.1 turns that into a terminating error
# when the output is redirected and $ErrorActionPreference is 'Stop', so native commands run with it relaxed; their exit code is
# still in $LASTEXITCODE for the caller to check.
function Invoke-Native {
  param([Parameter(Mandatory)][scriptblock]$Command)
  $previous = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    & $Command 2>&1 | ForEach-Object {
      # An error record's own message, so an empty stderr line is blank and not the name of its exception type.
      Write-Host ($(if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.Exception.Message } else { "$_" }))
    }
  } finally { $ErrorActionPreference = $previous }
}
if ($Provider -eq 'postgresql' -and -not (Select-String -Path platform.yaml -Pattern '^\s*providers:.*postgresql' -Quiet)) {
  throw 'PostgreSQL migrations are not in this build (platform.yaml lists only sqlserver). Run scripts/add-postgresql.ps1 first.'
}
foreach ($tool in 'dotnet', 'docker', 'node', 'mkcert') {
  if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is required." }
}
if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
  # Node.js 25 and later no longer ship corepack.
  if (-not (Get-Command corepack -ErrorAction SilentlyContinue)) { throw 'pnpm is required: run "npm install -g corepack", then "corepack enable pnpm".' }
  Invoke-Native { corepack enable pnpm }
}

New-Item -ItemType Directory -Force .dev/certs | Out-Null
$root = (Get-Location).Path
if (-not (Test-Path .dev/certs/localhost.pem)) {
  Invoke-Native { mkcert -cert-file .dev/certs/localhost.pem -key-file .dev/certs/localhost-key.pem localhost '*.localhost' 127.0.0.1 ::1 }
}
# mkcert has no "is it trusted?" command (there is no -check); -install does nothing when the CA is already in the trust store.
# Windows asks once to add the local certificate authority to your trusted roots: answer Yes.
Invoke-Native { mkcert -install }
if ($LASTEXITCODE -ne 0) { Write-Host 'The local certificate authority is not trusted yet: browsers will warn until "mkcert -install" succeeds.' }

if (-not $NoBuild) {
  Push-Location frontend
  try {
    Invoke-Native { pnpm install --frozen-lockfile }
    if ($LASTEXITCODE -ne 0) { throw 'pnpm install failed.' }
    Invoke-Native { pnpm build }
    if ($LASTEXITCODE -ne 0) { throw 'The frontend build failed.' }
  } finally { Pop-Location }
}

Remove-Item .dev/invitations.json.done -ErrorAction SilentlyContinue
$env:Database__Provider = $Provider
$env:AppHost__Layout = if ($AllInOne) { 'all-in-one' } else { 'services' }
$env:AppHost__Seed = 'true'
$env:Dev__CertificatePath = "$root/.dev/certs/localhost.pem"
$env:Dev__CertificateKeyPath = "$root/.dev/certs/localhost-key.pem"
$env:Dev__FrontendPath = "$root/frontend/dist"
$env:DevSeed__InvitationsFile = "$root/.dev/invitations.json"
$process = Start-Process dotnet -ArgumentList 'run', '--project', 'backend/src/AppHost', '--launch-profile', 'http' -RedirectStandardOutput .dev/apphost.log -PassThru
$process.Id | Set-Content .dev/apphost.pid

Write-Host 'Waiting for the Gateway (the first start pulls images and can take several minutes)...'
$deadline = (Get-Date).AddMinutes(20)
# curl.exe (included with Windows 10 and later) works in Windows PowerShell 5.1 and in PowerShell 7, unlike
# Invoke-WebRequest -SkipCertificateCheck, which 5.1 doesn't have.
while ((Get-Date) -lt $deadline) {
  $status = & curl.exe --silent --insecure --max-time 5 --output NUL --write-out '%{http_code}' https://localhost:7443/health/ready
  if ($status -eq '200') { break }
  Start-Sleep 5
}
if ($status -ne '200') {
  Write-Host ''
  Write-Host 'The stack did not become ready. Open the Aspire dashboard (http://localhost:15100) and look at the resource that failed:'
  Write-Host '  - migrator failed with "There is already an object named ...": the local databases are from an earlier copy of this'
  Write-Host '    product. Start clean with scripts/dev-down.ps1 -Reset, then scripts/dev-up.ps1 (this deletes the local data).'
  Write-Host '  - a service failed: its console log in the dashboard says why.'
  Write-Host 'Stop it with scripts/dev-down.ps1.'
  exit 1
}
while (-not (Test-Path .dev/invitations.json.done) -and (Get-Date) -lt $deadline) { Start-Sleep 5 }
Write-Host "demo       https://demo.localhost:7443/"
Write-Host "More tenants: scripts/onboard-tenant.ps1 <slug> [--plan <plan>]"
if (Test-Path .dev/invitations.json) { Get-Content .dev/invitations.json }
Write-Host "Aspire dashboard http://localhost:15100 - Mailpit http://localhost:8025 - stop with scripts/dev-down.ps1"
