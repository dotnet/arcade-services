// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

#nullable disable
namespace BuildInsights.BuildAnalysis.WorkItems.Models;

public class KnownIssueValidationRequest : WorkItem
{
    public string Organization { get; set; }

    public string Repository { get; set; }

    public long IssueId { get; set; }

    public string RepositoryWithOwner { get; set; }
}
