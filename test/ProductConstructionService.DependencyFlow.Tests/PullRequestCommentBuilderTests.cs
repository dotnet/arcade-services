// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using ClientModels = Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
public class PullRequestCommentBuilderTests
{
    private const string FakeOrgName = "orgname";
    private const string FakeRepoName = "reponame";
    private List<ClientModels.Subscription> _fakeSubscriptions = GenerateFakeSubscriptionModels();

    private InProgressPullRequest GetInProgressPullRequestWithoutTags(string url)
    {
        var containedSubscriptions = new List<SubscriptionPullRequestUpdate>();

        var tagless = _fakeSubscriptions.Where(f => string.IsNullOrEmpty(f.PullRequestFailureNotificationTags)).First();
        containedSubscriptions.Add(new SubscriptionPullRequestUpdate()
        {
            BuildId = 12345,
            SubscriptionId = tagless.Id,
            SourceRepo = "https://github.com/foo/bar/"
        });

        return new InProgressPullRequest()
        {
            UpdaterId = new BatchedPullRequestUpdaterId(FakeRepoName, "main").Id,
            Url = url,
            HeadBranch = "pr.head.branch",
            SourceSha = "pr.head.sha",
            ContainedSubscriptions = containedSubscriptions,
            SourceRepoNotified = false
        };
    }

    [Test]
    public async Task CommentBuilderNotifyACheckFailed()
    {
        // Happy Path: Successfully create a comment, try to do it again, ensure we get an empty comment.
        Mock<IRemoteFactory> remoteFactoryMock = new();
        Mock<IBasicBarClient> basicBarClientMock = new();
        Mock<IRemote> remoteMock = new();

        basicBarClientMock.Setup(b => b.GetSubscriptionAsync(It.IsAny<Guid>()))
            .Returns((Guid subscriptionToFind) =>
            {
                return Task.FromResult(
                    (from subscription in _fakeSubscriptions
                     where subscription.Id.Equals(subscriptionToFind)
                     select subscription).FirstOrDefault());
            });
        remoteMock.Setup(r => r.GetPullRequestChecksAsync(It.IsAny<string>()))
            .Returns((string fakePrsUrl) =>
            {
                List<Check> checksToReturn =
                [
                    new Check(CheckState.Failure, "Some Maestro Policy", "", true),
                    new Check(CheckState.Error, "Some Other Maestro Policy", "", true),
                ];

                if (fakePrsUrl.EndsWith("/12345"))
                {
                    checksToReturn.Add(new Check(CheckState.Failure, "Important PR Check", "", false));
                }

                return Task.FromResult(checksToReturn.AsEnumerable());
            });
        remoteFactoryMock.Setup(f => f.CreateRemoteAsync(It.IsAny<string>()))
            .ReturnsAsync(remoteMock.Object);

        PullRequestCommentBuilder commentBuilder = new(
            NullLogger<PullRequestCommentBuilder>.Instance,
            remoteFactoryMock.Object,
            basicBarClientMock.Object
        );

        var pr = GetInProgressPullRequest("https://api.github.com/repos/orgname/reponame/pulls/12345");
        var comment = await commentBuilder.BuildTagSourceRepositoryGitHubContactsCommentAsync(pr);

        string.IsNullOrEmpty(comment).Should().BeFalse();
        pr.SourceRepoNotified.Should().BeTrue();

        comment = await commentBuilder.BuildTagSourceRepositoryGitHubContactsCommentAsync(pr);
        string.IsNullOrEmpty(comment).Should().BeTrue();

    }

    private InProgressPullRequest GetInProgressPullRequest(string url, int containedSubscriptionCount = 1)
    {
        var containedSubscriptions = new List<SubscriptionPullRequestUpdate>();

        for (var i = 0; i < containedSubscriptionCount; i++)
        {
            containedSubscriptions.Add(new SubscriptionPullRequestUpdate()
            {
                BuildId = 10000 + i,
                SubscriptionId = _fakeSubscriptions[i].Id,
                SourceRepo = "https://github.com/foo/bar/"
            });
        }
        // For the purposes of testing this class, we only need to fill out
        // the "Url" and ContainedSubscriptions fields in InProgressPullRequestObjects
        return new InProgressPullRequest()
        {
            UpdaterId = new BatchedPullRequestUpdaterId(FakeRepoName, "main").Id,
            Url = url,
            HeadBranch = "pr.head.branch",
            SourceSha = "pr.head.sha",
            ContainedSubscriptions = containedSubscriptions,
            SourceRepoNotified = false
        };
    }

    private static List<ClientModels.Subscription> GenerateFakeSubscriptionModels() =>
    [
        new ClientModels.Subscription(
            new Guid("35684498-9C08-431F-8E66-8242D7C38598"),
            true,
            false,
            $"https://github.com/{FakeOrgName}/source-repo1",
            $"https://github.com/{FakeOrgName}/dest-repo",
            "fakebranch",
            null,
            null,
            "@notifiedUser1;@notifiedUser2;userWithoutAtSign;",
            excludedAssets: []),
        new ClientModels.Subscription(
            new Guid("80B3B6EE-4C9B-46AC-B275-E016E0D5AF41"),
            true,
            false,
            $"https://github.com/{FakeOrgName}/source-repo2",
            $"https://github.com/{FakeOrgName}/dest-repo",
            "fakebranch",
            null,
            null,
            "@notifiedUser3;@notifiedUser4",
            excludedAssets: []),
        new ClientModels.Subscription(
            new Guid("1802E0D2-D6BF-4A14-BF4C-B2A292739E59"),
            true,
            false,
            $"https://github.com/{FakeOrgName}/source-repo2",
            $"https://github.com/{FakeOrgName}/dest-repo",
            "fakebranch",
            null,
            null,
            string.Empty,
            excludedAssets: [])
    ];
}
