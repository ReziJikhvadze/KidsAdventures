# Runs the .NET API locally (Production profile → appsettings.Production.json).
# Swagger: http://localhost:5000/swagger

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiDir = Join-Path $repoRoot "KidsAdventuresAPI"
$productionSettings = Join-Path $apiDir "appsettings.Production.json"

if (-not (Test-Path $productionSettings)) {
    Write-Host "Missing appsettings.Production.json" -ForegroundColor Yellow
    Write-Host "  Copy: KidsAdventuresAPI\appsettings.Production.example.json" -ForegroundColor Yellow
    Write-Host "  To:   KidsAdventuresAPI\appsettings.Production.json" -ForegroundColor Yellow
    Write-Host "  Then fill SQL, OpenAI, and Azure Blob settings." -ForegroundColor Yellow
    exit 1
}

Set-Location $apiDir
Write-Host "Starting API (ASPNETCORE_ENVIRONMENT=Production)..." -ForegroundColor Cyan
Write-Host "  Folder: $apiDir" -ForegroundColor DarkGray
Write-Host "  Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

dotnet run --launch-profile Production
