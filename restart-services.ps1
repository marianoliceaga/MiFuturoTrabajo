$ErrorActionPreference = "Continue"

Write-Host "==================================" -ForegroundColor Cyan
Write-Host " Restarting MiFuturoTrabajo Stack " -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan

Write-Host "`n[1/4] Stopping Azurite..." -ForegroundColor Yellow
Get-Process azurite* -ErrorAction SilentlyContinue | Stop-Process -Force
$nodeProcesses = Get-CimInstance Win32_Process -Filter "Name = 'node.exe'" | Where-Object { $_.CommandLine -match "azurite" }
if ($nodeProcesses) { $nodeProcesses | Invoke-CimMethod -MethodName Terminate | Out-Null }
$cmdProcesses = Get-CimInstance Win32_Process -Filter "Name = 'cmd.exe'" | Where-Object { $_.CommandLine -match "azurite" }
if ($cmdProcesses) { $cmdProcesses | Invoke-CimMethod -MethodName Terminate | Out-Null }

Write-Host "[2/4] Stopping .NET Services (API & Client)..." -ForegroundColor Yellow
$dotnetProcesses = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" | Where-Object { $_.CommandLine -match "MiFuturoTrabajo" }
if ($dotnetProcesses) { $dotnetProcesses | Invoke-CimMethod -MethodName Terminate | Out-Null }

Get-Process "MiFuturoTrabajo.Api", "MiFuturoTrabajo.Client" -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Waiting for processes to exit..."
Start-Sleep -Seconds 2

$scriptPath = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptPath)) { $scriptPath = (Get-Location).Path }

Write-Host "`n[3/4] Starting Azurite..." -ForegroundColor Green
$azuriteLog = Join-Path $scriptPath "azurite.log"
# We try running it using cmd /c azurite. This will work if it's installed globally via npm
Start-Process -FilePath "cmd.exe" -ArgumentList "/c azurite > `"$azuriteLog`" 2>&1" -WindowStyle Hidden -ErrorAction SilentlyContinue
$azuriteLogErr = Join-Path $scriptPath "azurite_err.log"
# Also try the executable just in case (e.g. from Visual Studio)
try {
    Start-Process -FilePath "azurite.exe" -RedirectStandardOutput $azuriteLog -RedirectStandardError $azuriteLogErr -WindowStyle Hidden -ErrorAction Stop
} catch {
    # Ignore if azurite.exe is not found in path
}

Write-Host "[4/4] Starting .NET Services..." -ForegroundColor Green

$apiLog = Join-Path $scriptPath "api.log"
$apiLogErr = Join-Path $scriptPath "api_err.log"
Write-Host "  -> Starting API (Logs: api.log)"
Start-Process -FilePath "dotnet" -ArgumentList "run" -WorkingDirectory "$scriptPath\MiFuturoTrabajo.Api" -RedirectStandardOutput $apiLog -RedirectStandardError $apiLogErr -WindowStyle Hidden

$clientLog = Join-Path $scriptPath "client.log"
$clientLogErr = Join-Path $scriptPath "client_err.log"
Write-Host "  -> Starting Client (Logs: client.log)"
Start-Process -FilePath "dotnet" -ArgumentList "run" -WorkingDirectory "$scriptPath\MiFuturoTrabajo.Client" -RedirectStandardOutput $clientLog -RedirectStandardError $clientLogErr -WindowStyle Hidden

Write-Host "`n==================================" -ForegroundColor Cyan
Write-Host " Services successfully started!" -ForegroundColor Cyan
Write-Host " API: https://localhost:7071"
Write-Host " Client: https://localhost:5011"
Write-Host "==================================" -ForegroundColor Cyan

Start-Sleep -Seconds 3
Start-Process "https://localhost:7071/swagger"
