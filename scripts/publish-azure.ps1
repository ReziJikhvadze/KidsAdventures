# Publish KidsAdventures API (+ embedded frontend) to Azure App Service without a pipeline.
# Usage:
#   .\publish-azure.ps1 -WebAppName "your-api-app" -ResourceGroup "your-rg"
# Or set env vars: AZURE_WEBAPP_NAME, AZURE_RESOURCE_GROUP

param(
    [string]$WebAppName = $env:AZURE_WEBAPP_NAME,
    [string]$ResourceGroup = $env:AZURE_RESOURCE_GROUP,
    [switch]$SkipAzDeploy
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$apiDir = Join-Path $repoRoot "KidsAdventuresAPI"
$productionJson = Join-Path $apiDir "appsettings.Production.json"
$exampleJson = Join-Path $apiDir "appsettings.Production.example.json"

if (-not (Test-Path $productionJson)) {
    if (-not (Test-Path $exampleJson)) {
        Write-Error "Missing appsettings.Production.example.json"
    }
    Write-Host "Creating appsettings.Production.json from example - fill secrets before going live." -ForegroundColor Yellow
    Copy-Item $exampleJson $productionJson
}

Write-Host "Publishing API (Release) + building frontend-deploy.zip..." -ForegroundColor Cyan
Push-Location $apiDir
try {
    dotnet publish -c Release -o ./publish
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

$zipPath = Join-Path $apiDir "publish.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $apiDir "publish\*") -DestinationPath $zipPath

Write-Host "Publish package: $zipPath" -ForegroundColor Green
Write-Host "Visual Studio: you can also use Right-click KidsAdventuresAPI -> Publish -> Azure App Service (same result)." -ForegroundColor DarkGray

if ($SkipAzDeploy -or [string]::IsNullOrWhiteSpace($WebAppName) -or [string]::IsNullOrWhiteSpace($ResourceGroup)) {
    Write-Host ""
    Write-Host "Next steps (manual or re-run with -WebAppName and -ResourceGroup):" -ForegroundColor Yellow
    Write-Host "  1. Azure Portal -> App Service -> Configuration -> ASPNETCORE_ENVIRONMENT = Production"
    Write-Host "  2. General settings -> Always On = On"
    Write-Host "  3. Configuration -> General settings -> Stack: .NET 8 (Node 20+ available for SSR sidecar)"
    Write-Host "  4. Deploy publish.zip via Advanced Tools (Kudu) Zip Deploy, or run:"
    Write-Host "     az webapp deploy --resource-group $ResourceGroup --name $WebAppName --src-path `"$zipPath`" --type zip"
    exit 0
}

Write-Host "Deploying to Azure App Service $WebAppName ..." -ForegroundColor Cyan
az webapp config appsettings set `
    --resource-group $ResourceGroup `
    --name $WebAppName `
    --settings ASPNETCORE_ENVIRONMENT=Production WEBSITE_NODE_DEFAULT_VERSION=~20 | Out-Null

az webapp config set `
    --resource-group $ResourceGroup `
    --name $WebAppName `
    --always-on true | Out-Null

az webapp deploy `
    --resource-group $ResourceGroup `
    --name $WebAppName `
    --src-path $zipPath `
    --type zip

Write-Host "Deployed. Browse: https://$WebAppName.azurewebsites.net" -ForegroundColor Green
