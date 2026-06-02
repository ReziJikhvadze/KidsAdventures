# Opens two PowerShell windows: backend API + frontend dev server.

$ErrorActionPreference = "Stop"

$scriptsDir = $PSScriptRoot
$backend = Join-Path $scriptsDir "run-backend.ps1"
$frontend = Join-Path $scriptsDir "run-frontend.ps1"

Write-Host "Launching backend and frontend in separate windows..." -ForegroundColor Cyan
Write-Host "  Close each window to stop that service." -ForegroundColor DarkGray
Write-Host ""

Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-ExecutionPolicy", "Bypass",
    "-File", $backend
)

Start-Sleep -Seconds 2

Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-ExecutionPolicy", "Bypass",
    "-File", $frontend
)

Write-Host "Done. Check the two new terminals." -ForegroundColor Green
