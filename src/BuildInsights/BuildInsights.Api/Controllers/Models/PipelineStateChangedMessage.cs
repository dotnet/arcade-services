// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace BuildInsights.Api.Controllers.Models;

/// <summary>
/// Message received from Azure DevOps when a pipeline run or stage state changes.
/// </summary>
public class PipelineStateChangedMessage : AzureDevOpsEventBase
{
    public const string RunStateChangedEventType = "ms.vss-pipelines.run-state-changed-event";
    public const string StageStateChangedEventType = "ms.vss-pipelines.stage-state-changed-event";

    [JsonPropertyName("resource")]
    public required PipelineStateChangedResource Resource { get; set; }

    public string GetProjectId()
    {
        // Example URL: https://dev.azure.com/{ORG-ID}/{PROJECT-GUID}/_apis/pipelines/{PIPELINE-ID}/runs/{BUILD-ID}
        // Example URL: https://{ORG-ID}.visualstudio.com/{PROJECT-GUID}/_apis/pipelines/{PIPELINE-ID}/runs/{BUILD-ID}

        Uri buildUri = ValidateBuildUri();

        int projectGuidSegment = buildUri.Host.StartsWith("dev.azure") ? 2 : 1;
        string potentialProjectGuid = buildUri.Segments[projectGuidSegment][..^1];

        if (buildUri.Segments[projectGuidSegment + 1] != "_apis/" ||
            buildUri.Segments[projectGuidSegment + 2] != "pipelines/" ||
            buildUri.Segments[projectGuidSegment + 4] != "runs/")
        {
            throw new InvalidOperationException("Project resource does not appear to point to the _apis/pipelines/runs REST API");
        }

        if (!Guid.TryParse(potentialProjectGuid, out _))
        {
            throw new InvalidOperationException("Project resource URL does not appear to contain the project GUID");
        }

        return potentialProjectGuid;
    }

    public string GetOrgId()
    {
        // Example URL: https://dev.azure.com/{ORG-ID}/{PROJECT-GUID}/_apis/pipelines/{PIPELINE-ID}/runs/{BUILD-ID}
        // Example URL: https://{ORG-ID}.visualstudio.com/{PROJECT-GUID}/_apis/pipelines/{PIPELINE-ID}/runs/{BUILD-ID}

        Uri buildUri = ValidateBuildUri();

        int orgIdSegment = buildUri.Host.StartsWith("dev.azure.") ? 1 : 0;
        string potentialOrgId = orgIdSegment == 0 ? buildUri.Host.Split('.')[0] : buildUri.Segments[orgIdSegment][..^1];

        if (buildUri.Segments[orgIdSegment + 2] != "_apis/" ||
            buildUri.Segments[orgIdSegment + 3] != "pipelines/" ||
            buildUri.Segments[orgIdSegment + 5] != "runs/")
        {
            throw new InvalidOperationException("Org resource does not appear to point to the _apis/pipelines/runs REST API");
        }

        return potentialOrgId;
    }

    private Uri ValidateBuildUri()
    {
        var buildUri = new Uri(Resource.Url);

        if (!buildUri.Host.StartsWith("dev.azure") && !buildUri.Host.Contains(".visualstudio."))
        {
            throw new InvalidOperationException("Project resource URL does not appear to be for dev.azure.com or *.visualstudio");
        }

        return buildUri;
    }
}

public class PipelineStateChangedResource
{
    [JsonPropertyName("runId")]
    public int Id { get; set; }

    [JsonPropertyName("runUrl")]
    public required string Url { get; set; }
}
