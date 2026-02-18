// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

namespace BuildInsights.BuildAnalysis.WorkItems.Models;

public class KnownIssueAnalysisRequest : WorkItem
{
    public long IssueId { get; set; }

    public string Repository { get; set; }
}
