$ErrorActionPreference = "Continue"

$override = '--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 --add Microsoft.VisualStudio.Component.Windows11SDK.22621 --includeRecommended'

$wingetArgs = @(
  'install',
  '--id', 'Microsoft.VisualStudio.2022.BuildTools',
  '--exact',
  '--accept-package-agreements',
  '--accept-source-agreements',
  '--disable-interactivity',
  '--override', $override,
  '--force'
)

Write-Output ("Running: winget " + ($wingetArgs -join ' '))
& winget @wingetArgs 2>&1 | ForEach-Object { Write-Output $_ }
Write-Output ("winget exit: " + $LASTEXITCODE)
