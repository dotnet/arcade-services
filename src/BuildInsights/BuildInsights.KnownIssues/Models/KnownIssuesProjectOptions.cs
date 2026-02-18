// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.KnownIssues.Models;

public class KnownIssuesProjectOptions
{
    public int ProjectNumber { get; set; }
    public string Organization { get; set; }
    public string[] KnownIssueLabels { get; set; }
    public string[] CriticalIssueLabels { get; set; }
    public string IssueTypeField { get; set; }
}
