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

    [Test]
    public async Task LastCommentContainsAsyncReturnsTrueWhenLastCommentHasBlockedFlowMarker()
    {
        var remote = new Mock<IRemote>();
        remote.Setup(r => r.GetPullRequestCommentsAsync(PullRequestUrl))
            .ReturnsAsync([CreatePostedComment(
                PullRequestCommentBuilder.BuildBlockedCodeflowNotification())]);

        var isLastComment = await PullRequestCommenter.LastCommentContainsAsync(
            remote.Object,
            PullRequestUrl,
            PullRequestCommentBuilder.BlockedCodeflowNotificationIntro);

        isLastComment.Should().BeTrue();
    }

    [Test]
    public async Task LastCommentContainsAsyncReturnsFalseWhenMarkerIsNotInLastComment()
    {
        var remote = new Mock<IRemote>();
        remote.Setup(r => r.GetPullRequestCommentsAsync(PullRequestUrl)).ReturnsAsync(
        [
            CreatePostedComment(PullRequestCommentBuilder.BuildBlockedCodeflowNotification()),
            "A newer comment"
        ]);

        var isLastComment = await PullRequestCommenter.LastCommentContainsAsync(
            remote.Object,
            PullRequestUrl,
            PullRequestCommentBuilder.BlockedCodeflowNotificationIntro);

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
