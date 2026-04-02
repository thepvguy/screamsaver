#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes all Screamsaver projects and builds the MSI installer.

.DESCRIPTION
    Run from the Screamsaver.Installer directory (or pass the repo root as -RepoRoot).
    Requires:
      - .NET 8 SDK
      - WiX v6 CLI: dotnet tool install --global wix

.PARAMETER RepoRoot
    Path to the solution root. Defaults to the parent of the script's directory.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Version
    MSI product version (Major.Minor.Patch). Defaults to 1.0.0.
    Must be incremented for MajorUpgrade to replace an existing installation.
    Example: .\Build.ps1 -Version 1.2.0

.PARAMETER RuntimeIdentifier
    .NET runtime identifier for self-contained publish. Defaults to win-x64.

.PARAMETER TargetFramework
    Target framework moniker. Defaults to net8.0-windows.
#>
param(
    [string]$RepoRoot          = (Split-Path $PSScriptRoot -Parent),
    [string]$Configuration     = "Release",
    [string]$Version           = "1.0.0",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$TargetFramework   = "net8.0-windows"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Publish([string]$ProjectName) {
    $proj = Join-Path $RepoRoot "$ProjectName\$ProjectName.csproj"
    Write-Host "Publishing $ProjectName..." -ForegroundColor Cyan
    dotnet publish $proj -c $Configuration -r $RuntimeIdentifier --self-contained true -p:PublishSingleFile=true
    if ($LASTEXITCODE -ne 0) { throw "Failed to publish $ProjectName" }
}

# 1. Publish all binaries
Publish "Screamsaver.Service"
Publish "Screamsaver.TrayApp"
Publish "Screamsaver.UninstallHelper"

# 2. Compute publish directories (dotnet publish default: bin/{config}/{tfm}/{rid}/publish/)
function PublishDir([string]$ProjectName) {
    Join-Path $RepoRoot "$ProjectName\bin\$Configuration\$TargetFramework\$RuntimeIdentifier\publish"
}

$servicePublishDir      = PublishDir "Screamsaver.Service"
$trayAppPublishDir      = PublishDir "Screamsaver.TrayApp"
$uninstallPublishDir    = PublishDir "Screamsaver.UninstallHelper"

# 3. Build the MSI
Write-Host "Building MSI (version $Version)..." -ForegroundColor Cyan
$wxsFiles = Get-ChildItem -Path $PSScriptRoot -Filter "*.wxs" | Select-Object -ExpandProperty FullName
$outMsi   = Join-Path $PSScriptRoot "Screamsaver.msi"

$wixArgs = @("build") + $wxsFiles + @(
    "-o", $outMsi,
    "-ext", "WixToolset.Util.wixext",
    "-d", "ProductVersion=$Version",
    "-d", "ServicePublishDir=$servicePublishDir",
    "-d", "TrayAppPublishDir=$trayAppPublishDir",
    "-d", "UninstallHelperPublishDir=$uninstallPublishDir"
)

Write-Host "wix $($wixArgs -join ' ')" -ForegroundColor DarkGray
& wix @wixArgs

if ($LASTEXITCODE -ne 0) { throw "WiX build failed." }

Write-Host ""
Write-Host "Build complete: $outMsi" -ForegroundColor Green
Write-Host ""
Write-Host "IMPORTANT: After installing, open the Screamsaver tray icon and set a PIN" -ForegroundColor Yellow
Write-Host "           via Settings > Change PIN before the child uses the computer." -ForegroundColor Yellow
Write-Host "           The recovery password was shown once during key generation." -ForegroundColor Yellow
Write-Host "           Retrieve it from your secure notes — it is not stored here." -ForegroundColor Yellow
