// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLError
{
    public string Type { get; set; }
    public string[] Path { get; set; }
    public GitHubGraphQLLocation Location { get; set; }
    public string Message { get; set; }
}

public class GitHubGraphQLException: Exception
{
    public GitHubGraphQLException() 
    {
    }

    public GitHubGraphQLException(string message)
        : base(message)
    {
    }

    public GitHubGraphQLException(IEnumerable<GitHubGraphQLError> errors)
        : base(String.Join("\n", errors.Select(e => String.Format("{0}: {1}", e.Type, e.Message))))
    {
    }
}
