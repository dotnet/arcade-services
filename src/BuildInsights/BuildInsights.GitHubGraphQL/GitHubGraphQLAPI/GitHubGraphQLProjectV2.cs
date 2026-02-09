// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLProjectV2
{
    public string Title { get; set; }
    public string Id { get; set; }
    public GitHubGraphQLFields Fields { get; set; }
    public GitHubGraphQLProjectV2Items Items { get; set; }
}
