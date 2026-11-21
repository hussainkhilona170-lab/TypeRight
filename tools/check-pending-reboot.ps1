$p = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager'
$item = Get-ItemProperty $p -ErrorAction SilentlyContinue
$ops = $item.PendingFileRenameOperations

# @($null).Count is 1 in PowerShell; treat a missing/null value as zero entries.
$entries = @()
if ($null -ne $ops) {
  $entries = @($ops | Where-Object { -not [string]::IsNullOrEmpty($_) })
}

Write-Output ("PendingFileRenameOperations entries: " + $entries.Count)
$entries | Select-Object -First 40 | ForEach-Object { Write-Output ("  " + $_) }
