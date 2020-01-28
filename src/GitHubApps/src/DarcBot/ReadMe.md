DarcBot is a GitHub app used to categorize Azure DevOps build failures and link build failures to GitHub issues.

## Background

We generate [PowerBi reports](https://dev.azure.com/dnceng/public/_dashboards/dashboard/40ac4990-3498-4b3a-85dd-2ffde961d672), that include [telemetry categorization](https://github.com/dotnet/arcade/blob/master/Documentation/Projects/DevOps/CI/Telemetry-Guidance.md).  The reports are useful to show build metrics / trends.  However, there is no way to discuss failures via these reports alone and if the categorization is incorrect, there is no way to recategorize a failure and improve our reports for historical failures (see this [doc](https://github.com/dotnet/arcade/blob/master/Documentation/Projects/PKPIs/Triage-Design.md) for more information).

## DarcBot issue identification

DarcBot searches GitHub issues for issues which contain specific identifiable information in the body.

```text
[BuildId=(Azure DevOps BuildId),RecordId=(Azure DevOps RecordId),Index=(Azure DevOps issue index)]
```

It is not expected that users manually provide this information, the issue body is auto-generated via links on the reports (see https://github.com/dotnet/arcade/blob/master/Documentation/Projects/PKPIs/Triage-Design.md#powerbi).

When a DarcBot issue is detected, DarcBot will link the GitHub issue to the failure in the PowerBI report (see [Behavior notes](#behavior-notes)).

Example:

```text
[BuildId=454812,RecordId=06716121-e960-5216-6960-25973794f7db,Index=10]

```

## Re-categorizing

To change the category of an issue, add a comment with the following text included.

```text
[Category=(desired category)]
```

The category for the build failure will update only when the issue is `Closed`, and the last commented "category" metadata is always used.

Example:

```text
[Category=Build]
```

# Behavior notes

- When you create a DarcBot issue, the category for the build failure is automatically changed to "InTriage" and will remain as that category until the issue is "Closed".  When the issue is closed, DarcBot will update the category with the last commented categorization in the issue.

- If you open an issue that refers to an already open build [failure](#darcbot-issue-identification), DarcBot will close the new issue with a link to the oldest `Open` issue that refers to the same failure.

- DarcBot immediately updates the underlying database that defines build failure categorization and GitHub issue links, but PowerBI reports only refresh twice a day so it may take hours before your changes are reflected in the report (manually scheduling a refresh will shortcut the wait time).
