// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHub.Models;

public class GitHubBuildReference
{
    public string Owner { get; set; }
    public string RepositoryName { get; set; }
    public string PrSourceSha { get; set; }
    public string RepositoryId { get; set; }
}
