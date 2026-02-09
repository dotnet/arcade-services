// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLResponse
{
    public GitHubGraphQLData Data { get; set; }
    
    public IEnumerable<GitHubGraphQLError> Errors { get; set; }

}
