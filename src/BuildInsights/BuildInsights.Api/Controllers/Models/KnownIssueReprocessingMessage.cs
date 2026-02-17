// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace BuildInsights.Api.Controllers.Models;

/// <summary>
/// Message for reprocessing a build analysis due to known issue changes.
/// </summary>
public class KnownIssueReprocessingMessage : AzureDevOpsEventBase
{
    public const string MessageEventType = "knownissue.reprocessing";

    [JsonPropertyName("projectId")]
    public required string ProjectId { get; set; }

    [JsonPropertyName("buildId")]
    public int BuildId { get; set; }

    [JsonPropertyName("organizationId")]
    public required string OrganizationId { get; set; }
}
