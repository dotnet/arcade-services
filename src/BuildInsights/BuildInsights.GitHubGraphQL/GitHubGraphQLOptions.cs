// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL;

public class GitHubGraphQLOptions
{
    public string Endpoint { get; set; }
    public string Token { get; set; } 
    public string InstallationRepository { get; set; }
}
