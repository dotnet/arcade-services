// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLProjectV2Items
{
    public IEnumerable<GitHubGraphQLProjectV2Item> Nodes { get; }
    public GitHubGraphQLPageInfo PageInfo { get; }

    public GitHubGraphQLProjectV2Items(IEnumerable<GitHubGraphQLProjectV2Item> nodes, GitHubGraphQLPageInfo pageInfo)
    {
        Nodes = nodes;
        PageInfo = pageInfo;
    }
}
