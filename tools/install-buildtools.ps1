$ErrorActionPreference = "Stop"
$bootstrapper = "C:\TypeRight-buildtools.exe"
$log = "C:\TypeRight-buildtools-install.log"

Write-Output "[1/2] Downloading Visual Studio Build Tools 2022 bootstrapper..."
Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vs_buildtools.exe' -OutFile $bootstrapper -UseBasicParsing
Write-Output ("      Downloaded {0:N1} MB" -f ((Get-Item $bootstrapper).Length / 1MB))

Write-Output "[2/2] Installing C++ workload + Windows 11/10 SDK (this can take a while)..."
$args = @(
  '--quiet','--wait','--norestart','--nocache',
  '--add','Microsoft.VisualStudio.Workload.VCTools',
  '--add','Microsoft.VisualStudio.Component.VC.Tools.x86.x64',
  '--add','Microsoft.VisualStudio.Component.Windows11SDK.22621',
  '--add','Microsoft.VisualStudio.Component.Windows10SDK.19041',
  '--includeRecommended'
)
$p = Start-Process -FilePath $bootstrapper -ArgumentList $args -PassThru -Wait -RedirectStandardOutput $log -RedirectStandardError "$log.err"
Write-Output ("Installer exit code: " + $p.ExitCode)
Write-Output ("Install log: " + $log)
if (Test-Path $log) { Get-Content $log -Tail 30 }
