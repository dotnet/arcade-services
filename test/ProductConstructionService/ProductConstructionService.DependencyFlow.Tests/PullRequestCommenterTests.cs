// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.DarcLib;
using Moq;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
public class PullRequestCommenterTests
{
    private const string PullRequestUrl = "https://github.com/org/repo/pull/1";
    private const string BlockedFlowMarker =
        "While this PR was open, the source repository has received code changes from this repository (an opposite codeflow merged).";

    [Test]
    public async Task LastCommentContainsAsyncReturnsTrueWhenLastCommentHasBlockedFlowMarker()
    {
        var remote = new Mock<IRemote>();
        remote.Setup(r => r.GetPullRequestCommentsAsync(PullRequestUrl))
            .ReturnsAsync([CreatePostedComment(
                $"{BlockedFlowMarker}\nTo avoid complex conflicts, the codeflow cannot continue.")]);

        var isLastComment = await PullRequestCommenter.LastCommentContainsAsync(
            remote.Object,
            PullRequestUrl,
            BlockedFlowMarker);

        isLastComment.Should().BeTrue();
    }

    [Test]
    public async Task LastCommentContainsAsyncReturnsFalseWhenMarkerIsNotInLastComment()
    {
        var remote = new Mock<IRemote>();
        remote.Setup(r => r.GetPullRequestCommentsAsync(PullRequestUrl)).ReturnsAsync(
        [
            CreatePostedComment(BlockedFlowMarker),
            "A newer comment"
        ]);

        var isLastComment = await PullRequestCommenter.LastCommentContainsAsync(
            remote.Object,
            PullRequestUrl,
            BlockedFlowMarker);

        isLastComment.Should().BeFalse();
    }

    private static string CreatePostedComment(string text)
    {
        var lines = text.Split('\n');
        return string.Join(
            Environment.NewLine,
            new[] { "> [!IMPORTANT]" }
                .Concat(lines.Select(line => $"> {line}"))
                .Append(">")
                .Append("> Footer")) + Environment.NewLine;
    }
}
