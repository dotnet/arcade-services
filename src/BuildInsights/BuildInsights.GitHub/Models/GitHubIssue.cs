// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.GitHub.Models;

public class GitHubIssue
{
    public int Id { get; }
    public string Repository { get; }
    public string RepositoryWithOwner { get; }
    public string Body { get; }
    public List<string> Labels { get; }
    public string Title { get; }
    public string LinkGitHubIssue { get; }

    public GitHubIssue(
        int id = default,
        string title = default,
        string repository = default,
        string repositoryWithOwner = default,
        string body = default,
        string linkGitHubIssue = default,
        List<string> labels = default)
    {
        Id = id;
        Title = title;
        Repository = repository;
        RepositoryWithOwner = repositoryWithOwner;
        Body = body;
        LinkGitHubIssue = linkGitHubIssue;
        Labels = labels;
    }
}

public class GitHubIssueComparer : IEqualityComparer<GitHubIssue>
{
    public bool Equals(GitHubIssue x, GitHubIssue y)
    {
        if (ReferenceEquals(x, y)) return true;

        if (x == null || y == null) return false;

        return x.Id.Equals(y.Id) &&
               ((x.Repository != null && x.Repository.Equals(y.Repository)) ||
                (x.RepositoryWithOwner != null && x.RepositoryWithOwner.Equals(y.RepositoryWithOwner))) &&
               x.LinkGitHubIssue.Equals(y.LinkGitHubIssue);
    }

    public int GetHashCode(GitHubIssue obj)
    {
        return HashCode.Combine(obj.Id, obj.Repository, obj.RepositoryWithOwner, obj.LinkGitHubIssue);
    }
}
