// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLNode
{
    public GitHubGraphQLFields Fields { get; }

    public GitHubGraphQLNode(GitHubGraphQLFields fields)
    {
        Fields = fields;
    }
}
