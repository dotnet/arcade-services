// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;
using ClientModels = Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
public class PullRequestCommentBuilderTests
{
    private const string FakeOrgName = "orgname";
    private const string FakeRepoName = "reponame";
    private readonly List<ClientModels.Subscription> _fakeSubscriptions = GenerateFakeSubscriptionModels();

    private readonly Mock<IRemoteFactory> _remoteFactoryMock = new();
    private readonly Mock<IBasicBarClient> _basicBarClientMock = new();
    private readonly Mock<IRemote> _remoteMock = new();

    [SetUp]
    public void SetUp()
    {
        _remoteFactoryMock.Reset();
        _basicBarClientMock.Reset();
        _remoteMock.Reset();

        _basicBarClientMock.Setup(b => b.GetSubscriptionAsync(It.IsAny<Guid>()))
            .Returns((Guid subscriptionToFind) =>
            {
                return Task.FromResult(
                    (from subscription in _fakeSubscriptions
                     where subscription.Id.Equals(subscriptionToFind)
                     select subscription).FirstOrDefault());
            });
        _remoteMock.Setup(r => r.GetPullRequestChecksAsync(It.IsAny<string>()))
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
        _remoteFactoryMock.Setup(f => f.CreateRemoteAsync(It.IsAny<string>()))
            .ReturnsAsync(_remoteMock.Object);
    }

    // Happy Path: Successfully create a comment, try to do it again, ensure we get an empty comment.
    [Test]
    public async Task CommentBuilderBuildsCorrectCommentForFailedCheckPr()
    {
        PullRequestCommentBuilder commentBuilder = new(
            NullLogger<PullRequestCommentBuilder>.Instance,
            _remoteFactoryMock.Object,
            _basicBarClientMock.Object
        );

        var pr = GetInProgressPullRequest("https://api.github.com/repos/orgname/reponame/pulls/12345");
        var comment = await commentBuilder.BuildTagSourceRepositoryGitHubContactsCommentAsync(pr);

        comment.Should().Contain($"Notification for subscribed users from https://github.com/{FakeOrgName}/source-repo1");
        pr.SourceRepoNotified.Should().BeTrue();

        foreach (var individual in _fakeSubscriptions[0].PullRequestFailureNotificationTags.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var valueToCheck = individual;
            if (!individual.StartsWith('@'))
                valueToCheck = $"@{valueToCheck}";

            comment.Should().Contain(valueToCheck);
        }

        // second invocation should return an empty string
        comment = await commentBuilder.BuildTagSourceRepositoryGitHubContactsCommentAsync(pr);
        string.IsNullOrEmpty(comment).Should().BeTrue();
    }

    [Test]
    public async Task CommentBuilderReturnsEmptyCommentWhenOnlyMaestroChecksHaveFailedOrErrored()
    {
        // Checks like "all checks succeeded" stay in a failed state until all the other checks, err, succeed.
        // Ensure we don't just go tag everyone's automerge PRs.
        PullRequestCommentBuilder commentBuilder = new(
            NullLogger<PullRequestCommentBuilder>.Instance,
            _remoteFactoryMock.Object,
            _basicBarClientMock.Object
        );
        InProgressPullRequest prToTag = GetInProgressPullRequest("https://api.github.com/repos/orgname/reponame/pulls/67890", 1);

        var comment = await commentBuilder.BuildTagSourceRepositoryGitHubContactsCommentAsync(prToTag);

        prToTag.SourceRepoNotified.Should().BeFalse();
        comment.Should().BeEmpty();
    }

    // "Do nothing" Path: Just don't blow up when a subscription object has no tags.
    [Test]
    public async Task CommentBuilderReturnsEmptyCommentWhenNoContactAliasesProvided()
    {
        PullRequestCommentBuilder commentBuilder = new(
            NullLogger<PullRequestCommentBuilder>.Instance,
            _remoteFactoryMock.Object,
            _basicBarClientMock.Object
        );
        InProgressPullRequest prToTag = GetInProgressPullRequestWithoutTags("https://api.github.com/repos/orgname/reponame/pulls/23456");

        var comment = await commentBuilder.BuildTagSourceRepositoryGitHubContactsCommentAsync(prToTag);

        prToTag.SourceRepoNotified.Should().BeFalse();
        comment.Should().BeEmpty();
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
            TargetBranch = "pr.target.branch",
            HeadBranch = "pr.head.branch",
            HeadBranchSha = "pr.head.sha",
            SourceSha = "update.source.sha",
            ContainedSubscriptions = containedSubscriptions,
            SourceRepoNotified = false
        };
    }


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
            TargetBranch = "pr.target.branch",
            HeadBranch = "pr.head.branch",
            HeadBranchSha = "pr.head.sha",
            SourceSha = "update.source.sha",
            ContainedSubscriptions = containedSubscriptions,
            SourceRepoNotified = false
        };
    }

    [Test]
    public void ManualConflictCommentFiltersOutMetadataFilesForForwardFlow()
    {
        // Create a forward flow subscription (has TargetDirectory set)
        var forwardFlowSubscription = new ClientModels.Subscription(
            new Guid("12345678-1234-1234-1234-123456789012"),
            true,
            true, // sourceEnabled = true
            $"https://github.com/{FakeOrgName}/source-repo",
            $"https://github.com/{FakeOrgName}/vmr",
            "main",
            null, // sourceDirectory
            "sdk", // targetDirectory - makes this a forward flow
            "@notifiedUser1",
            excludedAssets: []);

        var update = new SubscriptionUpdateWorkItem
        {
            UpdaterId = "test-updater-id",
            SubscriptionId = forwardFlowSubscription.Id,
            BuildId = 12345,
            SourceSha = "abcdef123",
            SourceRepo = $"https://github.com/{FakeOrgName}/source-repo"
        };

        // Conflicted files include both actual source files and metadata files
        var conflictedFiles = new List<Microsoft.DotNet.DarcLib.Helpers.UnixPath>
        {
            new("src/sdk/file1.cs"), // Should be included
            new("src/sdk/subfolder/file2.cs"), // Should be included
            new("src/source-manifest.json"), // Should be filtered out (metadata at src/ level)
            new("src/git-info/sdk.props") // Should be filtered out (not under src/sdk/)
        };

        var comment = PullRequestCommentBuilder.BuildNotificationAboutManualConflictResolutionComment(
            update,
            forwardFlowSubscription,
            conflictedFiles,
            "pr-head-branch",
            true);

        // Verify only files under src/sdk/ are included
        comment.Should().Contain("file1.cs");
        comment.Should().Contain("subfolder/file2.cs");
        
        // Verify metadata files are not included
        comment.Should().NotContain("source-manifest.json");
        comment.Should().NotContain("git-info");
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
