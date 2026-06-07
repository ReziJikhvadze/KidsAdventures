# Build TanStack/Nitro frontend for Azure (node-server preset -> wwwroot/azure-ssr).
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
    if (-not (Test-Path "azure-ssr\server\index.mjs")) {
        Write-Error "Build finished but azure-ssr\server\index.mjs is missing. Check vite.config.ts nitro preset."
    }
    Write-Host "Frontend ready: $wwwroot\azure-ssr" -ForegroundColor Green
}
finally {
    Pop-Location
}
