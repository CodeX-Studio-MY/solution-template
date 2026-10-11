# Starts the whole stack locally with the Aspire AppHost, onboards the two demo tenants and prints how to sign in.
#   scripts/dev-up.ps1 [-Provider sqlserver|postgresql] [-AllInOne] [-NoBuild]
param(
  [ValidateSet('sqlserver', 'postgresql')][string]$Provider = 'sqlserver',
  [switch]$AllInOne,
  [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if ($Provider -eq 'postgresql' -and -not (Select-String -Path platform.yaml -Pattern '^\s*providers:.*postgresql' -Quiet)) {
  throw 'PostgreSQL migrations are not in this build (platform.yaml lists only sqlserver). Run scripts/add-postgresql.ps1 first.'
}
foreach ($tool in 'dotnet', 'docker', 'node', 'mkcert') {
  if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is required." }
}
if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
  # Node.js 25 and later no longer ship corepack.
  if (-not (Get-Command corepack -ErrorAction SilentlyContinue)) { throw 'pnpm is required: run "npm install -g corepack", then "corepack enable pnpm".' }
  corepack enable pnpm
}

New-Item -ItemType Directory -Force .dev/certs | Out-Null
$root = (Get-Location).Path
if (-not (Test-Path .dev/certs/localhost.pem)) {
  mkcert -cert-file .dev/certs/localhost.pem -key-file .dev/certs/localhost-key.pem localhost '*.localhost' 127.0.0.1 ::1
}
# mkcert has no "is it trusted?" command (there is no -check); -install does nothing when the CA is already in the trust store.
# Windows asks once to add the local certificate authority to your trusted roots: answer Yes.
mkcert -install
if ($LASTEXITCODE -ne 0) { Write-Host 'The local certificate authority is not trusted yet: browsers will warn until "mkcert -install" succeeds.' }

if (-not $NoBuild) { Push-Location frontend; pnpm install --frozen-lockfile; pnpm build; Pop-Location }

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
while (-not (Test-Path .dev/invitations.json.done) -and (Get-Date) -lt $deadline) { Start-Sleep 5 }
Write-Host "demo       https://demo.localhost:7443/"
Write-Host "demo-plus  https://demo-plus.localhost:7443/"
if (Test-Path .dev/invitations.json) { Get-Content .dev/invitations.json }
Write-Host "Aspire dashboard http://localhost:15100 - Mailpit http://localhost:8025 - stop with scripts/dev-down.ps1"
