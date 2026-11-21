$ErrorActionPreference = "Stop"
$src = 'C:\GrammarlyRE\desktop-payload\$LOCALAPPDATA\Grammarly\DesktopIntegrationsUpdate\Resources\Configuration\IntegrationOptions.json'
$dst = 'C:\Users\Administrator\Downloads\TypeRight!\src\TypeRight.Privacy\IntegrationOptions.json'

$item = Get-Item $src
Write-Output ("Source size: {0:N1} KB" -f ($item.Length / 1KB))

$utf8 = New-Object System.Text.UTF8Encoding $false
$raw = [System.IO.File]::ReadAllText($src, $utf8)
$rules = $raw | ConvertFrom-Json

# Copy the full rule set verbatim in shape and content. TypeRight consumes the
# same fields: ElementName, IsEnabled, Language, IsSensitivityCheck, Notes.
$json = $rules | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($dst, $json, $utf8)

$outItem = Get-Item $dst
Write-Output ("Wrote {0} rules ({1:N1} KB) to {2}" -f $rules.Count, ($outItem.Length / 1KB), $dst)
