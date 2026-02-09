// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class BuildFromGitHubIssue
{
    public string OrganizationId { get; }
    public string ProjectId { get; }
    public int Id { get; }

    public BuildFromGitHubIssue(string organizationId, string projectId, int buildId)
    {
        OrganizationId = organizationId;
        ProjectId = projectId;
        Id = buildId;
    }
}
