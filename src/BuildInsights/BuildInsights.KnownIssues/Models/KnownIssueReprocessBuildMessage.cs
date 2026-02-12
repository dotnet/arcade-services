// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace BuildInsights.KnownIssues.Models;

public class KnownIssueReprocessBuildMessage
{
    [JsonPropertyName("eventType")]
    public static string EventType => "knownissue.reprocessing";
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; }
    [JsonPropertyName("buildId")]
    public int BuildId { get; set; }
    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; }
}
