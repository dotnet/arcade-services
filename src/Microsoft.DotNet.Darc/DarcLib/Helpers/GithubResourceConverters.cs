// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.GitHub;
using Octokit;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

internal class GithubResourceConverters
{
    internal static Models.PullRequest ConvertPullRequest(Octokit.PullRequest pr)
    {
        var status = pr.State == ItemState.Closed ?
                    pr.Merged == true ? PrStatus.Merged : PrStatus.Closed :
                    PrStatus.Open;
        return new()
        {
            Title = pr.Title,
            Description = pr.Body,
            BaseBranch = pr.Base.Ref,
            HeadBranch = pr.Head.Ref,
            Status = status,
            UpdatedAt = pr.UpdatedAt,
            HeadBranchCommitSha = pr.Head.Sha,
        };
    }

    internal static GithubPullRequestReviews ConvertPullRequestReviews(IEnumerable<PullRequestReview> pullRequestReviews)
    {
        var reviews = pullRequestReviews
            .Select(r => new GithubReview(
                TranslateReviewState(r.State.Value),
                r.PullRequestUrl,
                r.User.Login,
                r.SubmittedAt))
            .ToArray();

        return new GithubPullRequestReviews
        {
            Reviews = reviews
        };
    }

    private static ReviewState TranslateReviewState(PullRequestReviewState state)
    {
        return state switch
        {
            PullRequestReviewState.Approved => ReviewState.Approved,
            PullRequestReviewState.ChangesRequested => ReviewState.ChangesRequested,
            PullRequestReviewState.Commented => ReviewState.Commented,
            // A PR comment could be dismissed by a new push, so this does not count as a rejection.
            // Change to a comment
            PullRequestReviewState.Dismissed => ReviewState.Commented,
            PullRequestReviewState.Pending => ReviewState.Pending,
            _ => throw new NotImplementedException($"Unexpected pull request review state {state}"),
        };
    }
}
