# Build and zip the Nitro SSR frontend for a Linux Node App Service (Node 20/22 LTS).
# Usage:
#   .\publish-frontend-azure.ps1
#   .\publish-frontend-azure.ps1 -ApiBaseUrl "https://your-api.azurewebsites.net"
# Deploy the zip via Kudu Zip Push Deploy on the FRONTEND app (not the API app).

param(
    [string]$ApiBaseUrl = "https://adventuresapi-guajeacbcucsbwau.polandcentral-01.azurewebsites.net"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$wwwroot = Join-Path $repoRoot "KidsAdventuresAPI\wwwroot"
$buildScript = Join-Path $PSScriptRoot "build-frontend.ps1"

& $buildScript -ApiBaseUrl $ApiBaseUrl
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$azureSsr = Join-Path $wwwroot "azure-ssr"
$zipPath = Join-Path $wwwroot "frontend-deploy.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Zip contents of azure-ssr (server/, public/, nitro.json at archive root — not nested in a folder)
Compress-Archive -Path (Join-Path $azureSsr "*") -DestinationPath $zipPath

Write-Host ""
Write-Host "Frontend deploy zip: $zipPath" -ForegroundColor Green
Write-Host ""
Write-Host "Azure Portal -> FRONTEND App Service (Node 22 LTS):" -ForegroundColor Cyan
Write-Host "  1. Configuration -> General settings -> Startup Command: node server/index.mjs"
Write-Host "  2. Configuration -> Application settings:"
Write-Host "       SCM_DO_BUILD_DURING_DEPLOYMENT = false"
Write-Host "       WEBSITE_NODE_DEFAULT_VERSION   = ~22"
Write-Host "  3. Advanced Tools (Kudu) -> Zip Push Deploy -> upload frontend-deploy.zip"
Write-Host "  4. On the API appsettings.Production.json add your FRONTEND URL to Cors:AllowedOrigins"
Write-Host "     and set Email:BaseUrl + Stripe Success/Cancel URLs to the frontend host"
Write-Host "  5. Republish the API once CORS is updated"
