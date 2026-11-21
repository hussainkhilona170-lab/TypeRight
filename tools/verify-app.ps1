Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class WinApi2 {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
"@

$procs = @(Get-Process -Name TypeRight -ErrorAction SilentlyContinue)
Write-Output "TypeRight process count: $($procs.Count)"
foreach ($p in $procs) {
  Write-Output ("  PID {0}  Responding={1}  MainWindowTitle='{2}'" -f $p.Id, $p.Responding, $p.MainWindowTitle)
}

Write-Output ""
Write-Output "Visible top-level windows owned by TypeRight:"
$targetIds = @($procs | ForEach-Object { [uint32]$_.Id })
$cb = [WinApi2+EnumWindowsProc]{
  param($hWnd, $lParam)
  [uint32]$procId = 0
  [WinApi2]::GetWindowThreadProcessId($hWnd, [ref]$procId) | Out-Null
  if ($targetIds -contains $procId -and [WinApi2]::IsWindowVisible($hWnd)) {
    $sb = New-Object System.Text.StringBuilder 512
    [WinApi2]::GetWindowText($hWnd, $sb, 512) | Out-Null
    if ($sb.Length -gt 0) {
      Write-Output ("  hWnd={0} PID={1} Title='{2}'" -f $hWnd, $procId, $sb.ToString())
    }
  }
  return $true
}
[WinApi2]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
