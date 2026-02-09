// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json.Serialization;
using BuildInsights.GitHub.Models;

namespace BuildInsights.BuildAnalysis.Models;

public class CheckRunConclusionUpdateMessage
{
    [JsonPropertyName("eventType")]
    public string EventType => "checkrun.conclusion-update";

    [JsonPropertyName("repository")]
    public string Repository { get; set; }

    [JsonPropertyName("issueNumber")]
    public int IssueNumber { get; set; }

    [JsonPropertyName("headSha")]
    public string HeadSha { get; set; }

    [JsonPropertyName("checkResult")]
    public string CheckResultString { get; set; }

    [JsonPropertyName("justification")]
    public string Justification { get; set; }

    public Octokit.CheckConclusion GetCheckConclusion()
    {
        return Enum.Parse<Octokit.CheckConclusion>(CheckResultString);
    }
}
