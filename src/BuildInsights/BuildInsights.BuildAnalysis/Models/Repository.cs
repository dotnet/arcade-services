// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class Repository
{
    public string Id { get; }
    public bool HasIssues { get; }

    public Repository(string repositoryId, bool hasIssues)
    {
        Id = repositoryId;
        HasIssues = hasIssues;
    }
}
