// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json.Serialization;

#nullable disable
namespace BuildInsights.Utilities.AzureDevOps.Models;

public class CompletedBuildMessage : AzureDevOpsEventBase
{
    [JsonPropertyName("resource")]
    public BuildCompletedResource Resource { get; set; }

    [JsonPropertyName("resourceContainers")]
    public BuildCompletedResourceContainers ResourceContainers { get; set; }
}

public class BuildCompletedResource
{
    public BuildCompletedResource(int id)
    {
        Id = id;
    }

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("orgId")]
    public string OrgId => GetOrgId();

    private string GetOrgId()
    {
        // Regex to validate Url
        // Example URL: https://dev.azure.com/{ORG-ID}/{PROJECT-GUID}/{PIPELINE-iD}/_apis/build-release/Builds/{BUILD-ID}
        // Example URL: https://{ORG-ID}.visualstudio.com/{PROJECT-GUID}/{PIPELINE-iD}/_apis/build-release/Builds/{BUILD-ID}

        Uri buildUri = new(Url);

        if (!buildUri.Host.StartsWith("dev.azure") && !buildUri.Host.Contains(".visualstudio."))
        {
            throw new InvalidOperationException("Resource URL does not appear to be for dev.azure.com or *.visualstudio");
        }

        int orgIdSegment = buildUri.Host.StartsWith("dev.azure.") ? 1 : 0;
        string potentialOrgId = orgIdSegment == 0 ? buildUri.Host.Split('.')[0] : buildUri.Segments[orgIdSegment][..^1];

        if (buildUri.Segments[orgIdSegment + 2] != "_apis/" ||
            buildUri.Segments[orgIdSegment + 3] != "build/" ||
            buildUri.Segments[orgIdSegment + 4] != "Builds/")
        {
            throw new InvalidOperationException(
                "Org resource does not appear to point to the _apis/build/Builds REST API"
            );
        }

        return potentialOrgId;
    }
}

public class BuildCompletedResourceContainers
{
    [JsonPropertyName("project")]
    public BuildCompletedProject Project { get; set; }
}

public class BuildCompletedProject
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
}
