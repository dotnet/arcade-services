// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace BuildInsights.BuildAnalysis.Models;

public class RerunCheckRunAnalysisMessage
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; }

    [JsonPropertyName("repository")]
    public string Repository { get; set; }

    [JsonPropertyName("headSha")]
    public string HeadSha { get; set; }
}
