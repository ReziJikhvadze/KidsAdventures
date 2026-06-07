# Package frontend-dist into frontend-deploy.zip for adventuresfront ZipDeployUI.
$ErrorActionPreference = "Stop"
$apiDir = Join-Path (Split-Path -Parent $PSScriptRoot) "KidsAdventuresAPI"
$wwwroot = Join-Path $apiDir "wwwroot"
$frontendDist = Join-Path $apiDir "frontend-dist"
$zipPath = Join-Path $apiDir "frontend-deploy.zip"
$staging = Join-Path $apiDir "_frontend-deploy-staging"

if (-not (Test-Path (Join-Path $frontendDist "server\index.mjs"))) {
    Write-Error "Missing $frontendDist\server\index.mjs - run npm run build in wwwroot first."
}

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -Path (Join-Path $frontendDist "*") -Destination $staging -Recurse -Force
Copy-Item (Join-Path $wwwroot "azure-deploy.package.json") (Join-Path $staging "package.json") -Force
Copy-Item (Join-Path $wwwroot "azure-deploy.deployment") (Join-Path $staging ".deployment") -Force

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath
Remove-Item $staging -Recurse -Force
Write-Host "Frontend deploy zip: $zipPath" -ForegroundColor Green
