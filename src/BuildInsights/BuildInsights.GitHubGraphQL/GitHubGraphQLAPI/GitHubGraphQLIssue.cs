// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLIssue
{
    public string Id { get; set; }
    public int Number { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public string Url { get;set; }
    public bool Closed { get; set; }
    public GitHubGraphQLUser Author { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public GitHubGraphQLTracked TrackedInIssues { get; set; }
    public GitHubGraphQLLabels Labels { get; set; }
    public GitHubGraphQLRepository Repository { get; set; }
    public GitHubGraphQLProjectsV2 ProjectsV2 { get; set; }
}
