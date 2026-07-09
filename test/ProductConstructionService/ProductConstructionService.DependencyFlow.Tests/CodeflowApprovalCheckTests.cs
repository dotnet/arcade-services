// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.Tests;

/// <summary>
/// Covers the auto-approval behavior of forward-flow code flow PRs. This behavior is
/// security-sensitive because a passing check produces an approving review, so we assert
/// that an approval is requested only when the PR's diff matches the expected source diff.
/// </summary>
[TestFixture, NonParallelizable, Ignore("TODO https://github.com/dotnet/arcade-services/issues/6482")]

internal class CodeflowApprovalCheckTests : UpdateAssetsPullRequestUpdaterTests
{
    private const string PreviousSourceSha = "previous.source.sha";

    [Test]
    public async Task ApprovesPullRequestWhenTheDiffMatches()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild,
            },
            autoApprove: true);
        Build build = GivenANewBuild(true);

        GivenAnOpenInProgressCodeFlowPullRequest(build);
        GivenThePullRequestOnlyHasBotCommits(VmrPullRequestUrl);
        GivenTheForwardFlowMatchesTheSourceDiff(true);

        await WhenRunCodeflowApprovalCheckAsyncIsCalled(GivenACodeflowApprovalCheck(build));

        ThenThePullRequestShouldHaveBeenApproved(VmrPullRequestUrl);
        ThenTheInProgressPullRequestWasChecked();
    }

    [Test]
    public async Task DoesNotApprovePullRequestWhenTheDiffDoesNotMatch()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild,
            },
            autoApprove: true);
        Build build = GivenANewBuild(true);

        GivenAnOpenInProgressCodeFlowPullRequest(build);
        GivenThePullRequestOnlyHasBotCommits(VmrPullRequestUrl);
        GivenTheForwardFlowMatchesTheSourceDiff(false);

        await WhenRunCodeflowApprovalCheckAsyncIsCalled(GivenACodeflowApprovalCheck(build));

        ThenThePullRequestShouldNotHaveBeenApproved();
        ThenTheInProgressPullRequestWasChecked();
    }

    private CodeflowApprovalCheck GivenACodeflowApprovalCheck(Build build)
        => new()
        {
            UpdaterId = GetPullRequestUpdaterId().ToString(),
            SubscriptionId = Subscription.Id,
            PreviousSourceSha = PreviousSourceSha,
            CurrentSourceSha = build.Commit,
            PullRequestUrl = VmrPullRequestUrl,
        };
}
