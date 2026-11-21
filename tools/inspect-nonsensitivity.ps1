$repoRoot = Split-Path -Parent $PSScriptRoot
$p = Join-Path $repoRoot 'src\TypeRight.Privacy\IntegrationOptions.json'
$rules = [System.IO.File]::ReadAllText($p, (New-Object System.Text.UTF8Encoding $false)) | ConvertFrom-Json
$ns = $rules | Where-Object { -not $_.IsSensitivityCheck }
Write-Output ("Non-sensitivity rules: " + $ns.Count)
Write-Output ("Enabled: " + ($ns | Where-Object { $_.IsEnabled -eq $true }).Count)
Write-Output ''
Write-Output '=== Enabled non-sensitivity rules ==='
$ns | Where-Object { $_.IsEnabled -eq $true } | Select-Object -First 40 | ForEach-Object {
  Write-Output ("[{0}] {1} | {2}" -f $_.Language, $_.ElementName, $_.Notes)
}
