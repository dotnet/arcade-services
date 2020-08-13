param(
  [Parameter(Mandatory = $True)]
  [string]
  $AppInsightsKey
)
Start-Transcript -Path "$PSScriptRoot\transcript.log"

setx APPLICATION_INSIGHTS_KEY "$AppInsightsKey"  /M

.\Set-TlsConfiguration.ps1

reg.exe IMPORT "$PSScriptRoot\enable-dumps.reg"
