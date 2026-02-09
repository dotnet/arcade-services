// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLField
{
    public string Id { get; set; }
    public string Name { get; set; }
    public IEnumerable<GitHubGraphQLFieldOption> Options { get; set; }
}
