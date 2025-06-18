// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using FluentAssertions;
using Maestro.MergePolicies;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
internal class PullRequestBuilderTests : SubscriptionOrPullRequestUpdaterTests
{
    private Dictionary<string, Mock<IRemote>> _darcRemotes = null!;
    private Mock<IRemoteFactory> _remoteFactory = null!;
    private Mock<IBasicBarClient> _barClient = null!;

    [SetUp]
    public void PullRequestBuilderTests_SetUp()
    {
        _darcRemotes = new()
        {
            [TargetRepo] = new Mock<IRemote>()
        };
        _remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        _barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
    }

    protected override void RegisterServices(IServiceCollection services)
    {
        _remoteFactory
            .Setup(f => f.CreateRemoteAsync(It.IsAny<string>()))
            .ReturnsAsync((string repo) => _darcRemotes.GetOrAddValue(repo, () => CreateMock<IRemote>()).Object);

        services.AddSingleton(_remoteFactory.Object);
        services.AddSingleton(_barClient.Object);

        base.RegisterServices(services);
    }

    [Test]
    public async Task ShouldReturnCalculateCorrectPRDescriptionWhenCoherencyUpdate()
    {
        var build = GivenANewBuildId(101, "abc1234");
        SubscriptionUpdateWorkItem update = GivenSubscriptionUpdate(true, build.Id, "11111111-1111-1111-1111-111111111111");
        List<DependencyUpdate> deps = GivenDependencyUpdates('a', build.Id);

        var description = await GeneratePullRequestDescription([(update, deps)]);
        description.ToString().Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps));
    }

    [Test]
    public async Task ShouldReturnCalculateCorrectPRDescriptionWhenNonCoherencyUpdate()
    {
        var build1 = GivenANewBuildId(101, "abc1234");
        var build2 = GivenANewBuildId(102, "def2345");
        SubscriptionUpdateWorkItem update1 = GivenSubscriptionUpdate(false, build1.Id, "11111111-1111-1111-1111-111111111111");
        SubscriptionUpdateWorkItem update2 = GivenSubscriptionUpdate(false, build2.Id, "22222222-2222-2222-2222-222222222222");
        List<DependencyUpdate> deps1 = GivenDependencyUpdates('a', build1.Id);
        List<DependencyUpdate> deps2 = GivenDependencyUpdates('b', build2.Id);

        var description = await GeneratePullRequestDescription([(update1, deps1), (update2, deps2)]);

        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps1, 1));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps2, 3));
    }

    [Test]
    public async Task ShouldReturnCalculateCorrectPRDescriptionWhenUpdatingExistingPR()
    {
        var build1 = GivenANewBuildId(101, "abc1234");
        var build2 = GivenANewBuildId(102, "def2345");
        var build3 = GivenANewBuildId(103, "gha3456");
        var build4 = GivenANewBuildId(104, "gha3777");
        SubscriptionUpdateWorkItem update1 = GivenSubscriptionUpdate(false, build1.Id, "11111111-1111-1111-1111-111111111111");
        SubscriptionUpdateWorkItem update2 = GivenSubscriptionUpdate(false, build2.Id, "22222222-2222-2222-2222-222222222222");
        SubscriptionUpdateWorkItem update3 = GivenSubscriptionUpdate(false, build3.Id, "33333333-3333-3333-3333-333333333333");
        SubscriptionUpdateWorkItem update4 = GivenSubscriptionUpdate(false, build4.Id, "22222222-2222-2222-2222-222222222222");
        List<DependencyUpdate> deps1 = GivenDependencyUpdates('a', build1.Id);
        List<DependencyUpdate> deps2 = GivenDependencyUpdates('b', build2.Id);
        List<DependencyUpdate> deps3 = GivenDependencyUpdates('c', build3.Id);
        List<DependencyUpdate> deps4 = GivenDependencyUpdates('e', build4.Id);

        var description = await GeneratePullRequestDescription([(update1, deps1), (update2, deps2), (update3, deps3)]);

        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps1, 1));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps2, 3));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps1, 1));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps2, 3));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps3, 5));

        List<DependencyUpdate> deps22 = GivenDependencyUpdates('d', build3.Id);

        description = await GeneratePullRequestDescription([(update2, deps22)], description);

        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps1, 1));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps3, 5));
        description.Should().NotContain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps2, 3));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps22, 7));

        deps3 = GivenDependencyUpdates('e', build4.Id);

        description = await GeneratePullRequestDescription([(update4, deps3)], description);
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps1, 1));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps4, 9));
        description.Should().NotContain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps2, 3));
    }

    [Test]
    public async Task ShouldReturnCorrectPRDescriptionForCodeEnabledSubscription()
    {
        string commitSha = "abc1234567";
        Build build = GivenANewBuildId(101, commitSha);
        build.GitHubRepository = "https://github.com/foo/foobar";
        build.GitHubBranch = "main";
        build.AzureDevOpsBuildNumber = "20230205.2";
        build.AzureDevOpsAccount = "foo";
        build.AzureDevOpsProject = "bar";
        build.AzureDevOpsBuildId = 1234;
        string subscriptionGuid = "11111111-1111-1111-1111-111111111111";
        List<DependencyUpdateSummary> dependencyUpdates = GivenDependencyUpdateSummaries();
        SubscriptionUpdateWorkItem update = new()
        {
            UpdaterId = subscriptionGuid,
            IsCoherencyUpdate = false,
            SourceRepo = build.GetRepository(),
            SubscriptionId = new Guid(subscriptionGuid),
            BuildId = build.Id,
            SubscriptionType = SubscriptionType.DependenciesAndSources,
        };

        List<UpstreamRepoDiff> upstreamRepoDiffs =
            [
                new UpstreamRepoDiff("https://github.com/foo/bar", "oldSha123", "newSha789"),
                new UpstreamRepoDiff("https://github.com/foo/boz", "oldSha234", "newSha678"),
                new UpstreamRepoDiff("https://github.com/foo/baz", "oldSha345", "newSha567")
            ];

        string mockPreviousCommitSha = "SHA1234567890";

        string? description = null;
        await Execute(
            async context =>
            {
                var builder = ActivatorUtilities.CreateInstance<PullRequestBuilder>(context);
                description = builder.GenerateCodeFlowPRDescription(update, build, mockPreviousCommitSha, dependencyUpdates: dependencyUpdates, upstreamRepoDiffs: upstreamRepoDiffs, currentDescription: null, isForwardFlow: false);
                await Task.CompletedTask; // hacky way to make the lambda async
            });

        string shortCommitSha = commitSha.Substring(0, 7);
        string shortPreviousCommitSha = mockPreviousCommitSha.Substring(0, 7);

        description.Should().Be(
            $"""
            
            > [!NOTE]
            > This is a codeflow update. It may contain both source code changes from [the VMR]({update.SourceRepo}) as well as dependency updates. Learn more [here]({PullRequestBuilder.CodeFlowPrFaqUri}).
            
            This pull request brings the following source code changes

            [marker]: <> (Begin:{subscriptionGuid})

            ## From {build.GitHubRepository}
            - **Subscription**: [{subscriptionGuid}](https://maestro.dot.net/subscriptions?search={subscriptionGuid})
            - **Build**: [{build.AzureDevOpsBuildNumber}](https://dev.azure.com/{build.AzureDevOpsAccount}/{build.AzureDevOpsProject}/_build/results?buildId={build.AzureDevOpsBuildId})
            - **Date Produced**: {build.DateProduced.ToUniversalTime():MMMM d, yyyy h:mm:ss tt UTC}
            - **Commit**: [{commitSha}]({build.GitHubRepository}/commit/{commitSha})
            - **Commit Diff**: [{shortPreviousCommitSha}...{shortCommitSha}]({build.GitHubRepository}/compare/{mockPreviousCommitSha}...{commitSha})
            - **Branch**: main

            **Updated Dependencies**
            - **Foo.Bar**: [from 1.0.0 to 2.0.0][1]
            - **Foo.Biz**: [from 1.0.0 to 2.0.0][1]
            - **Biz.Boz**: [from 1.0.0 to 2.0.0]({build.GitHubRepository}/compare/uvw789...xyz890)

            [marker]: <> (End:{subscriptionGuid})
            
            [1]: {build.GitHubRepository}/compare/abc123...def456
            [marker]: <> (Start:Footer:CodeFlow PR)
            
            ## Associated changes in original repos:
            - https://github.com/foo/bar/compare/oldSha123...newSha789
            - https://github.com/foo/boz/compare/oldSha234...newSha678
            - https://github.com/foo/baz/compare/oldSha345...newSha567

            [marker]: <> (End:Footer:CodeFlow PR)
            """);
    }


    [Test]
    public async Task ShouldReturnCorrectPRDescriptionForBatchedCodeFlowSubscriptions()
    {
        string commitSha = "abc1234567";
        Build build1 = GivenANewBuildId(101, commitSha);
        build1.GitHubRepository = "https://github.com/foo/foobar";
        build1.GitHubBranch = "main";
        build1.AzureDevOpsBuildNumber = "20230205.2";
        build1.AzureDevOpsAccount = "foo";
        build1.AzureDevOpsProject = "bar";
        build1.AzureDevOpsBuildId = 1234;
        string subscriptionGuid = "11111111-1111-1111-1111-111111111111";
        SubscriptionUpdateWorkItem update = GivenSubscriptionUpdate(false, build1.Id, guid: subscriptionGuid, SubscriptionType.DependenciesAndSources);
        string previousCommitSha = "SHA1234567890";
        string? description = null;
        await Execute(
            async context =>
            {
                var builder = ActivatorUtilities.CreateInstance<PullRequestBuilder>(context);
                description = builder.GenerateCodeFlowPRDescription(update, build1, previousCommitSha, dependencyUpdates: [], currentDescription: null, isForwardFlow: true, upstreamRepoDiffs: []);
                await Task.CompletedTask; // hacky way to make the lambda async
            });
        string shortCommitSha = commitSha.Substring(0, 7);
        string shortPreviousCommitSha = previousCommitSha.Substring(0, 7);


        string commitSha2 = "xyz1234567";
        Build build2 = GivenANewBuildId(101, commitSha2);
        build2.GitHubRepository = "https://github.com/zoo/faz";
        build2.GitHubBranch = "main";
        build2.AzureDevOpsBuildNumber = "20240220.2";
        build2.AzureDevOpsAccount = "zoo";
        build2.AzureDevOpsProject = "faz";
        build2.AzureDevOpsBuildId = 7890;
        string subscriptionGuid2 = "22222222-2222-2222-2222-222222222222";
        SubscriptionUpdateWorkItem update2 = GivenSubscriptionUpdate(false, build2.Id, guid: subscriptionGuid2, SubscriptionType.DependenciesAndSources);
        string previousCommitSha2 = "SHA0987654321";
        string? description2 = null;
        await Execute(
            async context =>
            {
                var builder = ActivatorUtilities.CreateInstance<PullRequestBuilder>(context);
                description2 = builder.GenerateCodeFlowPRDescription(update2, build2, previousCommitSha2, dependencyUpdates: [], upstreamRepoDiffs: [], description, isForwardFlow: true);
                await Task.CompletedTask; // hacky way to make the lambda async
            });
        string shortCommitSha2 = commitSha2.Substring(0, 7);
        string shortPreviousCommitSha2 = previousCommitSha2.Substring(0, 7);


        description2.Should().Be(
            $"""
            
            > [!NOTE]
            > This is a codeflow update. It may contain both source code changes from [the source repo]({update.SourceRepo}) as well as dependency updates. Learn more [here]({PullRequestBuilder.CodeFlowPrFaqUri}).
            
            This pull request brings the following source code changes
            
            [marker]: <> (Begin:{subscriptionGuid})
            
            ## From {build1.GitHubRepository}
            - **Subscription**: [{subscriptionGuid}](https://maestro.dot.net/subscriptions?search={subscriptionGuid})
            - **Build**: [{build1.AzureDevOpsBuildNumber}](https://dev.azure.com/{build1.AzureDevOpsAccount}/{build1.AzureDevOpsProject}/_build/results?buildId={build1.AzureDevOpsBuildId})
            - **Date Produced**: {build1.DateProduced.ToUniversalTime():MMMM d, yyyy h:mm:ss tt UTC}
            - **Commit**: [{commitSha}]({build1.GitHubRepository}/commit/{commitSha})
            - **Commit Diff**: [{shortPreviousCommitSha}...{shortCommitSha}]({build1.GitHubRepository}/compare/{previousCommitSha}...{commitSha})
            - **Branch**: main
            
            [marker]: <> (End:{subscriptionGuid})
            
            [marker]: <> (Begin:{subscriptionGuid2})

            ## From {build2.GitHubRepository}
            - **Subscription**: [{subscriptionGuid2}](https://maestro.dot.net/subscriptions?search={subscriptionGuid2})
            - **Build**: [{build2.AzureDevOpsBuildNumber}](https://dev.azure.com/{build2.AzureDevOpsAccount}/{build2.AzureDevOpsProject}/_build/results?buildId={build2.AzureDevOpsBuildId})
            - **Date Produced**: {build2.DateProduced.ToUniversalTime():MMMM d, yyyy h:mm:ss tt UTC}
            - **Commit**: [{commitSha2}]({build2.GitHubRepository}/commit/{commitSha2})
            - **Commit Diff**: [{shortPreviousCommitSha2}...{shortCommitSha2}]({build2.GitHubRepository}/compare/{previousCommitSha2}...{commitSha2})
            - **Branch**: main

            [marker]: <> (End:{subscriptionGuid2})

            """);
    }

    [Test]
    public void ShouldReturnCorrectDependencyUpdateBlock()
    {
        DependencyUpdateSummary newDependency = new()
        {
            DependencyName = "Foo.Bar",
            FromVersion = null,
            ToVersion = "2.0.0",
            FromCommitSha = null,
            ToCommitSha = "def456"
        };

        DependencyUpdateSummary removedDependency = new()
        {
            DependencyName = "Foo.Biz",
            FromVersion = "1.0.0",
            ToVersion = null,
            FromCommitSha = "abc123",
            ToCommitSha = null
        };

        DependencyUpdateSummary updatedDependency = new()
        {
            DependencyName = "Biz.Boz",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            FromCommitSha = "uvw789",
            ToCommitSha = "xyz890"
        };

        string dependencyBlock = PullRequestBuilder.CreateDependencyUpdateBlock([newDependency, removedDependency, updatedDependency], "https://github.com/Foo");

        dependencyBlock.Should().Be(
            """

            **New Dependencies**
            - **Foo.Bar**: [2.0.0](https://github.com/Foo/commit/def456)

            **Removed Dependencies**
            - **Foo.Biz**: 1.0.0

            **Updated Dependencies**
            - **Biz.Boz**: [from 1.0.0 to 2.0.0](https://github.com/Foo/compare/uvw789...xyz890)

            """);
    }

    private const string RegexTestString1 =
        """
        [2]:qqqq
        qqqqq
        qqqq
        [42]:qq
        [2q]:qq
        [123]
        qq[234]:qq
         [345]:qq
        """;

    private const string RegexTestString2 = "";
    private const string RegexTestString3 =
        """
        > [!NOTE]
        > This is a codeflow update. It may contain both source code changes from [the VMR](https://github.com/foo/foobar) as well as dependency updates. Learn more [here](https://github.com/dotnet/dotnet/tree/main/docs/Codeflow-PRs.md).

        This pull request brings the following source code changes

        [marker]: <> (Begin:11111111-1111-1111-1111-111111111111)

        ## From https://github.com/foo/foobar
        - **Subscription**: [11111111-1111-1111-1111-111111111111](https://maestro.dot.net/subscriptions?search=11111111-1111-1111-1111-111111111111)
        - **Build**: [20230205.2](https://dev.azure.com/foo/bar/_build/results?buildId=1234)
        - **Date Produced**: června 18, 2025 11:12:39 dop. UTC
        - **Commit**: [abc1234567](https://github.com/foo/foobar/commit/abc1234567)
        - **Commit Diff**: [SHA1234...abc1234](https://github.com/foo/foobar/compare/SHA1234567890...abc1234567)
        - **Branch**: main

        **Updated Dependencies**
        - **Foo.Bar**: [from 1.0.0 to 2.0.0](https://github.com/foo/foobar/compare/abc123...def456)
        - **Foo.Biz**: [from 1.0.0 to 2.0.0](https://github.com/foo/foobar/compare/abc123...def456)
        - **Biz.Boz**: [from 1.0.0 to 2.0.0](https://github.com/foo/foobar/compare/uvw789...xyz890)

        [marker]: <> (End:11111111-1111-1111-1111-111111111111)
        """;

    private const string RegexTestString4 =
        """
        [1]:q
        [2]:1
        [3]:q
        [4]:q
        """;
    private static readonly object[] RegexTestCases =
    [
        new object[] { RegexTestString1, 43},
        new object[] { RegexTestString2, 1},
        new object[] { RegexTestString3, 1},
        new object [] { RegexTestString4, 5},
    ];

    [TestCaseSource(nameof(RegexTestCases))]
    public void ShouldReturnCorrectMaximumIndex(string str, int expectedResult)
    {
        PullRequestBuilder.GetStartingReferenceId(str).Should().Be(expectedResult);
    }

    private List<DependencyUpdate> GivenDependencyUpdates(char version, int buildId)
    {
        List<DependencyUpdate> dependencies =
        [
            new DependencyUpdate
            {
                From = new DependencyDetail
                {
                    Name = $"from dependency name 1{version}",
                    Version = $"1.0.0{version}",
                    CoherentParentDependencyName = $"from parent name 1{version}",
                    Commit = $"{version} commit from 1"
                },
                To = new DependencyDetail
                {
                    Name = $"to dependency name 1{version}",
                    Version = $"1.0.0{version}",
                    CoherentParentDependencyName = $"from parent name 1{version}",
                    RepoUri = "https://amazing_uri.com",
                    Commit = $"{version} commit to 1",
                }
            },
            new DependencyUpdate
            {
                From = new DependencyDetail
                {
                    Name = $"from dependency name 2{version}",
                    Version = $"1.0.0{version}",
                    CoherentParentDependencyName = $"from parent name 2{version}",
                    Commit = $"{version} commit from 2"
                },
                To = new DependencyDetail
                {
                    Name = $"to dependency name 2{version}",
                    Version = $"1.0.0{version}",
                    CoherentParentDependencyName = $"from parent name 2{version}",
                    RepoUri = "https://amazing_uri.com",
                    Commit = $"{version} commit to 2"
                }
            }
        ];

        foreach (var dependency in dependencies.SelectMany(d => new[] { d.From, d.To }))
        {
            _barClient
                .Setup(x => x.GetAssetsAsync(dependency.Name, dependency.Version, null, null))
                .ReturnsAsync(
                [
                    new Asset(1,buildId,false,dependency.Name,dependency.Version,[])
                ]);
        }

        return dependencies;
    }

    private static List<DependencyUpdateSummary> GivenDependencyUpdateSummaries()
    {
        return [
            new DependencyUpdateSummary
            {
                DependencyName = "Foo.Bar",
                FromVersion = "1.0.0",
                ToVersion = "2.0.0",
                FromCommitSha = "abc123",
                ToCommitSha = "def456"
            },
            new DependencyUpdateSummary
            {
                DependencyName = "Foo.Biz",
                FromVersion = "1.0.0",
                ToVersion = "2.0.0",
                FromCommitSha = "abc123",
                ToCommitSha = "def456"
            },
            new DependencyUpdateSummary
            {
                DependencyName = "Biz.Boz",
                FromVersion = "1.0.0",
                ToVersion = "2.0.0",
                FromCommitSha = "uvw789",
                ToCommitSha = "xyz890"
            }
        ];
    }

    private static SubscriptionUpdateWorkItem GivenSubscriptionUpdate(
        bool isCoherencyUpdate,
        int buildId,
        string guid,
        SubscriptionType type = SubscriptionType.Dependencies) => new()
    {
        UpdaterId = guid,
        IsCoherencyUpdate = isCoherencyUpdate,
        SourceRepo = "The best repo",
        SubscriptionId = new Guid(guid),
        BuildId = buildId,
        SubscriptionType = type,
    };

    private static string BuildCorrectPRDescriptionWhenCoherencyUpdate(List<DependencyUpdate> deps)
    {
        var stringBuilder = new StringBuilder();
        foreach (DependencyUpdate dep in deps)
        {
            stringBuilder.AppendLine($"  - **{dep.To.Name}**: from {dep.From.Version} to {dep.To.Version} (parent: {dep.To.CoherentParentDependencyName})");
        }
        return stringBuilder.ToString();
    }

    private static string BuildCorrectPRDescriptionWhenNonCoherencyUpdate(List<DependencyUpdate> deps, int startingId)
    {
        var builder = new StringBuilder();
        List<string> urls = [];
        for (var i = 0; i < deps.Count; i++)
        {
            urls.Add(PullRequestBuilder.GetChangesURI(deps[i].To.RepoUri, deps[i].From.Commit, deps[i].To.Commit));
            builder.AppendLine($"  - **{deps[i].To.Name}**: [from {deps[i].From.Version} to {deps[i].To.Version}][{startingId + i}]");
        }
        builder.AppendLine();
        for (var i = 0; i < urls.Count; i++)
        {
            builder.AppendLine($"[{i + startingId}]: {urls[i]}");
        }
        return builder.ToString();
    }

    private async Task<string> GeneratePullRequestDescription(
        List<(SubscriptionUpdateWorkItem update, List<DependencyUpdate> deps)> updates,
        string? originalDescription = null)
    {
        string description = null!;
        await Execute(
            async context =>
            {
                var builder = ActivatorUtilities.CreateInstance<PullRequestBuilder>(context);
                description = await builder.CalculatePRDescriptionAndCommitUpdatesAsync(
                    updates,
                    originalDescription,
                    TargetRepo,
                    "new-branch");
            });

        return description;
    }

    private Build GivenANewBuildId(int id, string sha)
    {
        Build build = new(
            id: id,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: false,
            commit: sha,
            channels: [],
            assets: [],
            dependencies: [],
            incoherencies: [])
        {
            GitHubRepository = "https://github.com/foo/bar/"
        };

        _barClient
            .Setup(x => x.GetBuildAsync(id))
            .ReturnsAsync(build);

        return build;
    }
}
