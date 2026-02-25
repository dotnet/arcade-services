// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Octokit;
using ProductConstructionService.WorkItems;

#nullable disable
namespace BuildInsights.BuildAnalysis.WorkItems.Models;

public class CheckRunConclusionUpdateEvent : WorkItem
{
    public string Repository { get; set; }

    public int IssueNumber { get; set; }

    public string HeadSha { get; set; }

    public string CheckResultString { get; set; }

    public string Justification { get; set; }

    public CheckConclusion GetCheckConclusion()
    {
        return Enum.Parse<CheckConclusion>(CheckResultString);
    }
}
