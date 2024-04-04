param(
  [Parameter(Mandatory = $True)]
  [string]
  $AppInsightsConnectionString
)

setx APPLICATION_INSIGHTS_CONNECTION_STRING "$AppInsightsConnectionString" /M

.\Set-TlsConfiguration.ps1
