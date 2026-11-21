$ErrorActionPreference = "Continue"

# Use a unique path in the temp directory instead of a predictable C:\ root file.
$runId = [guid]::NewGuid().ToString('N')
$tempRoot = [System.IO.Path]::GetTempPath()
$exe = Join-Path $tempRoot "TypeRight-buildtools-$runId.exe"
$log = Join-Path $tempRoot "TypeRight-VS-install-$runId"
$logDir = Split-Path -Parent $log
$logLeaf = Split-Path -Leaf $log

$installerArgs = @(
  '--passive','--wait','--norestart',
  '--add','Microsoft.VisualStudio.Workload.VCTools',
  '--add','Microsoft.VisualStudio.Component.VC.Tools.x86.x64',
  '--add','Microsoft.VisualStudio.Component.Windows11SDK.22621',
  '--includeRecommended',
  '--log', $log
)

try {
  Write-Output "Downloading Visual Studio Build Tools bootstrapper..."
  Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vs_buildtools.exe' -OutFile $exe -UseBasicParsing

  Write-Output "Starting installer with log path $log ..."
  $p = Start-Process -FilePath $exe -ArgumentList $installerArgs -PassThru -Wait
  Write-Output ("Exit code: " + $p.ExitCode)

  # Find and print the newest summary log
  $candidates = Get-ChildItem -LiteralPath $logDir -Filter "$logLeaf*" -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending
  if ($candidates) {
    Write-Output ("=== " + $candidates[0].FullName + " (tail) ===")
    Get-Content -LiteralPath $candidates[0].FullName -Tail 60
  } else {
    Write-Output "No summary log found."
  }
}
finally {
  # Clean up the downloaded bootstrapper and any logs from this run.
  if (Test-Path -LiteralPath $exe) {
    Remove-Item -LiteralPath $exe -Force -ErrorAction SilentlyContinue
  }
  Get-ChildItem -LiteralPath $logDir -Filter "$logLeaf*" -File -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
}
