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

& (Join-Path $PSScriptRoot "package-frontend-zip.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$zipPath = Join-Path (Split-Path -Parent $wwwroot) "frontend-deploy.zip"

Write-Host ""
Write-Host ""
Write-Host "Azure Portal -> FRONTEND App Service (Node 22 LTS):" -ForegroundColor Cyan
Write-Host "  1. Configuration -> General settings -> Startup Command: node server/index.mjs"
Write-Host "  2. Configuration -> Application settings (add ALL, then Save):"
Write-Host "       SCM_DO_BUILD_DURING_DEPLOYMENT = false"
Write-Host "       ENABLE_ORYX_BUILD              = false"
Write-Host "       WEBSITE_NODE_DEFAULT_VERSION   = ~22"
Write-Host "  3. Deploy zip (do NOT use QuickDeploy / VS publish for frontend):"
Write-Host "       https://adventuresfront-bugebshtdxg6e8a8.scm.polandcentral-01.azurewebsites.net/ZipDeployUI"
Write-Host "       Upload frontend-deploy.zip"
Write-Host "  4. On the API appsettings.Production.json add your FRONTEND URL to Cors:AllowedOrigins"
Write-Host "     and set Email:BaseUrl + Stripe Success/Cancel URLs to the frontend host"
Write-Host "  5. Republish the API once CORS is updated"
