$ErrorActionPreference = "Continue"
$exe = "C:\TypeRight-buildtools.exe"
if (-not (Test-Path $exe)) {
  Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vs_buildtools.exe' -OutFile $exe -UseBasicParsing
}
$log = "C:\TypeRight-VS-install"
$args = @(
  '--passive','--wait','--norestart',
  '--add','Microsoft.VisualStudio.Workload.VCTools',
  '--add','Microsoft.VisualStudio.Component.VC.Tools.x86.x64',
  '--add','Microsoft.VisualStudio.Component.Windows11SDK.22621',
  '--includeRecommended',
  '--log', $log
)
Write-Output "Starting installer with log path $log ..."
$p = Start-Process -FilePath $exe -ArgumentList $args -PassThru -Wait
Write-Output ("Exit code: " + $p.ExitCode)

# Find and print the newest summary log
$dir = Split-Path $log -Parent
$leaf = Split-Path $log -Leaf
$candidates = Get-ChildItem $dir -Filter "$leaf*" -ErrorAction SilentlyContinue |
  Where-Object { $_.Extension -in '.log','.txt' } |
  Sort-Object LastWriteTime -Descending
if ($candidates) {
  Write-Output ("=== " + $candidates[0].FullName + " (tail) ===")
  Get-Content $candidates[0].FullName -Tail 60
} else {
  Write-Output "No summary log found."
}
Get-ChildItem $dir -Filter "$leaf*" -ErrorAction SilentlyContinue | Select-Object Name, Length | Format-Table -AutoSize
