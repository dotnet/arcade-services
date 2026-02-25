// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace BuildInsights.GitHub.Models;

public class KnownIssueUrlOptions
{
    public string Host { get; set; }
    public IssueParameters InfrastructureIssueParameters { get; set; }
    public IssueParameters RepositoryIssueParameters { get; set; }
}

public class IssueParameters
{
    public string GithubTemplateName { get; set; }
    public List<string> Labels { get; set; }
    public string Repository { get; set; }
}
