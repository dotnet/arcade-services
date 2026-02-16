// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json.Serialization;

#nullable disable
namespace BuildInsights.Utilities.AzureDevOps.Models;

public class WorkItemCreatedMessage
{
    [JsonPropertyName("resource")]
    public WorkItemCreatedResource Resource { get; set; }

    [JsonPropertyName("resourceContainers")]
    public BuildCompletedResourceContainers ResourceContainers { get; set; }
}

public class WorkItemCreatedResource
{
    [JsonPropertyName("fields")]
    public WorkItemFields Fields { get; set; }

    [JsonPropertyName("_links")]
    public WorkItemCreatedLinks Links { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }

    private string _orgId;
    public string OrgId
    {
        get => _orgId ?? GetOrgId();
        set { _orgId = value; }
    }

    private string GetOrgId()
    {
        // Regex to validate Url
        // Example URL: https://dev.azure.com/{ORG-ID}/{PROJECT-GUID}/_apis/wit/workItems/{ID}
        // Example URL: https://{ORG-ID}.visualstudio.com/{PROJECT-GUID}/_apis/wit/workItems/{ID}

        Uri buildUri = new Uri(Url);

        if (!buildUri.Host.StartsWith("dev.azure.") && !buildUri.Host.Contains(".visualstudio."))
        {
            throw new InvalidOperationException("Resource URL does not appear to be for dev.azure.com or *.visualstudio");
        }

        int orgIdSegment = buildUri.Host.StartsWith("dev.azure.") ? 1 : 0;
        string potentialOrgId = orgIdSegment == 0 ? buildUri.Host.Split('.')[0] : buildUri.Segments[orgIdSegment][..^1];

        if (buildUri.Segments[orgIdSegment + 2] != "_apis/" ||
            buildUri.Segments[orgIdSegment + 3] != "wit/" ||
            buildUri.Segments[orgIdSegment + 4] != "workItems/")
        {
            throw new InvalidOperationException(
                "Org resource does not appear to point to the _apis/wit/workItems REST API"
            );
        }

        return potentialOrgId;
    }
}

public class WorkItemFields
{
    [JsonPropertyName("System.Description")]
    public string Description { get; set; }

    [JsonPropertyName("Custom.GithubFriendlyTitle")]
    public string GitHubFriendlyTitle { get; set; }

    [JsonPropertyName("Custom.GitHubFriendlyDescription")]
    public string GitHubFriendlyDescription { get; set; }

    [JsonPropertyName("Custom.EpicIssue")]
    public string EpicIssue { get; set; }
}

public class WorkItemCreatedLinks
{
    [JsonPropertyName("html")]
    public WorkItemCreatedLink Html { get; set; }
}

public class WorkItemCreatedLink
{
    [JsonPropertyName("href")]
    public Uri Href { get; set; }
}
