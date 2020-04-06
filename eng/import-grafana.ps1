param(
	[switch]$Production,
	[switch]$Staging,
	
	[Parameter(ValueFromRemainingArguments)]
	[string[]]$Dashboards
)

function Do-Import([string]$vault, [string]$hostSuffix, [string]$dash){
	$key = (Get-AzureKeyVaultSecret -VaultName $vault -Name grafana-admin-api-key).SecretValueText
	dotnet build -t:ImportGrafana "-p:GrafanaAccessToken=$key" "-p:GrafanaHost=https://dotnet-eng-grafana$hostSuffix.westus2.cloudapp.azure.com" -v:normal "-p:DashboardId=$dash"
}

if ($Production) {
	$Dashboards |% { Do-Import dotnet-grafana "" $_ }
}

if ($Staging) {
	$Dashboards |% { Do-Import dotnet-grafana-staging "-staging" $_ }
}