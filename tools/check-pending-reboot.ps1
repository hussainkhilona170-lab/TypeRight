$p = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager'
$item = Get-ItemProperty $p -ErrorAction SilentlyContinue
$ops = $item.PendingFileRenameOperations
Write-Output ("PendingFileRenameOperations entries: " + @($ops).Count)
@($ops) | Select-Object -First 40 | ForEach-Object { Write-Output ("  " + $_) }
