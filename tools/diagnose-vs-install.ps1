Write-Output '=== OS ==='
Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber, OSArchitecture | Format-List

Write-Output '=== PENDING REBOOT ==='
$rb = @(
  'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending',
  'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired',
  'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager'
)
foreach ($p in $rb) {
  if (Test-Path $p) {
    $item = Get-ItemProperty $p -ErrorAction SilentlyContinue
    if ($p -like '*Session Manager' -and $item.PendingFileRenameOperations) { Write-Output ("PENDING (file rename): " + $p) }
    elseif ($p -notlike '*Session Manager') { Write-Output ("PENDING: " + $p) }
  }
}

Write-Output '=== WINDOWS INSTALLER SERVICE ==='
Get-Service -Name msiserver -ErrorAction SilentlyContinue | Select-Object Name, Status, StartType | Format-List

Write-Output '=== VS INSTALLER PRODUCTS ==='
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
  & $vswhere -all -products * -format json 2>&1 | Out-String | Write-Output
} else { Write-Output 'vswhere not found' }

Write-Output '=== .NET FRAMEWORK 4.x ==='
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue | Select-Object Release, Version | Format-List
