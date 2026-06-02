# Runs the Vite dev server for the frontend.
# Requires VITE_API_BASE_URL in KidsAdventuresAPI\wwwroot\.env (default: http://localhost:5000)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$wwwroot = Join-Path $repoRoot "KidsAdventuresAPI\wwwroot"
$envFile = Join-Path $wwwroot ".env"

if (-not (Test-Path $wwwroot)) {
    Write-Error "Frontend folder not found: $wwwroot"
}

Set-Location $wwwroot

if (-not (Test-Path $envFile)) {
    Write-Host "Creating wwwroot\.env with VITE_API_BASE_URL=http://localhost:5000" -ForegroundColor Yellow
    'VITE_API_BASE_URL=http://localhost:5000' | Set-Content -Path $envFile -Encoding utf8
}

if (-not (Test-Path "node_modules")) {
    Write-Host "Installing npm dependencies (first run)..." -ForegroundColor Cyan
    npm install
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Starting frontend dev server..." -ForegroundColor Cyan
Write-Host "  Folder: $wwwroot" -ForegroundColor DarkGray
Write-Host "  API URL: see .env (VITE_API_BASE_URL)" -ForegroundColor DarkGray
Write-Host "  Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

npm run dev
