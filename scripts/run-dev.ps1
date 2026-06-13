# Starts backend + frontend in two separate PowerShell windows.
#
# Usage (from repo root):
#   .\scripts\run-dev.ps1
#
# Or double-click: scripts\run-dev.bat

$ErrorActionPreference = "Stop"

$scriptsDir = $PSScriptRoot
$backend = Join-Path $scriptsDir "run-backend.ps1"
$frontend = Join-Path $scriptsDir "run-frontend.ps1"

Write-Host ""
Write-Host "=== Adventrya local dev ===" -ForegroundColor Cyan
Write-Host "  Opening two terminals:" -ForegroundColor DarkGray
Write-Host "    1) API      -> http://localhost:5000" -ForegroundColor Green
Write-Host "    2) Frontend -> Vite URL (usually http://localhost:5173)" -ForegroundColor Green
Write-Host "  Close each window to stop that service." -ForegroundColor DarkGray
Write-Host ""

Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-ExecutionPolicy", "Bypass",
    "-File", $backend
)

Start-Sleep -Seconds 3

Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-ExecutionPolicy", "Bypass",
    "-File", $frontend
)

Write-Host "Done. Wait for both terminals to finish starting, then open the frontend URL." -ForegroundColor Green
Write-Host ""
