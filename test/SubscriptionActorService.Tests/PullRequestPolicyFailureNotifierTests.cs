// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using SubscriptionActorService.StateModel;
using ClientModels = Microsoft.DotNet.Maestro.Client.Models;

namespace SubscriptionActorService.Tests;

[TestFixture]
public class PullRequestPolicyFailureNotifierTests
{
    private const string FakeOrgName = "orgname";
    private const string FakeRepoName = "reponame";

    protected Mock<IBarApiClient> BarClient = null!;
    protected Mock<IRemoteGitRepo> GitRepo = null!;
    protected Mock<ILocalLibGit2Client> LocalGitClient = null!;
    protected Mock<IRemoteFactory> RemoteFactory = null!;
    protected Mock<IHostEnvironment> Env = null!;
    protected Mock<Octokit.IGitHubClient> GithubClient = null!;
    protected Mock<IGitHubTokenProvider> GitHubTokenProvider = null!;
    protected Mock<IGitHubClientFactory> GitHubClientFactory = null!;
    protected IServiceScope Scope = null!;
    protected Remote MockRemote = null!;
    protected ServiceProvider Provider = null!;
    protected List<ClientModels.Subscription> FakeSubscriptions = null!;
    private Dictionary<string, string> _prCommentsMade = [];

    [SetUp]
    public void PullRequestActorTests_SetUp()
    {
        _prCommentsMade = [];

        var services = new ServiceCollection();
        FakeSubscriptions = GenerateFakeSubscriptionModels();

        Env = new Mock<IHostEnvironment>(MockBehavior.Strict);
        services.AddSingleton(Env.Object);
        GithubClient = new Mock<Octokit.IGitHubClient>();
        GitHubTokenProvider = new Mock<IGitHubTokenProvider>(MockBehavior.Strict);
        GitHubTokenProvider.Setup(x => x.GetTokenForRepository(It.IsAny<string>())).ReturnsAsync("doesnotmatter");
        GitHubClientFactory = new Mock<IGitHubClientFactory>();
        GitHubClientFactory.Setup(x => x.CreateGitHubClient(It.IsAny<string>()))
            .Returns(GithubClient.Object);
        GithubClient.Setup(ghc => ghc.Issue.Comment.Create(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>()))
            .ReturnsAsync(new Octokit.IssueComment())
            .Callback<string, string, int, string>(RecordGitHubClientComment);

        services.AddLogging();

        BarClient = new Mock<IBarApiClient>(MockBehavior.Strict);
        BarClient.Setup(b => b.GetSubscriptionAsync(It.IsAny<Guid>()))
            .Returns((Guid subscriptionToFind) =>
            {
                return Task.FromResult(
                    (from subscription in FakeSubscriptions
                        where subscription.Id.Equals(subscriptionToFind)
                        select subscription).FirstOrDefault());
            });

        GitRepo = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        GitRepo.Setup(g => g.GetPullRequestChecksAsync(It.IsAny<string>()))
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

                return Task.FromResult((IList<Check>) checksToReturn);
            });

        MockRemote = new Remote(GitRepo.Object, new VersionDetailsParser(), NullLogger.Instance);

        RemoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        RemoteFactory.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(MockRemote);

        Provider = services.BuildServiceProvider();
        Scope = Provider.CreateScope();
    }

    [TestCase()]
    public async Task NotifyACheckFailed()
    {
        // Happy Path: Successfully create a comment, try to do it again, ensure it does not happen.
        var testObject = GetInstance();
        InProgressPullRequest prToTag = GetInProgressPullRequest("https://api.github.com/repos/orgname/reponame/pulls/12345");

        await testObject.TagSourceRepositoryGitHubContactsAsync(prToTag);

        prToTag.SourceRepoNotified.Should().BeTrue();

        // Second time; no second comment should be made. (If it were made, it'd throw)
        await testObject.TagSourceRepositoryGitHubContactsAsync(prToTag);
        _prCommentsMade.Count.Should().Be(1);
        // Spot check some values
        _prCommentsMade[$"{FakeOrgName}/{FakeRepoName}/12345"].Should().Contain(
            $"Notification for subscribed users from https://github.com/{FakeOrgName}/source-repo1");
        foreach (string individual in FakeSubscriptions[0].PullRequestFailureNotificationTags.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            // Make sure normalization happens; test includes a user without @.
            string valueToCheck = individual;
            if (!individual.StartsWith('@'))
                valueToCheck = $"@{valueToCheck}";

            _prCommentsMade[$"{FakeOrgName}/{FakeRepoName}/12345"].Should().Contain(valueToCheck);
        }
    }

    [TestCase()]
    public async Task MultipleBuildsIncluded()
    {
        // Scenario where a bunch of commits / builds are included in the same non-batched, single subscription PR
        var testObject = GetInstance();
        InProgressPullRequest prToTag = GetInProgressPullRequest("https://api.github.com/repos/orgname/reponame/pulls/12345", 2);

        await testObject.TagSourceRepositoryGitHubContactsAsync(prToTag);

        prToTag.SourceRepoNotified.Should().BeTrue();

        // Second time; no second comment should be made. (If it were made, it'd throw)
        await testObject.TagSourceRepositoryGitHubContactsAsync(prToTag);
        _prCommentsMade.Count.Should().Be(1);
        // Spot check some values
        _prCommentsMade[$"{FakeOrgName}/{FakeRepoName}/12345"].Should().Contain(
            $"Notification for subscribed users from https://github.com/{FakeOrgName}/source-repo1");
        foreach (string individual in FakeSubscriptions[0].PullRequestFailureNotificationTags.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            // Make sure normalization happens; test includes a user without @.
            string valueToCheck = individual;
            if (!individual.StartsWith('@'))
                valueToCheck = $"@{valueToCheck}";

            _prCommentsMade[$"{FakeOrgName}/{FakeRepoName}/12345"].Should().Contain(valueToCheck);
        }
    }

    [TestCase()]
    public async Task OnlyMaestroChecksHaveFailedOrErrored()
    {
        // Checks like "all checks succeeded" stay in a failed state until all the other checks, err, succeed.
        // Ensure we don't just go tag everyone's automerge PRs.
        var testObject = GetInstance();
        InProgressPullRequest prToTag = GetInProgressPullRequest("https://api.github.com/repos/orgname/reponame/pulls/67890", 1);

        await testObject.TagSourceRepositoryGitHubContactsAsync(prToTag);

        prToTag.SourceRepoNotified.Should().BeFalse();
        _prCommentsMade.Count.Should().Be(0);
    }

    [TestCase()]
    public async Task NoContactAliasesProvided()
    {
        // "Do nothing" Path: Just don't blow up when a subscription object has no tags.
        var testObject = GetInstance();
        InProgressPullRequest prToTag = GetInProgressPullRequestWithoutTags("https://api.github.com/repos/orgname/reponame/pulls/23456");
        await testObject.TagSourceRepositoryGitHubContactsAsync(prToTag);
        prToTag.SourceRepoNotified.Should().BeFalse();
        _prCommentsMade.Count.Should().Be(0);
    }

    #region Test Helpers

    private InProgressPullRequest GetInProgressPullRequest(string url, int containedSubscriptionCount = 1)
    {
        var containedSubscriptions = new List<SubscriptionPullRequestUpdate>();

        for (int i = 0; i < containedSubscriptionCount; i++)
        {
            containedSubscriptions.Add(new SubscriptionPullRequestUpdate()
            {
                BuildId = 10000 + i,
                SubscriptionId = FakeSubscriptions[i].Id
            });
        }
        // For the purposes of testing this class, we only need to fill out
        // the "Url" and ContainedSubscriptions fields in InProgressPullRequestObjects
        return new InProgressPullRequest()
        {
            Url = url,
            ContainedSubscriptions = containedSubscriptions,
            SourceRepoNotified = false
        };
    }

    private InProgressPullRequest GetInProgressPullRequestWithoutTags(string url)
    {
        var containedSubscriptions = new List<SubscriptionPullRequestUpdate>();

        var tagless = FakeSubscriptions.Where(f => string.IsNullOrEmpty(f.PullRequestFailureNotificationTags)).First();
        containedSubscriptions.Add(new SubscriptionPullRequestUpdate()
        {
            BuildId = 12345,
            SubscriptionId = tagless.Id
        });

        return new InProgressPullRequest()
        {
            Url = url,
            ContainedSubscriptions = containedSubscriptions,
            SourceRepoNotified = false
        };
    }

    private void RecordGitHubClientComment(string owner, string repo, int prIssue, string comment)
    {
        lock (_prCommentsMade)
        {
            // No need to check for existence; if the same comment gets added twice, it'd be a bug
            _prCommentsMade.Add($"{owner}/{repo}/{prIssue}", comment);
        }
    }

    public IPullRequestPolicyFailureNotifier GetInstance() => new PullRequestPolicyFailureNotifier(
        GitHubTokenProvider.Object,
        GitHubClientFactory.Object,
        RemoteFactory.Object,
        BarClient.Object,
        Scope.ServiceProvider.GetRequiredService<ILogger<PullRequestPolicyFailureNotifier>>());

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
            excludedAssets: ImmutableList<string>.Empty),
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
            excludedAssets: ImmutableList<string>.Empty),
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
            excludedAssets: ImmutableList<string>.Empty)
    ];

    #endregion
}
