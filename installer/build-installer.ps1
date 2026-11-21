# TypeRight 2.0 - Windows Installer Build Script
# Requires: WiX Toolset 3.11+ (https://wixtoolset.org/)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish"
$installerDir = $PSScriptRoot
$outputDir = Join-Path $root "release"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TypeRight 2.0 - Installer Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Publish self-contained app
Write-Host "[1/3] Publishing self-contained application..." -ForegroundColor Yellow
$appProject = Join-Path $root "src\TypeRight.Desktop\TypeRight.Desktop.csproj"
& dotnet publish $appProject -c Release -r win-x64 --self-contained true -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish application"
    exit 1
}

# Step 2: Harvest all published files into WiX component group
Write-Host "[2/3] Harvesting published files..." -ForegroundColor Yellow
$harvestPath = Join-Path $installerDir "ApplicationFiles.wxs"

$heat = "heat.exe"
$heatPath = Get-Command $heat -ErrorAction SilentlyContinue
if (-not $heatPath) {
    # Try common WiX installation paths
    $possiblePaths = @(
        "C:\Program Files (x86)\WiX Toolset v3.11\bin\heat.exe",
        "C:\Program Files\WiX Toolset v3.11\bin\heat.exe"
    )
    foreach ($p in $possiblePaths) {
        if (Test-Path $p) {
            $heat = $p
            break
        }
    }
}

& $heat dir $publishDir -cg ApplicationFiles -gg -scom -sreg -sfrag -srd -dr AppFolder -var var.PublishDir -out $harvestPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to harvest files. Is WiX Toolset installed?"
    exit 1
}

# Step 3: Build MSI
Write-Host "[3/3] Building MSI installer..." -ForegroundColor Yellow
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

$candle = "candle.exe"
$light = "light.exe"
# Resolve WiX tools similar to heat
$wixBin = Split-Path -Parent $heat
$candle = Join-Path $wixBin "candle.exe"
$light = Join-Path $wixBin "light.exe"

$objectFiles = @(
    (Join-Path $installerDir "TypeRightInstaller.wxs"),
    $harvestPath
) -join " "

& $candle -dPublishDir=$publishDir\ -out $installerDir\ $objectFiles

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to compile WiX sources"
    exit 1
}

$msiPath = Join-Path $outputDir "TypeRight-2.0-Setup.msi"
$wixobjs = (Get-ChildItem $installerDir -Filter "*.wixobj" | ForEach-Object { $_.FullName }) -join " "

& $light -ext WixUIExtension -cultures:en-us -out $msiPath $wixobjs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to link MSI"
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Installer built successfully!" -ForegroundColor Green
Write-Host "Output: $msiPath" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
