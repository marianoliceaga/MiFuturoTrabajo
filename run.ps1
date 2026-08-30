#Requires -Version 5.1
<#
.SYNOPSIS
    Stops all MiFuturoTrabajo services and restarts the full stack.
.DESCRIPTION
    Terminates:
      - Cosmos DB Emulator
      - Azurite (Azure Storage Emulator)
      - dotnet processes belonging to this solution (API, Functions, Client)
    Then starts them back in order:
      1. Cosmos DB Emulator  (waits until the data-plane is reachable)
      2. Azurite
      3. API
      4. Functions
      5. Client
#>
param(
    [switch]$SkipCosmosWait,   # Skip waiting for Cosmos DB to be ready
    [switch]$NoSwagger         # Don't open Swagger in the browser after startup
)

$ErrorActionPreference = "Continue"

$scriptPath = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptPath)) { $scriptPath = (Get-Location).Path }

# --- Colours ------------------------------------------------------------------
function Write-Step  ($n, $t, $msg) { Write-Host "`n[$n/$t] $msg" -ForegroundColor Yellow }
function Write-OK    ($msg)         { Write-Host "  OK  $msg"     -ForegroundColor Green  }
function Write-Info  ($msg)         { Write-Host "  ..  $msg"     -ForegroundColor Cyan   }
function Write-Warn  ($msg)         { Write-Host "  !!  $msg"     -ForegroundColor DarkYellow }

$totalSteps = 7

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "    MiFuturoTrabajo  -  Full Restart        " -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# --- STEP 1: Stop Cosmos DB Emulator ------------------------------------------
Write-Step 1 $totalSteps "Stopping Cosmos DB Emulator..."

$cosmosProc = Get-Process "Microsoft.Azure.Cosmos.Emulator" -ErrorAction SilentlyContinue
if ($cosmosProc) {
    $cosmosProc | Stop-Process -Force
    Write-OK "Cosmos DB Emulator stopped."
} else {
    Write-Info "Cosmos DB Emulator was not running."
}

Get-CimInstance Win32_Process -Filter "Name = 'CosmosDB.Emulator.exe'" -ErrorAction SilentlyContinue |
    Invoke-CimMethod -MethodName Terminate -ErrorAction SilentlyContinue | Out-Null

# --- STEP 2: Stop Azurite -----------------------------------------------------
Write-Step 2 $totalSteps "Stopping Azurite..."

Get-Process azurite* -ErrorAction SilentlyContinue | Stop-Process -Force

$azuriteNodeProcs = Get-CimInstance Win32_Process -Filter "Name = 'node.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match "azurite" }
if ($azuriteNodeProcs) {
    $azuriteNodeProcs | Invoke-CimMethod -MethodName Terminate | Out-Null
    Write-OK "Azurite node process(es) stopped."
} else {
    Write-Info "Azurite was not running."
}

$azuriteCmdProcs = Get-CimInstance Win32_Process -Filter "Name = 'cmd.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match "azurite" }
if ($azuriteCmdProcs) {
    $azuriteCmdProcs | Invoke-CimMethod -MethodName Terminate | Out-Null
}

# --- STEP 3: Stop .NET services -----------------------------------------------
Write-Step 3 $totalSteps "Stopping .NET services (API, Functions, Client)..."

$dotnetProcs = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match "MiFuturoTrabajo" }
if ($dotnetProcs) {
    $dotnetProcs | Invoke-CimMethod -MethodName Terminate | Out-Null
    Write-OK "$($dotnetProcs.Count) dotnet process(es) terminated."
} else {
    Write-Info "No MiFuturoTrabajo dotnet processes were running."
}

