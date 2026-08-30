#Requires -Version 5.1
<#
.SYNOPSIS
    Stops or starts the MiFuturoTrabajo Azure resources that can actually be
    stopped, and reports honestly on the ones that cannot.

.DESCRIPTION
    Only two resources in this application have a meaningful off switch:

      mifuturotrabajo-api    App Service (Linux F1)
      mifuturotrabajo-func   Function App (Consumption) - stopping it also
                             stops both reminder timers

    Everything else either has no stop, or has no cost worth stopping for:

      Static Web App      Free tier. No stop exists. Costs nothing.
      Cosmos DB           Serverless. Cannot be paused - capacity is per
                          request. Idle cost is storage only, around
                          USD 0.25/GB/month. Only deletion stops it.
      Storage account     Cannot be stopped. Cents per month for the CV blobs.
      Communication Svc   Bills per message. Nothing runs when idle.
      App Insights / Logs Bills on ingestion. Nothing arrives once the two
                          apps above are stopped.

    So "disable" here means: stop the compute, stop the timers, stop the log
    ingestion. It does not mean zero cost - that needs `az group delete`.

.PARAMETER Action
    status  Show the current state of everything. Read-only. (default)
    stop    Stop the API and the Function App.
    start   Start them again.

.PARAMETER IncludeCi
    Also disable or enable the GitHub Actions deploy workflow. Worth using
    with -Action stop: otherwise the next push to main redeploys and starts
    both apps again, silently undoing this. Requires the gh CLI.

.EXAMPLE
    .\infra\azure-power.ps1
    .\infra\azure-power.ps1 -Action stop -IncludeCi
    .\infra\azure-power.ps1 -Action start -IncludeCi
#>
[CmdletBinding()]
param(
    [ValidateSet('status', 'stop', 'start')]
    [string]$Action = 'status',

    [switch]$IncludeCi
)

$ErrorActionPreference = 'Stop'

$ApiRg    = 'rg-mifuturotrabajo-prod'
$ApiName  = 'mifuturotrabajo-api'
$FuncRg   = 'rg-mifuturotrabajo-func'
$FuncName = 'mifuturotrabajo-func'
$SwaRg    = 'rg-mifuturotrabajo-prod'
$SwaName  = 'mifuturotrabajo-web'

function Write-Head ($msg) { Write-Host "`n$msg" -ForegroundColor Cyan }
function Write-OK   ($msg) { Write-Host "  ok    $msg" -ForegroundColor Green }
function Write-Info ($msg) { Write-Host "  ..    $msg" -ForegroundColor DarkGray }
function Write-Warn ($msg) { Write-Host "  !!    $msg" -ForegroundColor Yellow }
function Write-Bad  ($msg) { Write-Host "  fail  $msg" -ForegroundColor Red }

# --- preflight ----------------------------------------------------------------

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Bad "The Azure CLI is not installed or not on PATH."
    exit 1
}

$account = az account show -o json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Bad "Not signed in to Azure. Run: az login"
    exit 1
}

Write-Host ""
Write-Host "===========================================================" -ForegroundColor Cyan
Write-Host "  MiFuturoTrabajo  -  $($Action.ToUpper())" -ForegroundColor Cyan
Write-Host "  subscription: $($account.name)" -ForegroundColor DarkGray
Write-Host "===========================================================" -ForegroundColor Cyan

function Get-SiteState ($rg, $name) {
    $s = az webapp show -g $rg -n $name --query state -o tsv 2>$null
    if ([string]::IsNullOrWhiteSpace($s)) { return 'NotFound' }
    return $s.Trim()
}

# --- status -------------------------------------------------------------------

if ($Action -eq 'status') {
    Write-Head "Compute (can be stopped)"

    foreach ($t in @(
        @{ Label = 'API       '; Rg = $ApiRg;  Name = $ApiName },
        @{ Label = 'Functions '; Rg = $FuncRg; Name = $FuncName }
    )) {
        $state = Get-SiteState $t.Rg $t.Name
        switch ($state) {
            'Running'  { Write-OK   "$($t.Label) $($t.Name)  ->  Running" }
            'Stopped'  { Write-Warn "$($t.Label) $($t.Name)  ->  Stopped" }
            'NotFound' { Write-Bad  "$($t.Label) $($t.Name)  ->  not found" }
            default    { Write-Info "$($t.Label) $($t.Name)  ->  $state" }
        }
    }

    Write-Head "Always on (no stop exists)"
    Write-Info "Static Web App   $SwaName            Free tier, no cost"
    Write-Info "Cosmos DB        serverless          storage only when idle"
    Write-Info "Storage account  CV blobs            cents/month"
    Write-Info "Communication    per message         nothing when idle"
    Write-Info "App Insights     per ingestion       quiet once compute stops"

    if (Get-Command gh -ErrorAction SilentlyContinue) {
        Write-Head "CI"
        $wf = gh workflow list --json name,state -q '.[] | select(.name=="Deploy") | .state' 2>$null
        if ($wf) {
            if ($wf -eq 'active') {
                Write-Info "Deploy workflow  active   - a push to main will redeploy and start both apps"
            } else {
                Write-Warn "Deploy workflow  $wf"
            }
        }
    }

    Write-Host ""
    exit 0
}

# --- stop / start -------------------------------------------------------------

$failed = 0

foreach ($t in @(
    @{ Label = 'API';       Rg = $ApiRg;  Name = $ApiName;  Kind = 'webapp' },
    @{ Label = 'Functions'; Rg = $FuncRg; Name = $FuncName; Kind = 'functionapp' }
)) {
    $before = Get-SiteState $t.Rg $t.Name

    if ($before -eq 'NotFound') {
        Write-Bad "$($t.Label): not found in $($t.Rg)"
        $failed = 1
        continue
    }

    $want = if ($Action -eq 'stop') { 'Stopped' } else { 'Running' }

    if ($before -eq $want) {
        Write-Info "$($t.Label): already $before"
        continue
    }

    Write-Info "$($t.Label): $before -> $want ..."
    az $t.Kind $Action -g $t.Rg -n $t.Name --only-show-errors 2>$null | Out-Null

    # Trust the reported state, not the exit code: the CLI returns 0 for a
    # stop that has not taken effect yet.
    $after = ''
    foreach ($attempt in 1..12) {
        Start-Sleep -Seconds 5
        $after = Get-SiteState $t.Rg $t.Name
        if ($after -eq $want) { break }
    }

    if ($after -eq $want) {
        Write-OK "$($t.Label): $after"
    } else {
        Write-Bad "$($t.Label): still '$after' after waiting, wanted '$want'"
        $failed = 1
    }
}

# --- CI ----------------------------------------------------------------------

if ($IncludeCi) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Warn "gh CLI not found - leaving the deploy workflow untouched."
    } else {
        if ($Action -eq 'stop') {
            gh workflow disable deploy.yml 2>$null | Out-Null
            Write-OK "Deploy workflow disabled - a push to main will no longer restart these."
        } else {
            gh workflow enable deploy.yml 2>$null | Out-Null
            Write-OK "Deploy workflow enabled."
        }
    }
} elseif ($Action -eq 'stop') {
    Write-Warn "The deploy workflow is still active: the next push to main will"
    Write-Warn "redeploy and start both apps. Use -IncludeCi to disable it too."
}

Write-Host ""
if ($failed -ne 0) {
    Write-Bad "Finished with errors."
    exit 1
}

Write-Host "Done." -ForegroundColor Green
Write-Host ""
