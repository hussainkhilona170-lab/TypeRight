$tools = @('cl.exe','clang-cl.exe','clang.exe','g++.exe','rc.exe','midl.exe','msbuild.exe','dotnet.exe','regsvr32.exe')
Write-Output '=== TOOLS ON PATH ==='
foreach ($t in $tools) {
  $c = Get-Command $t -ErrorAction SilentlyContinue
  if ($c) { Write-Output ("{0,-16} {1}" -f $t, $c.Source) }
  else { Write-Output ("{0,-16} NOT FOUND" -f $t) }
}

Write-Output ''
Write-Output '=== VISUAL STUDIO ==='
$vsPaths = @("C:\Program Files\Microsoft Visual Studio", "C:\Program Files (x86)\Microsoft Visual Studio")
foreach ($p in $vsPaths) {
  if (Test-Path $p) { Write-Output ("FOUND: " + $p); Get-ChildItem $p -Name | ForEach-Object { Write-Output ("   " + $_) } }
  else { Write-Output ("NOT FOUND: " + $p) }
}

Write-Output ''
Write-Output '=== WINDOWS SDK ==='
$sdkPaths = @(
  "C:\Program Files\Windows Kits\10\Include",
  "C:\Program Files (x86)\Windows Kits\10\Include",
  "C:\Program Files\Windows Kits\10\Lib",
  "C:\Program Files (x86)\Windows Kits\10\Lib"
)
foreach ($p in $sdkPaths) {
  if (Test-Path $p) { Write-Output ("FOUND: " + $p); Get-ChildItem $p -Name | ForEach-Object { Write-Output ("   " + $_) } }
  else { Write-Output ("NOT FOUND: " + $p) }
}

Write-Output ''
Write-Output '=== dotnet SDKs ==='
$dotnet = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
  $localDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
  if (Test-Path $localDotnet) { $dotnet = $localDotnet }
}
if ($dotnet) { & $dotnet --list-sdks } else { Write-Output 'dotnet SDK not found' }
