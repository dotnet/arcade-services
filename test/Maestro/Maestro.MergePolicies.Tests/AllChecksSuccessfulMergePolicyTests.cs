// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Moq;

namespace Maestro.MergePolicies.Tests;

[TestFixture]
public class AllChecksSuccessfulMergePolicyTests
{
    [TestCase(
        "https://github.com/dotnet/runtime/pull/123",
        "WIP",
        "license/cla",
        "auto-merge.config.enforce",
        "Build Analysis")]
    [TestCase(
        "https://dev.azure.com/dnceng/internal/_git/dotnet-runtime/pullrequest/123",
        "Comment requirements",
        "Minimum number of reviewers",
        "auto-merge.config.enforce",
        "Work item linking")]
    public async Task EvaluateAsync_IgnoresDefaultChecksForProvider(
        string pullRequestUrl,
        params string[] ignoredCheckNames)
    {
        // Arrange
        var remote = new Mock<IRemote>(MockBehavior.Strict);
        remote.Setup(r => r.GetPullRequestChecksAsync(pullRequestUrl))
            .ReturnsAsync(
            [
                .. ignoredCheckNames.Select(name => new Check(CheckState.Failure, name, string.Empty)),
                new Check(CheckState.Success, "Required check", string.Empty),
            ]);

        var policy = new AllChecksSuccessfulMergePolicy([]);
        PullRequestUpdateSummary pullRequest = CreatePullRequestSummary(pullRequestUrl);

        // Act
        MergePolicyEvaluationResult result = await policy.EvaluateAsync(pullRequest, remote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.TransientSuccess);
        result.Title.Should().Contain("1 successful check(s)");
    }

    [Test]
    public async Task EvaluateAsync_CombinesConfiguredAndDefaultIgnoredChecks()
    {
        // Arrange
        const string pullRequestUrl = "https://github.com/dotnet/runtime/pull/123";
        var remote = new Mock<IRemote>(MockBehavior.Strict);
        remote.Setup(r => r.GetPullRequestChecksAsync(pullRequestUrl))
            .ReturnsAsync(
            [
                new Check(CheckState.Failure, "license/cla", string.Empty),
                new Check(CheckState.Failure, "Configured check", string.Empty),
                new Check(CheckState.Success, "Required check", string.Empty),
            ]);

        var policy = new AllChecksSuccessfulMergePolicy(["Configured check"]);
        PullRequestUpdateSummary pullRequest = CreatePullRequestSummary(pullRequestUrl);

        // Act
        MergePolicyEvaluationResult result = await policy.EvaluateAsync(pullRequest, remote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.TransientSuccess);
        result.Title.Should().Contain("1 successful check(s)");
    }

    private static PullRequestUpdateSummary CreatePullRequestSummary(string pullRequestUrl) => new(
        url: pullRequestUrl,
        coherencyCheckSuccessful: null,
        coherencyErrors: [],
        requiredUpdates: [],
        containedUpdates: [],
        headBranch: "pull-request-branch",
        repoUrl: "https://github.com/dotnet/runtime",
        codeFlowDirection: CodeFlowDirection.None);
}
