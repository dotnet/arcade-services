// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

#nullable disable
namespace BuildInsights.BuildAnalysis.WorkItems.Models;

public class CheckRunRerunGitHubEvent : WorkItem
{
    public string Repository { get; set; }

    public string HeadSha { get; set; }

    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
}
