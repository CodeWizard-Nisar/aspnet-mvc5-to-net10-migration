<#
  Starts the ConfReg stack on Windows. Browse http://localhost:5000 when it is up.

      .\tools\windows\run-stack.ps1                  everything (needs IIS Express for the net48 app)
      .\tools\windows\run-stack.ps1 -SkipLegacy      .NET 10 only: admin portal, no public wizard, no IIS needed
      .\tools\windows\run-stack.ps1 -RecreateDatabase
      .\tools\windows\run-stack.ps1 -Stop            stop what this script started

  Prerequisites: Docker Desktop (SQL Server) and the .NET 10 SDK. Legacy.Web additionally needs IIS Express, because
  System.Web cannot run on Kestrel. Step-by-step equivalents and troubleshooting: docs/00-setup.md.

  Each process opens in its own window so its log is readable and it can be closed on its own.
  Written for this repo's layout but NOT executed on Windows by its author: if something differs, docs/00-setup.md
  has the same steps as individual commands.
#>
[CmdletBinding()]
param(
  [switch]$RecreateDatabase,
  [switch]$SkipLegacy,
  [switch]$Stop,
  [string]$IisExpress
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent

function Default-Env($name, $value) {
  if (-not [Environment]::GetEnvironmentVariable($name)) { [Environment]::SetEnvironmentVariable($name, $value) }
}
# Demo values only. Real deployments supply these from user-secrets or the environment; see docs/03-libraries.md.
Default-Env 'ConfReg__ConnectionString' 'Server=localhost,1433;Database=ConfReg;User Id=sa;Password=ConfReg_Dev_Passw0rd!;TrustServerCertificate=True;'
Default-Env 'ConfReg__RemoteAppApiKey'  'deadbeefdeadbeefdeadbeefdeadbeef'
Default-Env 'ConfReg__LegacyCryptoKey'  'Y29uZnJlZy1kZW1vLWtleS1OT1QtQS1TRUNSRVQtMDA='
Default-Env 'ConfReg__LegacyCryptoIv'   'Y29uZnJlZy1kZW1vLWl2IQ=='
Default-Env 'ConfReg__PaymentApiKey'    'sk_test_DEMO_not_a_real_key'

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

# A running .NET app holds its own DLLs open, so building over it fails with MSB3027 "The file is locked by ...".
# Any second run of this script would hit that, so stop previous instances BEFORE building. IIS Express is left alone:
# ASP.NET shadow-copies assemblies, so it does not lock Legacy.Web's bin, and killing every iisexpress.exe could take
# down unrelated sites. Use -Stop to shut that down too.
function Stop-ConfReg([switch]$IncludeIis) {
  $names = @('ConfReg.Gateway', 'ConfReg.Modern.Web', 'ConfReg.Jobs.Worker')
  foreach ($proc in @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.ProcessName })) {
    Write-Host "  stopping $($proc.ProcessName) (pid $($proc.Id))"
    try { $proc | Stop-Process -Force -ErrorAction Stop } catch { Write-Warning "  could not stop $($proc.ProcessName): $_" }
  }
  if ($IncludeIis) {
    foreach ($proc in @(Get-Process -Name iisexpress -ErrorAction SilentlyContinue)) {
      Write-Host "  stopping iisexpress (pid $($proc.Id))"
      try { $proc | Stop-Process -Force -ErrorAction Stop } catch { Write-Warning "  could not stop iisexpress: $_" }
    }
  }
  Start-Sleep -Milliseconds 500   # let Windows release the file handles before MSBuild tries to overwrite them
}

if ($Stop) {
  Step 'Stopping ConfReg processes'
  Stop-ConfReg -IncludeIis
  Write-Host 'Stopped. SQL Server is still running; use "docker compose down" to stop it too.'
  return
}

Step 'Stopping anything still running from a previous run'
Stop-ConfReg

Step 'SQL Server (Docker)'
if (-not (Test-Path (Join-Path $root '.env'))) { Copy-Item (Join-Path $root '.env.example') (Join-Path $root '.env') }
docker compose --project-directory $root up -d
if ($LASTEXITCODE -ne 0) { throw 'docker compose failed. Is Docker Desktop running?' }

Step 'Build (Release)'
dotnet build (Join-Path $root 'ConfReg.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Step 'Database (created without needing Legacy.Web)'
$dbArgs = @('run','-c','Release','--project',(Join-Path $root 'tools\DbInit'))
if ($RecreateDatabase) { $dbArgs += @('--','--recreate') }
& dotnet @dbArgs
if ($LASTEXITCODE -ne 0) { throw 'Database init failed. SQL Server may still be starting; wait a few seconds and retry.' }

if (-not $SkipLegacy) {
  if (-not $IisExpress) {
    $IisExpress = @("$env:ProgramFiles\IIS Express\iisexpress.exe",
                    "${env:ProgramFiles(x86)}\IIS Express\iisexpress.exe") |
                  Where-Object { Test-Path $_ } | Select-Object -First 1
  }
  if (-not $IisExpress) {
    Write-Warning 'IIS Express not found: skipping Legacy.Web, so the public wizard will be unavailable.'
    Write-Warning 'Install IIS Express, or pass -IisExpress <full path>, or use -SkipLegacy to silence this.'
    $SkipLegacy = $true
  } else {
    Step 'Legacy.Web on IIS Express (http://localhost:5080)'
    Start-Process -FilePath $IisExpress -ArgumentList @("/path:$(Join-Path $root 'src\Legacy.Web')", '/port:5080')
  }
}

Step 'Modern.Web (http://localhost:5100)'
# While the login page still lives on Legacy.Web, Modern.Web accepts that login via the shared session.
[Environment]::SetEnvironmentVariable('Auth__AcceptLegacySession', $(if ($SkipLegacy) { 'false' } else { 'true' }))
Start-Process -FilePath 'dotnet' -ArgumentList @('run','-c','Release','--project',(Join-Path $root 'src\Modern.Web'))

Step 'Gateway (http://localhost:5000)'
# Without Legacy.Web there is nowhere to log in, so the login page has to come from Modern.Web.
if ($SkipLegacy) { [Environment]::SetEnvironmentVariable('Migration__Pages__Account', 'Modern') }
Start-Process -FilePath 'dotnet' -ArgumentList @('run','-c','Release','--project',(Join-Path $root 'src\Gateway'))

Write-Host "`nStarting up. Give it a few seconds, then open:" -ForegroundColor Green
Write-Host '  http://localhost:5000/Admin           admin portal  (served by .NET 10)'
if (-not $SkipLegacy) { Write-Host '  http://localhost:5000/Register/Step1  public wizard (served by .NET Framework 4.8)' }
Write-Host '  http://localhost:5000/gateway/routes  which app owns each page'
Write-Host "`nAdmin login: admin@confreg.example / ConfReg-Demo-1!"
