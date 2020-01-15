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
Either in the project file, or, more likely, during the build invokation with the /p argument.
e.g. `dotnet build -t:PublishGrafana /p:GrafanaHost=https://dotnet-eng-grafana.azurewebsites.net /p:GrafanaAccessToken=MY_ACCESS_TOKEN`

## Publish dashboards
Ideally the publish will happen as part of publishing the services that are being monitored during a deployment.

To run the publish, run:

`dotnet build MyMonitoring.proj -t:PublishGrafana`

## Import existing dashboard
To import a dashboard, run:

`dotnet build MyMonitoring.proj -t:ImportGrafana /p:DashBoardId=999`

where 999 is the ID from grafana of the dashboard you wish to import.
It, and any referenced data sources, will be placed in the <DashboardDirectory>
