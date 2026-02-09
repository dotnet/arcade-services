// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLProjectsV2
{
    public IEnumerable<GitHubGraphQLProjectV2> Nodes { get; }

    public GitHubGraphQLProjectsV2(IEnumerable<GitHubGraphQLProjectV2> nodes)
    {
        Nodes = nodes;
    }
}
