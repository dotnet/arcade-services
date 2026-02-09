// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace BuildInsights.KnownIssues.Models;

public class GitHubIssuesSettings
{
    public IEnumerable<string> CriticalIssuesRepositories { get; set; }
    public IEnumerable<string> CriticalIssuesLabels { get; set; }
    public IEnumerable<string> KnownIssuesRepositories { get; set; }
    public IEnumerable<string> KnownIssuesLabels { get; set; }
}
