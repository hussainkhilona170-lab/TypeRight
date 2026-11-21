Write-Output '=== PACKAGE MANAGERS ==='
foreach ($t in @('winget.exe','choco.exe','scoop.cmd')) {
  $c = Get-Command $t -ErrorAction SilentlyContinue
  if ($c) { Write-Output ("{0,-14} {1}" -f $t, $c.Source) } else { Write-Output ("{0,-14} NOT FOUND" -f $t) }
}

Write-Output ''
Write-Output '=== NETWORK (to Visual Studio download) ==='
try {
  $r = Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vs_buildtools.exe' -Method Head -TimeoutSec 12 -UseBasicParsing
  Write-Output ("HTTP " + [int]$r.StatusCode + "  Length=" + $r.Headers['Content-Length'])
} catch {
  Write-Output ("NETWORK ERROR: " + $_.Exception.Message)
}

Write-Output ''
Write-Output '=== DISK SPACE (C:) ==='
try {
  $d = Get-PSDrive C
  Write-Output ("Free: {0:N1} GB / Used: {1:N1} GB" -f ($d.Free/1GB), ($d.Used/1GB))
} catch { Write-Output $_.Exception.Message }

Write-Output ''
Write-Output '=== ADMIN ==='
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$p = New-Object Security.Principal.WindowsPrincipal($id)
Write-Output ("User: " + $id.Name)
Write-Output ("IsAdmin: " + $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
