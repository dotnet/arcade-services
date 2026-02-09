// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLMutation
{
    public GitHubGraphQLProjectV2Item Item { get; set; }
    public GitHubGraphQLProjectV2Item ProjectV2Item { get; set; }
    public string DeletedItemId { get; set; }
}
