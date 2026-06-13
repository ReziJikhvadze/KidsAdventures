# Build TanStack/Nitro frontend for Azure (node-server preset -> KidsAdventuresAPI/frontend-dist).
param(
    [string]$ApiBaseUrl = "",
    [string]$SiteUrl = "https://adventrya.com"
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

    $heroSrc = Join-Path $wwwroot "src\assets\hero.jpg"
    $ogDest = Join-Path $wwwroot "public\og-default.jpg"
    if (Test-Path $heroSrc) {
        Copy-Item $heroSrc $ogDest -Force
        Write-Host "Copied og-default.jpg for social previews." -ForegroundColor DarkGray
    }

    $envFile = Join-Path $wwwroot ".env.production"
    $apiLine = if ($ApiBaseUrl) { $ApiBaseUrl } else {
        $existing = Get-Content $envFile -ErrorAction SilentlyContinue | Where-Object { $_ -match '^VITE_API_BASE_URL=' }
        if ($existing) { ($existing -split '=', 2)[1] } else { "" }
    }
    @(
        "# Separate frontend App Service -> point at the API host (no trailing slash)",
        "VITE_API_BASE_URL=$apiLine",
        "VITE_SITE_URL=$SiteUrl"
    ) | Set-Content -Path $envFile -Encoding utf8
    if ($ApiBaseUrl) {
        Write-Host "Using VITE_API_BASE_URL=$ApiBaseUrl" -ForegroundColor DarkGray
    }
    Write-Host "Using VITE_SITE_URL=$SiteUrl" -ForegroundColor DarkGray

    $env:VITE_API_BASE_URL = $apiLine
    $env:VITE_SITE_URL = $SiteUrl
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
