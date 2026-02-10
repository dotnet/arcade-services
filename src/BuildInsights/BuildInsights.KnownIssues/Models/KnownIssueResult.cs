// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.KnownIssues.Models;

public class KnownIssueResult
{
    public int IssueId { get; set; }
    public string IssueRepository { get; set; }
    public KnownIssuesHits KnownIssuesHits { get; set; }
    public List<string> Labels { get; set; }
}
