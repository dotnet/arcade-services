## How to create a montioring project
Use `<Project Sdk="Helix.DotNet.Monitoring.Sdk">` in a project file.

This will, by default, scan for dashboards in json files named ./dashboard/\*.dashboard.json,
and data sources named ./datasource/\*.datasource.json.

If you want to use different directories, do:
```
  <PropertyGroup>
    <DashboardDirectory>Path/To/DashboardFolder</DashboardDirectory>
    <DataSourceDirectory>Path/To/DataSourceFolder</DataSourceDirectory>
  </PropertyGroup>
```

The "GrafanaHost" and "GrafanaAccessToken" will need to be specified to either publish or import.
Either in the project file, or, more likely, during the build invocation with the /p argument.
e.g.

```bash
  dotnet build \
    -t:PublishGrafana -p:GrafanaHost=https://dotnet-eng-grafana.azurewebsites.net \
    -p:GrafanaAccessToken=GRAFANA_ADMIN_API_KEY \
    -p:GrafanaKeyVaultName=dotnet-grafana \
    -p:GrafanaKeyVaultAppId=2bdfceef-194a-4775-99d9-b5575c77bc6b \
    -p:GrafanaKeyVaultAppSecret=KEY_VAULT_APP_SECRET \
    -p:GrafanaEnvironment=DEPLOYMENT_ENVIRONMENT
```

`GrafanaAccessToken`: An API token with Admin access level
`GrafanaKeyVaultName`: The name of the Azure Key Vault where data source secrets and deployment secrets are stored
`GrafanaKeyVaultAppId`: The AppId of the this Key Vault
`GrafanaKeyVaultAppSecret`: The App Secret for this App ID
`GrafanaEnvironment`: Either "staging" or "production", the target environment for this deployment

## Publish dashboards
Ideally the publish will happen as part of publishing the services that are being monitored during a deployment.

To run the publish, run:

`dotnet build MyMonitoring.proj -t:PublishGrafana`

## Import existing dashboard
To import a dashboard, run:

```bash
  dotnet build MyMonitoring.proj -t:ImportGrafana -p:GrafanaHost=https://dotnet-eng-grafana-staging.azurewebsites.net -p:GrafanaAccessToken=MY_ACCESS_TOKEN -p:DashBoardId=MyDashboardUid
```

where 999 is the ID from grafana of the dashboard you wish to import.
It, and any referenced data sources, will be placed in the <DashboardDirectory>
