// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.WorkItems.Models;

public class KnownIssueValidationMessage
{
    public string Organization { get; set; }

    public string Repository { get; set; }

    public int IssueId { get; set; }

    public string RepositoryWithOwner { get; set; }
}
