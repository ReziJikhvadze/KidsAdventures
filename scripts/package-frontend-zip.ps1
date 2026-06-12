# Package frontend-dist into frontend-deploy.zip for adventuresfront ZipDeployUI.
$ErrorActionPreference = "Stop"
$apiDir = Join-Path (Split-Path -Parent $PSScriptRoot) "KidsAdventuresAPI"
$wwwroot = Join-Path $apiDir "wwwroot"
$frontendDist = Join-Path $apiDir "frontend-dist"
$zipPath = Join-Path $apiDir "frontend-deploy.zip"
$staging = Join-Path $apiDir "_frontend-deploy-staging"
$serverIndex = Join-Path $frontendDist "server\index.mjs"

if (-not (Test-Path $serverIndex)) {
    Write-Error "Missing $serverIndex - run npm run build in wwwroot first."
}

function New-LinuxCompatibleZip {
    param(
        [string]$SourceDir,
        [string]$DestinationZip
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $root = (Resolve-Path $SourceDir).Path
    if (Test-Path $DestinationZip) {
        Remove-Item $DestinationZip -Force
    }

    $zip = [System.IO.Compression.ZipFile]::Open(
        $DestinationZip,
        [System.IO.Compression.ZipArchiveMode]::Create
    )

    try {
        Get-ChildItem -Path $root -Recurse -File | ForEach-Object {
            $relative = $_.FullName.Substring($root.Length + 1).Replace("\", "/")
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip,
                $_.FullName,
                $relative
            )
        }
    }
    finally {
        $zip.Dispose()
    }
}

function Test-FrontendAssetManifest {
    param(
        [string]$DistDir,
        [string]$IndexPath
    )

    $indexContent = Get-Content -Path $IndexPath -Raw
    $matches = [regex]::Matches($indexContent, '"path": "\.\./public/([^"]+)"')
    $missing = @()

    foreach ($match in $matches) {
        $relativePath = $match.Groups[1].Value.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
        $assetPath = [System.IO.Path]::Combine($DistDir, "public", $relativePath)
        if (-not (Test-Path $assetPath)) {
            $missing += $relativePath
        }
    }

    if ($missing.Count -gt 0) {
        Write-Error @(
            "frontend-dist is missing $($missing.Count) asset(s) referenced by server/index.mjs.",
            "Run a clean frontend build before packaging.",
            ($missing | Select-Object -First 10 | ForEach-Object { "  - $_" }) -join "`n"
        )
    }

    Write-Host "Validated $($matches.Count) manifest asset path(s)." -ForegroundColor DarkGray
}

Test-FrontendAssetManifest -DistDir $frontendDist -IndexPath $serverIndex

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -Path (Join-Path $frontendDist "*") -Destination $staging -Recurse -Force
Copy-Item (Join-Path $wwwroot "azure-deploy.package.json") (Join-Path $staging "package.json") -Force
Copy-Item (Join-Path $wwwroot "azure-deploy.deployment") (Join-Path $staging ".deployment") -Force

New-LinuxCompatibleZip -SourceDir $staging -DestinationZip $zipPath
Remove-Item $staging -Recurse -Force

# Avoid stale duplicate zips in wwwroot (wrong path confuses manual deploys).
$staleWwwrootZip = Join-Path $wwwroot "frontend-deploy.zip"
if (Test-Path $staleWwwrootZip) {
    Remove-Item $staleWwwrootZip -Force
    Write-Host "Removed stale $staleWwwrootZip" -ForegroundColor DarkGray
}

Write-Host "Frontend deploy zip: $zipPath" -ForegroundColor Green
