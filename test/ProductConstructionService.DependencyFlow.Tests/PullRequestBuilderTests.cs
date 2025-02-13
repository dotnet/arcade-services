// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using FluentAssertions;
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
        var build = GivenANewBuildId(101, "abc1");
        SubscriptionUpdateWorkItem update = GivenSubscriptionUpdate(true, build.Id, "11111111-1111-1111-1111-111111111111");
        List<DependencyUpdate> deps = GivenDependencyUpdates('a', build.Id);

        var description = await GeneratePullRequestDescription([(update, deps)]);
        description.ToString().Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps));
    }

    [Test]
    public async Task ShouldReturnCalculateCorrectPRDescriptionWhenNonCoherencyUpdate()
    {
        var build1 = GivenANewBuildId(101, "abc1");
        var build2 = GivenANewBuildId(102, "def2");
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
        var build1 = GivenANewBuildId(101, "abc1");
        var build2 = GivenANewBuildId(102, "def2");
        var build3 = GivenANewBuildId(103, "gha3");
        SubscriptionUpdateWorkItem update1 = GivenSubscriptionUpdate(false, build1.Id, "11111111-1111-1111-1111-111111111111");
        SubscriptionUpdateWorkItem update2 = GivenSubscriptionUpdate(false, build2.Id, "22222222-2222-2222-2222-222222222222");
        SubscriptionUpdateWorkItem update3 = GivenSubscriptionUpdate(false, build3.Id, "33333333-3333-3333-3333-333333333333");
        List<DependencyUpdate> deps1 = GivenDependencyUpdates('a', build1.Id);
        List<DependencyUpdate> deps2 = GivenDependencyUpdates('b', build2.Id);
        List<DependencyUpdate> deps3 = GivenDependencyUpdates('c', build3.Id);

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
    }

    [Test]
    public async Task ShouldReturnCorrectPRDescriptionForCodeEnabledSubscription()
    {
        Build build = GivenANewBuildId(101, "abc1");
        build.GitHubRepository = "https://github.com/foo/foobar";
        build.GitHubBranch = "main";
        build.AzureDevOpsBuildNumber = "20230205.2";
        build.AzureDevOpsAccount = "foo";
        build.AzureDevOpsProject = "bar";
        build.AzureDevOpsBuildId = 1234;
        SubscriptionUpdateWorkItem update = GivenSubscriptionUpdate(false, build.Id, guid: "11111111-1111-1111-1111-111111111111", SubscriptionType.DependenciesAndSources);

        string mockPreviousCommitSha = "SHA1234567890";

        string? description = null;
        await Execute(
            async context =>
            {
                var builder = ActivatorUtilities.CreateInstance<PullRequestBuilder>(context);
                description = await builder.GenerateCodeFlowPRDescriptionAsync(update, mockPreviousCommitSha);
            });

        description.Should().Be(
            $"""
            [marker]: <> (Begin:11111111-1111-1111-1111-111111111111)

            This pull request is bringing source changes from **The best repo**.

            - **Subscription**: 11111111-1111-1111-1111-111111111111
            - **Build**: [20230205.2](https://dev.azure.com/foo/bar/_build/results?buildId=1234)
            - **Date Produced**: {build.DateProduced.ToUniversalTime():MMMM d, yyyy h:mm:ss tt UTC}
            - **Source Diff**: [MY-SHA-](https://github.com/foo/foobar/compare/SHA1234567890..abc1)
            - **Commit**: [abc1](https://github.com/foo/foobar/commit/abc1)
            - **Branch**: main

            [marker]: <> (End:11111111-1111-1111-1111-111111111111)
            """);
    }

    private const string RegexTestString1 = """
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
    private const string RegexTestString3 = """
        this
        string
        shouldn't
        have
        any
        matches
        """;

    private const string RegexTestString4 = """
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
                    "new-branch"); ;
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
            incoherencies: []);

        _barClient
            .Setup(x => x.GetBuildAsync(id))
            .ReturnsAsync(build);

        return build;
    }
}
