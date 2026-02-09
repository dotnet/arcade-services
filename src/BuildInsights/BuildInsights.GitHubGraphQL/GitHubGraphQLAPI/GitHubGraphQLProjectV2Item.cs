// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLProjectV2Item
{
    public string Id  { get; set; }
    public bool IsArchived { get; set; }
    public GitHubGraphQLProjectV2 ProjectV2 { get; set; }
    public GitHubGraphQLIssue Content { get; set; }
    public GitHubGraphQLFieldValues FieldValues { get; set; }
}
