# Build TanStack/Nitro frontend for Azure (node-server preset -> KidsAdventuresAPI/frontend-dist).
param(
    [string]$ApiBaseUrl = ""
)

$ErrorActionPreference = "Stop"
$wwwroot = Join-Path (Split-Path -Parent $PSScriptRoot) "KidsAdventuresAPI\wwwroot"
Push-Location $wwwroot
try {
    if (-not (Test-Path "node_modules")) {
        npm install
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $dist = Join-Path (Split-Path -Parent $wwwroot) "frontend-dist"
    if (Test-Path $dist) {
        Write-Host "Cleaning $dist ..." -ForegroundColor DarkGray
        Remove-Item $dist -Recurse -Force
    }

    if ($ApiBaseUrl) {
        $envFile = Join-Path $wwwroot ".env.production"
        @(
            "# Separate frontend App Service -> point at the API host (no trailing slash)",
            "VITE_API_BASE_URL=$ApiBaseUrl"
        ) | Set-Content -Path $envFile -Encoding utf8
        Write-Host "Using VITE_API_BASE_URL=$ApiBaseUrl" -ForegroundColor DarkGray
    }
    $env:VITE_API_BASE_URL = $ApiBaseUrl
    cmd /c "npm run build 2>&1"
    $dist = Join-Path (Split-Path -Parent $wwwroot) "frontend-dist"
    if (-not (Test-Path (Join-Path $dist "server\index.mjs"))) {
        Write-Error "Build finished but frontend-dist\server\index.mjs is missing. Check vite.config.ts nitro preset."
    }
    Write-Host "Frontend ready: $dist" -ForegroundColor Green
}
finally {
    Pop-Location
}
