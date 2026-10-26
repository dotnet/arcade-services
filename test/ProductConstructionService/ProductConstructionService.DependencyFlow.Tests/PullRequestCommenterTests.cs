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
    private const string HelpLine =
        "> 💡 You may consult the [FAQ](https://github.com/dotnet/dotnet/tree/main/docs/Codeflow-PRs.md) for more information or tag **<b>@</b>dotnet/prodconsvcs** for assistance.";

    [Test]
    public async Task IsLastCommentAsyncReturnsTrueWhenLastCommentMatches()
    {
        var remote = new Mock<IRemote>();
        remote.Setup(r => r.GetPullRequestCommentsAsync(PullRequestUrl))
            .ReturnsAsync([CreatePostedComment("Run subscription 42")]);

        var isLastComment = await PullRequestCommenter.IsLastCommentAsync(
            remote.Object,
            PullRequestUrl,
            new Comment("Run subscription <subscriptionId>", CommentType.Warning),
            [("<subscriptionId>", "42")]);

        isLastComment.Should().BeTrue();
    }

    [Test]
    public async Task IsLastCommentAsyncReturnsFalseWhenMatchingCommentIsNotLast()
    {
        var remote = new Mock<IRemote>();
        remote.Setup(r => r.GetPullRequestCommentsAsync(PullRequestUrl))
            .ReturnsAsync([CreatePostedComment("Run subscription 42"), "A newer comment"]);

        var isLastComment = await PullRequestCommenter.IsLastCommentAsync(
            remote.Object,
            PullRequestUrl,
            new Comment("Run subscription <subscriptionId>", CommentType.Warning),
            [("<subscriptionId>", "42")]);

        isLastComment.Should().BeFalse();
    }

    private static string CreatePostedComment(string text) =>
        string.Join(
            Environment.NewLine,
            "> [!IMPORTANT]",
            $"> {text}",
            ">",
            HelpLine) + Environment.NewLine;
}
