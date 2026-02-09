// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLData
{
    public GitHubGraphQLQuery Organization { get; set; }
    public GitHubGraphQLMutation AddProjectV2ItemById { get; set; }
    public GitHubGraphQLMutation DeleteProjectV2Item { get; set; }
    public GitHubGraphQLMutation UpdateProjectNextV2Field { get; set; }
    public GitHubGraphQLProjectV2 Node { get; set; }
    public GitHubGraphQLRepository Repository { get; set; }
}
