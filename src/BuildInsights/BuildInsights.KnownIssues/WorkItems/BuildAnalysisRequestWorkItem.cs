// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

namespace BuildInsights.KnownIssues.WorkItems;

public class BuildAnalysisRequestWorkItem : WorkItem
{
    public string ProjectId { get; set; }

    public int BuildId { get; set; }

    public string OrganizationId { get; set; }

    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
}
