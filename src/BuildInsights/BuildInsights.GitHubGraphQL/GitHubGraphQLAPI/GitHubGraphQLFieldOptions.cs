// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLFieldOption
{
    public string Id { get; }
    public string Name { get; }

    public GitHubGraphQLFieldOption(string id, string name)
    {
        Id = id;
        Name = name;
    }
}
