param(
  [Parameter(Mandatory = $True)]
  [string]
  $AppInsightsKey
)

setx APPLICATION_INSIGHTS_KEY "$AppInsightsKey"  /M

.\Set-TlsConfiguration.ps1
