param(
  [string]$Source = (Join-Path $env:LOCALAPPDATA 'Grammarly\DesktopIntegrationsUpdate\Resources\Configuration\IntegrationOptions.json')
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$dst = Join-Path $repoRoot 'src\TypeRight.Privacy\IntegrationOptions.json'

if (-not (Test-Path -LiteralPath $Source)) {
  throw "Integration rules source not found: $Source (pass -Source <path> to override)"
}

$item = Get-Item -LiteralPath $Source
Write-Output ("Source size: {0:N1} KB" -f ($item.Length / 1KB))

$utf8 = New-Object System.Text.UTF8Encoding $false
$raw = [System.IO.File]::ReadAllText($Source, $utf8)
$rules = $raw | ConvertFrom-Json

# Copy the full rule set verbatim in shape and content. TypeRight consumes the
# same fields: ElementName, IsEnabled, Language, IsSensitivityCheck, Notes.
$json = $rules | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($dst, $json, $utf8)

$outItem = Get-Item -LiteralPath $dst
Write-Output ("Wrote {0} rules ({1:N1} KB) to {2}" -f $rules.Count, ($outItem.Length / 1KB), $dst)
