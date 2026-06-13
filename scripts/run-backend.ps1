# Runs the .NET API locally.
# Uses appsettings.Production.json (Azure SQL, OpenAI, Dodo, etc.) when ASPNETCORE_ENVIRONMENT=Production.
#
# Usage:
#   .\scripts\run-backend.ps1
#   .\scripts\run-backend.ps1 -Profile Development
#
# URLs:
#   API:     http://localhost:5000
#   Swagger: http://localhost:5000/swagger

param(
    [ValidateSet("Production", "Development")]
    [string] $Profile = "Production"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiDir = Join-Path $repoRoot "KidsAdventuresAPI"
$productionSettings = Join-Path $apiDir "appsettings.Production.json"

if ($Profile -eq "Production" -and -not (Test-Path $productionSettings)) {
    Write-Host "Missing appsettings.Production.json" -ForegroundColor Yellow
    Write-Host "  Copy: KidsAdventuresAPI\appsettings.Production.example.json" -ForegroundColor Yellow
    Write-Host "  To:   KidsAdventuresAPI\appsettings.Production.json" -ForegroundColor Yellow
    Write-Host "  Then fill SQL, OpenAI, Azure Blob, and Dodo settings." -ForegroundColor Yellow
    exit 1
}

Set-Location $apiDir

$running = Get-Process -Name "KidsAdventuresAPI" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping existing KidsAdventuresAPI (PID $($running.Id))..." -ForegroundColor Yellow
    Stop-Process -Name "KidsAdventuresAPI" -Force
    Start-Sleep -Seconds 1
}

Write-Host ""
Write-Host "=== Adventrya API ===" -ForegroundColor Cyan
Write-Host "  Profile:  $Profile" -ForegroundColor DarkGray
Write-Host "  Folder:   $apiDir" -ForegroundColor DarkGray
Write-Host "  API:      http://localhost:5000" -ForegroundColor Green
Write-Host "  Swagger:  http://localhost:5000/swagger" -ForegroundColor Green
Write-Host "  Stop:     Ctrl+C" -ForegroundColor DarkGray
Write-Host ""

dotnet run --launch-profile $Profile
