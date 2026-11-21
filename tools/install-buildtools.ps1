$ErrorActionPreference = "Stop"

# Use a unique path in the temp directory instead of a predictable C:\ root file.
$runId = [guid]::NewGuid().ToString('N')
$tempRoot = [System.IO.Path]::GetTempPath()
$bootstrapper = Join-Path $tempRoot "TypeRight-buildtools-$runId.exe"
$log = Join-Path $tempRoot "TypeRight-buildtools-$runId-install.log"
$errLog = Join-Path $tempRoot "TypeRight-buildtools-$runId-install.err"

try {
  Write-Output "[1/2] Downloading Visual Studio Build Tools 2022 bootstrapper..."
  Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vs_buildtools.exe' -OutFile $bootstrapper -UseBasicParsing
  Write-Output ("      Downloaded {0:N1} MB" -f ((Get-Item -LiteralPath $bootstrapper).Length / 1MB))

  Write-Output "[2/2] Installing C++ workload + Windows 11/10 SDK (this can take a while)..."
  $installerArgs = @(
    '--quiet','--wait','--norestart','--nocache',
    '--add','Microsoft.VisualStudio.Workload.VCTools',
    '--add','Microsoft.VisualStudio.Component.VC.Tools.x86.x64',
    '--add','Microsoft.VisualStudio.Component.Windows11SDK.22621',
    '--add','Microsoft.VisualStudio.Component.Windows10SDK.19041',
    '--includeRecommended'
  )
  $p = Start-Process -FilePath $bootstrapper -ArgumentList $installerArgs -PassThru -Wait -RedirectStandardOutput $log -RedirectStandardError $errLog
  Write-Output ("Installer exit code: " + $p.ExitCode)
  Write-Output ("Install log: " + $log)
  if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 30 }
}
finally {
  foreach ($file in @($bootstrapper, $log, $errLog)) {
    if (Test-Path -LiteralPath $file) {
      Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
    }
  }
}
