// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.QueueInsights.Models;

public class QueueInsightsBetaSettings
{
    public string[] AllowedRepos { get; set; } = Array.Empty<string>();
}
