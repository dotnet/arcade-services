// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.GitHub.Models;

namespace BuildInsights.BuildAnalysis.Models.Views;

public class RetryInformationView
{
    public RetryInformationView(
        string pipelineName,
        int buildId,
        string buildNumber,
        string linkToBuild,
        GitHubIssue? gitHubIssue)
    {
        PipelineName = pipelineName;
        BuildId = buildId;
        BuildNumber = buildNumber;
        LinkToBuild = linkToBuild;

        if (gitHubIssue != null)
        {
            GithubIssueId = gitHubIssue.Id;
            GitHubIssueRepository = gitHubIssue.RepositoryWithOwner;
        }
    }

    public string PipelineName { get; }
    public int BuildId { get; }
    public string BuildNumber { get;  } //In the form 20210226.40
    public string LinkToBuild { get;  }
    public int GithubIssueId { get; }
    public string? GitHubIssueRepository { get;  }
}
