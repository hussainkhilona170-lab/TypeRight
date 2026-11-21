# TypeRight 2.0 - PowerShell Installer
# This script installs TypeRight as a per-user Windows application.
# It creates Start Menu shortcuts, optional desktop shortcut, and startup registration.

param(
    [switch]$DesktopShortcut,
    [switch]$StartWithWindows,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

$appName = "TypeRight 2.0"
$installDir = Join-Path $env:LOCALAPPDATA "TypeRight"
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\TypeRight"
$desktopDir = [Environment]::GetFolderPath("Desktop")
$sourceDir = Join-Path (Split-Path -Parent $PSScriptRoot) "publish"

function Write-Status($msg) {
    Write-Host "[TypeRight] $msg" -ForegroundColor Cyan
}

function New-Shortcut($shortcutPath, $targetPath, $description) {
    $shell = New-Object -ComObject WScript.Shell
    $sc = $shell.CreateShortcut($shortcutPath)
    $sc.TargetPath = $targetPath
    $sc.Arguments = "--show"
    $sc.Description = $description
    $sc.IconLocation = "$targetPath,0"
    $sc.WorkingDirectory = Split-Path -Parent $targetPath
    $sc.Save()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($sc) | Out-Null
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
}

if ($Uninstall) {
    Write-Status "Uninstalling TypeRight 2.0..."

    # Remove startup registration
    Remove-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "TypeRight" -ErrorAction SilentlyContinue

    # Remove shortcuts
    Remove-Item -Path (Join-Path $startMenuDir "*") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $startMenuDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $desktopDir "TypeRight 2.0.lnk") -Force -ErrorAction SilentlyContinue

    # Remove files
    Remove-Item -Path $installDir -Recurse -Force -ErrorAction SilentlyContinue

    # Remove this installation's Add/Remove Programs entry
    Remove-Item -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TypeRight2.0" -Recurse -Force -ErrorAction SilentlyContinue

    Write-Status "TypeRight 2.0 has been uninstalled."
    exit 0
}

Write-Status "Installing TypeRight 2.0..."

# Check source
if (-not (Test-Path (Join-Path $sourceDir "TypeRight.exe"))) {
    Write-Error "Application files not found. Please publish the app first using build-installer.ps1"
    exit 1
}

# Do not overwrite files belonging to a running instance.
$running = @(Get-Process -Name TypeRight -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Warning ("TypeRight is running (PID(s): {0}). Close it and re-run this installer." -f (($running.Id) -join ', '))
    exit 0
}

# Create install directory
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

# Copy application files
Write-Status "Copying application files to $installDir..."
Copy-Item -Path (Join-Path $sourceDir "*") -Destination $installDir -Recurse -Force

$exePath = Join-Path $installDir "TypeRight.exe"

# Create Start Menu folder and shortcut
Write-Status "Creating Start Menu shortcut..."
if (-not (Test-Path $startMenuDir)) {
    New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
}
New-Shortcut (Join-Path $startMenuDir "TypeRight 2.0.lnk") $exePath "TypeRight 2.0 - Windows Typing Companion"

# Optional desktop shortcut
if ($DesktopShortcut) {
    Write-Status "Creating desktop shortcut..."
    New-Shortcut (Join-Path $desktopDir "TypeRight 2.0.lnk") $exePath "TypeRight 2.0 - Windows Typing Companion"
}

# Optional startup registration
if ($StartWithWindows) {
    Write-Status "Registering TypeRight to start with Windows..."
    Set-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "TypeRight" -Value "`"$exePath`"" -Type String
}

# Create uninstall registry entries
Write-Status "Registering uninstall information..."
$uninstallKey = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TypeRight2.0"
if (-not (Test-Path $uninstallKey)) {
    New-Item -Path $uninstallKey -Force | Out-Null
}
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value $appName -Type String
Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "2.0.0" -Type String
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "TypeRight" -Type String
Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $installDir -Type String
Set-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value "powershell.exe -NoProfile -File `"$PSScriptRoot\Install-TypeRight.ps1`" -Uninstall" -Type String
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value $exePath -Type String
Set-ItemProperty -Path $uninstallKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name "NoRepair" -Value 1 -Type DWord

Write-Status "Installation complete!"
Write-Status "TypeRight 2.0 installed to: $installDir"
Write-Status "Start Menu shortcut created."
if ($DesktopShortcut) { Write-Status "Desktop shortcut created." }
if ($StartWithWindows) { Write-Status "Startup registration enabled." }
