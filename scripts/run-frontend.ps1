# Runs the Vite / TanStack Start dev server for the frontend.
# Points at the local API via wwwroot\.env (VITE_API_BASE_URL).
#
# Usage:
#   .\scripts\run-frontend.ps1
#
# Prerequisite: run-backend.ps1 in another terminal (http://localhost:5000)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$wwwroot = Join-Path $repoRoot "KidsAdventuresAPI\wwwroot"
$envFile = Join-Path $wwwroot ".env"
$apiBaseUrl = "http://localhost:5000"

if (-not (Test-Path $wwwroot)) {
    Write-Error "Frontend folder not found: $wwwroot"
}

Set-Location $wwwroot

if (-not (Test-Path $envFile)) {
    Write-Host "Creating wwwroot\.env with VITE_API_BASE_URL=$apiBaseUrl" -ForegroundColor Yellow
    "VITE_API_BASE_URL=$apiBaseUrl" | Set-Content -Path $envFile -Encoding utf8NoBOM
}

if (-not (Test-Path "node_modules")) {
    Write-Host "Installing npm dependencies (first run)..." -ForegroundColor Cyan
    npm install
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host ""
Write-Host "=== Adventrya frontend (dev) ===" -ForegroundColor Cyan
Write-Host "  Folder:   $wwwroot" -ForegroundColor DarkGray
Write-Host "  API URL:  $apiBaseUrl (from .env)" -ForegroundColor DarkGray
Write-Host "  Site:     see Local: URL below after Vite starts (often http://localhost:5173)" -ForegroundColor Green
Write-Host "  Stop:     Ctrl+C" -ForegroundColor DarkGray
Write-Host ""

npm run dev
