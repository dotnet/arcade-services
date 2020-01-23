param(
	[Parameter(Mandatory=$True)]
	[string] $GrafanaHost,
	
	[Parameter(Mandatory=$True)]
	[string] $AccessToken,

	[Parameter(Mandatory=$True)]
	[string] $Dashboard
)

dotnet build -t:ImportGrafana "-p:GrafanaAccessToken=$AccessToken" "-p:GrafanaHost=$GrafanaHost" "-p:DashboardId=$Dashboard"