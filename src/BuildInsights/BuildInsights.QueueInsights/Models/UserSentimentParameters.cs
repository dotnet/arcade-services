// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.QueueInsights.Models;

public class UserSentimentParameters
{
    public UserSentimentParameters(string repository, string commitHash, string pullRequest, bool wasPending)
    {
        Repository = repository;
        CommitHash = commitHash;
        PullRequest = pullRequest;
        WasPending = wasPending;
    }

    public string Repository { get; set; }
    public string CommitHash { get; set; }
    public string PullRequest { get; set; }
    public bool WasPending { get; set; }
}
