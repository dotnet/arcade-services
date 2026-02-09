// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLFields
{
    public IEnumerable<GitHubGraphQLField> Nodes { get; }

    public GitHubGraphQLFields(IEnumerable<GitHubGraphQLField> nodes)
    {
        Nodes = nodes;
    }
}
