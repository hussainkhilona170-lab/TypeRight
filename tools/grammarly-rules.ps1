$path = 'C:\GrammarlyRE\desktop-payload\$LOCALAPPDATA\Grammarly\DesktopIntegrationsUpdate\Resources\Configuration\IntegrationOptions.json'
$rules = Get-Content $path -Raw | ConvertFrom-Json
Write-Output ("Total rules: " + $rules.Count)
Write-Output ("Properties: " + (($rules[0].PSObject.Properties.Name) -join ', '))
Write-Output ''
Write-Output '=== Notes categories (normalized) ==='
$norm = @{}
foreach ($r in $rules) {
  $n = [string]$r.Notes
  # strip ticket prefix like LMA-3465 / PBUG-1087
  $n = $n -replace '^[A-Z]+-\d+\s+', ''
  if (-not $n) { $n = '(empty)' }
  if (-not $norm.ContainsKey($n)) { $norm[$n] = 0 }
  $norm[$n]++
}
$norm.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 30 | ForEach-Object {
  Write-Output ("{0,4}  {1}" -f $_.Value, $_.Key)
}
Write-Output ''
Write-Output '=== Sensitivity rules by language ==='
$rules | Where-Object { $_.IsSensitivityCheck } | Group-Object Language | Sort-Object Count -Descending | ForEach-Object {
  Write-Output ("{0,4}  {1}" -f $_.Count, $_.Name)
}
Write-Output ''
Write-Output '=== Sample non-sensitivity rules ==='
$rules | Where-Object { -not $_.IsSensitivityCheck } | Select-Object -First 15 | ForEach-Object {
  Write-Output ("[{0}] enabled={1} | {2} | {3}" -f $_.Language, $_.IsEnabled, $_.ElementName, $_.Notes)
}
