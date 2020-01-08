# How to Deploy Dashboards

## Background

At its heart, Grafana stores most of its configuration as JSON objects.

Grafana organizes features thus:

A Grafana instance is made of one or more Organizations. It may also contain zero or more Annotations.

Organizations hold one or more Data Sources and Dashboards.

A Dashboard contains one or more Panels and zero or more Variables. It may also contain zero or more Annotations.

A Panel may contain zero or more Alerts. It may also contain zero or more Annotations.

## Procedure

1. Use a Grafana instance to configure the dashboards

Create a dashboard as you normally would. See the [Grafana Documentation](https://grafana.com/docs/guides/basic_concepts/).

2. Use the `MakeExportPack` tool to extract the dashboard configuration information into a file suitable for deployment

The DeployTool accepts a list of Dashboard UIDs. It will retrieve the definitions for these Dashboards, the Folders they live in, and any Data Sources into a JSON document custom for this purpose.

3. Update the secret definitions in the deployment file

Secret values, for example those used in data source definitions, are emitted in the form of `[vault(secret-name)]`, where `secret-name` is a name inferred from the API. This name must be changed to match the name of a secret in Azure Key Vault.

## Deploy Pack JSON format

A JSON schema is defined in an adjacent file.

The intent of a single JSON document is to ensure that all components necessary for clean deployment are present.

Much of this file takes advantage of the fact that the Grafana API exchanges data in JSON blobs.

The `MakeExportPack` method accepts a list of dashboards `uid`s. From this, it determines the folders and data sources used by these dashboards. All of this is sanitized and combined into the output file.

Some fields are provided by the API but are not safe to use for deployment. For example, many `id` fields are incrementing integers that must be unique for an instance of the Grafana server. The Grafana API has adopted the use of `uid` fields to provide a safe, cross-deployment, cross-server means to identify a resource. Links to dashboards and folders using the `uid` do not change.

The tool removes the `id` and `version` fields from a dashboard definition.

The tool keeps only the `uid` and `title` fields from a folder definition.

The tool removes the `id`, `orgId` and `url` fields from a data source definition. Data sources do not have `uid` fields, instead using the `name` field in a similar way.

### Secrets

Data source objects have two fields related to secret values. The field `secureJsonFields` is an object of key-value pairs where the key is the name of a field that contain secret information and the value is simply `true`. This field is provided by the API as a signal of which fields contain secrets, but the secrets themselves are not included.

The field `secureJsonData` is an object of key-value pairs where they is the name of a field and the value is the secret. This is used when creating objects.

The tool uses information from `secureJsonFields` to create the `secureJsonData` field. Since actual secret data is not available, it inserts a Vault reference placeholder. This should be modified to the correct reference.

For example, the Azure Monitor data source has two secrets: the Client Secret, called `clientSecret` and the Application Insights API Key, called `appInsightsApiKey`.

```json
"secureJsonFields": {
    "appInsightsApiKey": true,
    "clientSecret": true
}
```

The tool emits this:

```json
"secureJsonData": {
    "appInsightsApiKey": "[vault(appInsightsApiKey)]",
    "clientSecret": "[vault(clientSecret)]"
}
```

It is expected that these values be updated to correspond with the correct Key Vault secret name. For example:

```json
"secureJsonData": {
    "appInsightsApiKey": "[vault(grafana-datasource-ai-dotnet-eng-int-client-secret)]",
    "clientSecret": "[vault(helix-grafana-ad-client-secret)]"
}
```