Get-Process "MiFuturoTrabajo.Api", "MiFuturoTrabajo.Functions", "MiFuturoTrabajo.Client" `
    -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Info "Waiting for processes to exit..."
Start-Sleep -Seconds 2

# --- STEP 4: Start Cosmos DB Emulator -----------------------------------------
Write-Step 4 $totalSteps "Starting Cosmos DB Emulator..."

$cosmosExe = "$env:ProgramFiles\Azure Cosmos DB Emulator\Microsoft.Azure.Cosmos.Emulator.exe"
if (Test-Path $cosmosExe) {
    Start-Process -FilePath $cosmosExe -WindowStyle Minimized
    Write-OK "Cosmos DB Emulator launched."

    if (-not $SkipCosmosWait) {
        Write-Info "Waiting for Cosmos DB Emulator to become ready (this can take ~30 s)..."
        $cosmosReady = $false
        $maxAttempts = 40    # 40 x 5 s = up to 200 s
        $attempt     = 0
        while (-not $cosmosReady -and $attempt -lt $maxAttempts) {
            $attempt++
            try {
                $resp = Invoke-WebRequest -Uri "https://localhost:8081/_explorer/emulator.pem" `
                            -SkipCertificateCheck -TimeoutSec 4 -ErrorAction Stop
                if ($resp.StatusCode -eq 200) { $cosmosReady = $true }
            } catch {
                Write-Host "  .. [$attempt/$maxAttempts] Not ready yet, retrying in 5 s..." -NoNewline
                Write-Host "`r" -NoNewline
                Start-Sleep -Seconds 5
            }
        }
        if ($cosmosReady) {
            Write-OK "Cosmos DB Emulator is ready at https://localhost:8081"
        } else {
            Write-Warn "Cosmos DB Emulator did not respond in time - continuing anyway."
        }
    }
} else {
    Write-Warn "Cosmos DB Emulator executable not found at:"
    Write-Warn "  $cosmosExe"
    Write-Warn "Skipping. Start it manually or install from https://aka.ms/cosmosdb-emulator"
}

# --- STEP 5: Start Azurite ----------------------------------------------------
Write-Step 5 $totalSteps "Starting Azurite..."

$azuriteLog    = Join-Path $scriptPath "azurite.log"
$azuriteLogErr = Join-Path $scriptPath "azurite_err.log"

# Global npm install (most common)
Start-Process -FilePath "cmd.exe" `
    -ArgumentList "/c azurite > `"$azuriteLog`" 2>&1" `
    -WindowStyle Hidden -ErrorAction SilentlyContinue

# Also try azurite.exe (Visual Studio install)
try {
    Start-Process -FilePath "azurite.exe" `
        -RedirectStandardOutput $azuriteLog `
        -RedirectStandardError  $azuriteLogErr `
        -WindowStyle Hidden -ErrorAction Stop
} catch { }

Start-Sleep -Seconds 2
Write-OK "Azurite started  (log: azurite.log)"

# --- STEP 6: Start API & Functions --------------------------------------------
Write-Step 6 $totalSteps "Starting API and Azure Functions..."

$apiLog    = Join-Path $scriptPath "api.log"
$apiLogErr = Join-Path $scriptPath "api_err.log"
Write-Info "API        ->  api.log"
Start-Process -FilePath "dotnet" -ArgumentList "run" `
    -WorkingDirectory "$scriptPath\MiFuturoTrabajo.Api" `
    -RedirectStandardOutput $apiLog `
    -RedirectStandardError  $apiLogErr `
    -WindowStyle Hidden

$funcLog    = Join-Path $scriptPath "functions.log"
$funcLogErr = Join-Path $scriptPath "functions_err.log"
Write-Info "Functions  ->  functions.log"
Start-Process -FilePath "dotnet" -ArgumentList "run" `
    -WorkingDirectory "$scriptPath\MiFuturoTrabajo.Functions" `
    -RedirectStandardOutput $funcLog `
    -RedirectStandardError  $funcLogErr `
    -WindowStyle Hidden

Write-OK "API and Functions launched."

# --- STEP 7: Start Blazor Client ----------------------------------------------
Write-Step 7 $totalSteps "Starting Blazor Client..."

$clientLog    = Join-Path $scriptPath "client.log"
$clientLogErr = Join-Path $scriptPath "client_err.log"
Write-Info "Client  ->  client.log"
Start-Process -FilePath "dotnet" -ArgumentList "run" `
    -WorkingDirectory "$scriptPath\MiFuturoTrabajo.Client" `
    -RedirectStandardOutput $clientLog `
    -RedirectStandardError  $clientLogErr `
    -WindowStyle Hidden

Write-OK "Client launched."

# --- Done ---------------------------------------------------------------------
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "        All services are starting!          " -ForegroundColor Cyan
Write-Host "--------------------------------------------" -ForegroundColor Cyan
Write-Host "  Cosmos DB   https://localhost:8081        " -ForegroundColor White
Write-Host "  API         https://localhost:7071        " -ForegroundColor White
Write-Host "  Functions   http://localhost:7072         " -ForegroundColor White
Write-Host "  Client      https://localhost:5011        " -ForegroundColor White
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Logs: $scriptPath" -ForegroundColor DarkGray
Write-Host "  api.log  functions.log  client.log  azurite.log" -ForegroundColor DarkGray
Write-Host ""

if (-not $NoSwagger) {
    Start-Sleep -Seconds 5
    Start-Process "https://localhost:7071/swagger"
}
