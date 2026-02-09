// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.GitHub.Models;

namespace BuildInsights.BuildAnalysis.Models;

public class BuildAutomaticRetry
{
    public bool HasRerunAutomatically { get; }
    public GitHubIssue GitHubIssue { get; }

    public BuildAutomaticRetry() { }

    public BuildAutomaticRetry(bool hasRerunAutomatically)
    {
        HasRerunAutomatically = hasRerunAutomatically;
    }
    public BuildAutomaticRetry(bool hasRerunAutomatically,GitHubIssue gitHubIssue)
    {
        HasRerunAutomatically = hasRerunAutomatically;
        GitHubIssue = gitHubIssue;
    }
}
