// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.DarcLib.Models;
using Octokit;
using PullRequest = Microsoft.DotNet.DarcLib.Models.PullRequest;

namespace Microsoft.DotNet.DarcLib;
internal class GitResourceConverters
{
    internal static PullRequest ConvertPullRequest(Octokit.PullRequest pr)
    {
        var status = pr.State == ItemState.Closed ?
                    (pr.Merged == true ? PrStatus.Merged : PrStatus.Closed) :
                    PrStatus.Open;

        PullRequest result = new PullRequest
        {
            Title = pr.Title,
            Description = pr.Body,
            BaseBranch = pr.Base.Ref,
            HeadBranch = pr.Head.Ref,
            Status = status,
            UpdatedAt = pr.UpdatedAt,
            HeadCommitSha = pr.Head.Sha,
        };

        return result;
    }
    internal static GitPullRequestReviews ConvertPullRequestReviews(IEnumerable<PullRequestReview> pullRequestReviews)
    {
        IEnumerable<Review> reviews = pullRequestReviews
            .Select(r => new Review(
                TranslateReviewState(r.State.Value),
                r.PullRequestUrl,
                r.User.Login,
                r.SubmittedAt));

        return new GitPullRequestReviews
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
