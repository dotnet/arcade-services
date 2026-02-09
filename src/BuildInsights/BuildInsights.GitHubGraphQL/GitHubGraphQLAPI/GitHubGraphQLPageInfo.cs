// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLPageInfo
{
    public bool HasNextPage { get; set; }
    public string EndCursor { get; set; }
}
