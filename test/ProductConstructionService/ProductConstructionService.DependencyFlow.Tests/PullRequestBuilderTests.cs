// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using AwesomeAssertions;
using Maestro.MergePolicies;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

using Asset = Microsoft.DotNet.ProductConstructionService.Client.Models.Asset;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
internal class PullRequestBuilderTests : SubscriptionOrPullRequestUpdaterTests
{
    private Mock<IBasicBarClient> _barClient = null!;

    [SetUp]
    public void PullRequestBuilderTests_SetUp()
    {
        _barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
    }

    protected override void RegisterServices(IServiceCollection services)
    {
        foreach (var (_, remote) in DarcRemotes)
        {
            remote.Setup(r => r.GetUpdatedDependencyFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<DependencyDetail>>(), It.IsAny<UnixPath>()))
                .ReturnsAsync([new GitFile("path", "content")]);
        }
        services.AddSingleton(_barClient.Object);

        base.RegisterServices(services);
    }

    [Test]
    public async Task ShouldReturnCalculateCorrectPRDescriptionWhenCoherencyUpdate()
    {
        var build = GivenANewBuildId(101, "abc1234");
        SubscriptionUpdateWorkItem update = GivenSubscriptionUpdate(true, build.Id, new Guid("11111111-1111-1111-1111-111111111111"));
        List<DependencyUpdate> deps = GivenDependencyUpdates('a', build.Id);
        TargetRepoDependencyUpdates requiredUpdates = GetTargetRepoDependencyUpdates(
            update,
            nonCoherencyUpdates: [],
            coherencyUpdates: deps,
            UnixPath.Empty);

        var description = await GeneratePullRequestDescription(requiredUpdates);
        description.ToString().Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps));
    }

    [Test]
    public async Task ShouldReturnCalculateCorrectPRDescriptionWhenNonCoherencyUpdate()
    {
        var build1 = GivenANewBuildId(101, "abc1234");
        var build2 = GivenANewBuildId(102, "def2345");
        SubscriptionUpdateWorkItem update1 = GivenSubscriptionUpdate(false, build1.Id, new Guid("11111111-1111-1111-1111-111111111111"));
        SubscriptionUpdateWorkItem update2 = GivenSubscriptionUpdate(false, build2.Id, new Guid("22222222-2222-2222-2222-222222222222"));
        List<DependencyUpdate> deps1 = GivenDependencyUpdates('a', build1.Id);
        List<DependencyUpdate> deps2 = GivenDependencyUpdates('b', build2.Id);
        TargetRepoDependencyUpdates requiredUpdates1 = GetTargetRepoDependencyUpdates(
            update1,
            nonCoherencyUpdates: deps1,
            coherencyUpdates: [],
            UnixPath.Empty);
        UnixPath path2 = new("src/arcade");
        TargetRepoDependencyUpdates requiredUpdates2 = GetTargetRepoDependencyUpdates(
            update2,
            nonCoherencyUpdates: deps2,
            coherencyUpdates: [],
            path2);

        var description = await GeneratePullRequestDescription(requiredUpdates1);
        description = await GeneratePullRequestDescription(requiredUpdates2, description);

        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(GetUpdatedDependenciesPerPath(UnixPath.Empty, deps1), 1));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(GetUpdatedDependenciesPerPath(path2, deps2), 3));
    }

    [Test]
    public async Task ShouldReturnCalculateCorrectPRDescriptionWhenUpdatingExistingPR()
    {
        var build1 = GivenANewBuildId(101, "abc1234");
        var build2 = GivenANewBuildId(102, "def2345");
        var build3 = GivenANewBuildId(103, "gha3456");
        var build4 = GivenANewBuildId(104, "gha3777");
        SubscriptionUpdateWorkItem update1 = GivenSubscriptionUpdate(false, build1.Id, new Guid("11111111-1111-1111-1111-111111111111"));
        SubscriptionUpdateWorkItem update2 = GivenSubscriptionUpdate(false, build2.Id, new Guid("22222222-2222-2222-2222-222222222222"));
        SubscriptionUpdateWorkItem update3 = GivenSubscriptionUpdate(false, build3.Id, new Guid("33333333-3333-3333-3333-333333333333"));
        SubscriptionUpdateWorkItem update4 = GivenSubscriptionUpdate(false, build4.Id, new Guid("22222222-2222-2222-2222-222222222222"));
        List<DependencyUpdate> deps1 = GivenDependencyUpdates('a', build1.Id);
        List<DependencyUpdate> deps2 = GivenDependencyUpdates('b', build2.Id);
        List<DependencyUpdate> deps3 = GivenDependencyUpdates('c', build3.Id);
        List<DependencyUpdate> deps4 = GivenDependencyUpdates('e', build4.Id);
        TargetRepoDependencyUpdates requiredUpdates1 = GetTargetRepoDependencyUpdates(
            update1,
            nonCoherencyUpdates: deps1,
            coherencyUpdates: [],
            UnixPath.Empty);
        TargetRepoDependencyUpdates requiredUpdates2 = GetTargetRepoDependencyUpdates(
            update2,
            nonCoherencyUpdates: deps2,
            coherencyUpdates: [],
            UnixPath.Empty);
        TargetRepoDependencyUpdates requiredUpdates3 = GetTargetRepoDependencyUpdates(
            update3,
            nonCoherencyUpdates: deps3,
            coherencyUpdates: [],
            UnixPath.Empty);

        var description = await GeneratePullRequestDescription(requiredUpdates1);
        description = await GeneratePullRequestDescription(requiredUpdates2, description);
        description = await GeneratePullRequestDescription(requiredUpdates3, description);

        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(GetUpdatedDependenciesPerPath(UnixPath.Empty, deps1), 1));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(GetUpdatedDependenciesPerPath(UnixPath.Empty, deps2), 3));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(GetUpdatedDependenciesPerPath(UnixPath.Empty, deps3), 5));

        List<DependencyUpdate> deps22 = GivenDependencyUpdates('d', build3.Id);

        requiredUpdates2.DirectoryUpdates[UnixPath.Empty].NonCoherencyUpdates = deps22;
        description = await GeneratePullRequestDescription(requiredUpdates2, description);

        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(GetUpdatedDependenciesPerPath(UnixPath.Empty, deps1), 1));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(GetUpdatedDependenciesPerPath(UnixPath.Empty, deps3), 5));
        description.Should().NotContain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(GetUpdatedDependenciesPerPath(UnixPath.Empty, deps2), 3));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(GetUpdatedDependenciesPerPath(UnixPath.Empty, deps22), 7));

        TargetRepoDependencyUpdates requiredUpdates4 = GetTargetRepoDependencyUpdates(
            update4,
            nonCoherencyUpdates: deps4,
            coherencyUpdates: [],
            UnixPath.Empty);

        description = await GeneratePullRequestDescription(requiredUpdates4, description);
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(GetUpdatedDependenciesPerPath(UnixPath.Empty, deps1), 1));
        description.Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(GetUpdatedDependenciesPerPath(UnixPath.Empty, deps4), 9));
        description.Should().NotContain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(GetUpdatedDependenciesPerPath(UnixPath.Empty, deps2), 3));
    }

    [Test]
    public async Task ShouldReturnCorrectPRDescriptionForCodeEnabledSubscription()
    {
        string originalCommitSha = "abc1234567";
        string commitSha = originalCommitSha;
        Build build = GivenANewBuildId(101, commitSha);
        build.GitHubRepository = "https://github.com/foo/foobar";
        build.GitHubBranch = "main";
        build.AzureDevOpsBuildNumber = "20230205.2";
        build.AzureDevOpsAccount = "foo";
        build.AzureDevOpsProject = "bar";
        build.AzureDevOpsBuildId = 1234;

        GivenACodeFlowSubscription(new());
        Subscription.ChannelId = 1234;

        List<DependencyUpdateSummary> dependencyUpdates = GivenDependencyUpdateSummaries();

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
                description = await builder.GenerateCodeFlowPRDescription(
                    build,
                    ToClientModelSubscription(Subscription),
                    "pr-branch",
                    mockPreviousCommitSha,
                    dependencyUpdates,
                    upstreamRepoDiffs,
                    currentDescription: null);
            });

        string shortCommitSha = commitSha.Substring(0, 7);
        string shortPreviousCommitSha = mockPreviousCommitSha.Substring(0, 7);

        description.Should().Be(
            $"""
            
            > [!NOTE]
            > This is a codeflow update. It may contain both source code changes from
            > [the VMR]({build.GetRepository()})
            > as well as dependency updates. Learn more [here]({PullRequestBuilder.CodeFlowPrFaqUri}).
            
            This pull request brings the following source code changes

            [marker]: <> (Begin:{Subscription.Id})

            ## From {build.GitHubRepository}
            - **Subscription**: [{Subscription.Id}](https://maestro.dot.net/subscriptions?search={Subscription.Id})
            - **Build**: [{build.AzureDevOpsBuildNumber}](https://dev.azure.com/{build.AzureDevOpsAccount}/{build.AzureDevOpsProject}/_build/results?buildId={build.AzureDevOpsBuildId}) ([101](https://maestro.dot.net/channel/1234/github:foo:foobar/build/101))
            - **Date Produced**: {build.DateProduced.ToUniversalTime():MMMM d, yyyy h:mm:ss tt UTC}
            - **Commit**: [{commitSha}]({build.GitHubRepository}/commit/{commitSha})
            - **Commit Diff**: [{shortPreviousCommitSha}...{shortCommitSha}]({build.GitHubRepository}/compare/{mockPreviousCommitSha}...{commitSha})
            - **Branch**: [main]({build.GitHubRepository}/tree/main)

            **Updated Dependencies**
            - From [1.0.0 to 2.0.0]({build.GitHubRepository}/compare/abc123...def456)
              - Foo.Bar
              - Foo.Biz
            - From [1.0.0 to 2.0.0]({build.GitHubRepository}/compare/uvw789...xyz890)
              - Biz.Boz

            [marker]: <> (End:{Subscription.Id})
            [marker]: <> (Start:Footer:CodeFlow PR)
            
            ## Associated changes in source repos
            - https://github.com/foo/bar/compare/oldSha123...newSha789
            - https://github.com/foo/boz/compare/oldSha234...newSha678
            - https://github.com/foo/baz/compare/oldSha345...newSha567

            <details>
            <summary>Diff the source with this PR branch</summary>

            ```bash
            darc vmr diff --name-only https://github.com/foo/foobar:abc1234567..https://github.com/maestro-auth-test/dnceng-vmr:pr-branch
            ```
            </details>

            [marker]: <> (End:Footer:CodeFlow PR)
            """);

        // We add another update to see how it handles the existing link references etc

        commitSha = "def888999222";
        shortCommitSha = commitSha.Substring(0, 7);
        build = GivenANewBuildId(101, commitSha);
        build.GitHubRepository = "https://github.com/foo/foobar";
        build.GitHubBranch = "main";
        build.AzureDevOpsBuildNumber = "20230205.4";
        build.AzureDevOpsAccount = "foo";
        build.AzureDevOpsProject = "bar";
        build.AzureDevOpsBuildId = 5678;
        dependencyUpdates = [..dependencyUpdates.Select(u => new DependencyUpdateSummary()
        {
            DependencyName = u.DependencyName,
            FromCommitSha = u.FromCommitSha,
            ToCommitSha = commitSha,
            FromVersion = u.FromVersion,
            ToVersion = "3.0.0",
        })];

        await Execute(
            async context =>
            {
                var builder = ActivatorUtilities.CreateInstance<PullRequestBuilder>(context);
                description = await builder.GenerateCodeFlowPRDescription(
                    build,
                    ToClientModelSubscription(Subscription),
                    "pr-branch",
                    mockPreviousCommitSha,
                    dependencyUpdates,
                    upstreamRepoDiffs,
                    description);
            });

        description.Should().Be(
            $"""
            
            > [!NOTE]
            > This is a codeflow update. It may contain both source code changes from
            > [the VMR]({build.GetRepository()})
            > as well as dependency updates. Learn more [here]({PullRequestBuilder.CodeFlowPrFaqUri}).
            
            This pull request brings the following source code changes


            [marker]: <> (Begin:{Subscription.Id})

            ## From {build.GitHubRepository}
            - **Subscription**: [{Subscription.Id}](https://maestro.dot.net/subscriptions?search={Subscription.Id})
            - **Build**: [{build.AzureDevOpsBuildNumber}](https://dev.azure.com/{build.AzureDevOpsAccount}/{build.AzureDevOpsProject}/_build/results?buildId={build.AzureDevOpsBuildId}) ([101](https://maestro.dot.net/channel/1234/github:foo:foobar/build/101))
            - **Date Produced**: {build.DateProduced.ToUniversalTime():MMMM d, yyyy h:mm:ss tt UTC}
            - **Commit**: [{commitSha}]({build.GitHubRepository}/commit/{commitSha})
            - **Commit Diff**: [{shortPreviousCommitSha}...{shortCommitSha}]({build.GitHubRepository}/compare/{mockPreviousCommitSha}...{commitSha})
            - **Branch**: [main]({build.GitHubRepository}/tree/main)

            **Updated Dependencies**
            - From [1.0.0 to 3.0.0]({build.GitHubRepository}/compare/abc123...{commitSha.Substring(0, PullRequestBuilder.GitHubComparisonShaLength)})
              - Foo.Bar
              - Foo.Biz
            - From [1.0.0 to 3.0.0]({build.GitHubRepository}/compare/uvw789...def8889992)
              - Biz.Boz

            [marker]: <> (End:{Subscription.Id})

            [marker]: <> (Start:Footer:CodeFlow PR)

            ## Associated changes in source repos
            - https://github.com/foo/bar/compare/oldSha123...newSha789
            - https://github.com/foo/boz/compare/oldSha234...newSha678
            - https://github.com/foo/baz/compare/oldSha345...newSha567
  
            <details>
            <summary>Diff the source with this PR branch</summary>

            ```bash
            darc vmr diff --name-only https://github.com/foo/foobar:def888999222..https://github.com/maestro-auth-test/dnceng-vmr:pr-branch
            ```
            </details>

            [marker]: <> (End:Footer:CodeFlow PR)
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
            - Added [2.0.0](https://github.com/Foo/commit/def456)
              - Foo.Bar

            **Removed Dependencies**
            - Removed 1.0.0
              - Foo.Biz

            **Updated Dependencies**
            - From [1.0.0 to 2.0.0](https://github.com/Foo/compare/uvw789...xyz890)
              - Biz.Boz

            """);
    }

    [Test]
    public void ShouldGroupDependenciesWithSameVersionRange()
    {
        DependencyUpdateSummary groupedDependency1 = new()
        {
            DependencyName = "Foo.Bar",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            FromCommitSha = "abc123",
            ToCommitSha = "def456"
        };

        DependencyUpdateSummary groupedDependency2 = new()
        {
            DependencyName = "Foo.Biz",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            FromCommitSha = "abc123",
            ToCommitSha = "def456"
        };

        DependencyUpdateSummary separateDependency = new()
        {
            DependencyName = "Biz.Boz",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            FromCommitSha = "uvw789",
            ToCommitSha = "xyz890"
        };

        string dependencyBlock = PullRequestBuilder.CreateDependencyUpdateBlock([groupedDependency1, groupedDependency2, separateDependency], "https://github.com/Foo");

        dependencyBlock.Should().Be(
            """

            **Updated Dependencies**
            - From [1.0.0 to 2.0.0](https://github.com/Foo/compare/abc123...def456)
              - Foo.Bar
              - Foo.Biz
            - From [1.0.0 to 2.0.0](https://github.com/Foo/compare/uvw789...xyz890)
              - Biz.Boz

            """);
    }

    [Test]
    public void ShouldOrderDependenciesAlphabeticallyWithinGroups()
    {
        // Create dependencies in non-alphabetical order to verify sorting
        DependencyUpdateSummary dependencyZ = new()
        {
            DependencyName = "Zebra.Package",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            FromCommitSha = "abc123",
            ToCommitSha = "def456"
        };

        DependencyUpdateSummary dependencyA = new()
        {
            DependencyName = "Alpha.Package",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            FromCommitSha = "abc123",
            ToCommitSha = "def456"
        };

        DependencyUpdateSummary dependencyM = new()
        {
            DependencyName = "Middle.Package",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            FromCommitSha = "abc123",
            ToCommitSha = "def456"
        };

        // Pass dependencies in Z, A, M order to verify alphabetical sorting
        string dependencyBlock = PullRequestBuilder.CreateDependencyUpdateBlock([dependencyZ, dependencyA, dependencyM], "https://github.com/Foo");

        dependencyBlock.Should().Be(
            """

            **Updated Dependencies**
            - From [1.0.0 to 2.0.0](https://github.com/Foo/compare/abc123...def456)
              - Alpha.Package
              - Middle.Package
              - Zebra.Package

            """);
    }

    [Test]
    public async Task ShouldGroupDependenciesInDependencyFlowPRs()
    {
        var build1 = GivenANewBuildId(101, "abc1234");
        GivenACodeFlowSubscription(new());
        SubscriptionUpdateWorkItem update1 = GivenSubscriptionUpdate(false, build1.Id, Subscription.Id);

        // Create dependencies that should be grouped (same version range and commit range)
        List<DependencyUpdate> deps1 = [
            new DependencyUpdate
            {
                From = new DependencyDetail { Name = "Microsoft.TemplateEngine.Abstractions", Version = "10.0.100-preview.6.25317.107", Commit = "abc123" },
                To = new DependencyDetail { Name = "Microsoft.TemplateEngine.Abstractions", Version = "10.0.100-preview.6.25318.104", RepoUri = "https://amazing_uri.com", Commit = "def456" }
            },
            new DependencyUpdate
            {
                From = new DependencyDetail { Name = "Microsoft.TemplateEngine.Edge", Version = "10.0.100-preview.6.25317.107", Commit = "abc123" },
                To = new DependencyDetail { Name = "Microsoft.TemplateEngine.Edge", Version = "10.0.100-preview.6.25318.104", RepoUri = "https://amazing_uri.com", Commit = "def456" }
            },
            new DependencyUpdate
            {
                From = new DependencyDetail { Name = "Microsoft.TemplateEngine.Utils", Version = "10.0.100-preview.6.25317.107", Commit = "abc123" },
                To = new DependencyDetail { Name = "Microsoft.TemplateEngine.Utils", Version = "10.0.100-preview.6.25318.104", RepoUri = "https://amazing_uri.com", Commit = "def456" }
            }
        ];
        TargetRepoDependencyUpdates requiredUpdates = GetTargetRepoDependencyUpdates(
            update1,
            nonCoherencyUpdates: deps1,
            coherencyUpdates: [],
            UnixPath.Empty);

        foreach (var dependency in deps1.SelectMany(d => new[] { d.From, d.To }))
        {
            _barClient
                .Setup(x => x.GetAssetsAsync(dependency.Name, dependency.Version, null, null))
                .ReturnsAsync([new Asset(1, build1.Id, false, dependency.Name, dependency.Version, [])]);
        }

        var description = await GeneratePullRequestDescription(requiredUpdates);

        // The grouped dependencies should appear like:
        // - From [10.0.100-preview.6.25317.107 to 10.0.100-preview.6.25318.104][1]
        //   - Microsoft.TemplateEngine.Abstractions
        //   - Microsoft.TemplateEngine.Edge  
        //   - Microsoft.TemplateEngine.Utils
        description.Should().Contain("From [10.0.100-preview.6.25317.107 to 10.0.100-preview.6.25318.104][1]");
        description.Should().Contain("    - Microsoft.TemplateEngine.Abstractions");
        description.Should().Contain("    - Microsoft.TemplateEngine.Edge");
        description.Should().Contain("    - Microsoft.TemplateEngine.Utils");
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
        > This is a codeflow update. It may contain both source code changes from
        > [the VMR](https://github.com/foo/foobar)
        > as well as dependency updates. Learn more [here](https://github.com/dotnet/dotnet/tree/main/docs/Codeflow-PRs.md).

        This pull request brings the following source code changes

        [marker]: <> (Begin:11111111-1111-1111-1111-111111111111)

        ## From https://github.com/foo/foobar
        - **Subscription**: [11111111-1111-1111-1111-111111111111](https://maestro.dot.net/subscriptions?search=11111111-1111-1111-1111-111111111111)
        - **Build**: [20230205.2](https://dev.azure.com/foo/bar/_build/results?buildId=1234)
        - **Date Produced**: června 18, 2025 11:12:39 dop. UTC
        - **Commit**: [abc1234567](https://github.com/foo/foobar/commit/abc1234567)
        - **Commit Diff**: [SHA1234...abc1234](https://github.com/foo/foobar/compare/SHA1234567890...abc1234567)
        - **Branch**: [main](https://github.com/foo/foobar/tree/main)

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
        [4]:q
        [3]:q
        """;

    [TestCase(RegexTestString1, 43)]
    [TestCase(RegexTestString2, 1)]
    [TestCase(RegexTestString3, 1)]
    [TestCase(RegexTestString4, 5)]
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
        Guid id,
        SubscriptionType type = SubscriptionType.Dependencies) => new()
        {
            UpdaterId = id.ToString(),
            IsCoherencyUpdate = isCoherencyUpdate,
            SourceRepo = "The best repo",
            SubscriptionId = id,
            BuildId = buildId,
            SubscriptionType = type,
        };

    private static string BuildCorrectPRDescriptionWhenCoherencyUpdate(List<DependencyUpdate> deps)
    {
        var stringBuilder = new StringBuilder();
        foreach (DependencyUpdate dep in deps)
        {
            stringBuilder.AppendLine($"    - **{dep.To.Name}**: from {dep.From.Version} to {dep.To.Version} (parent: {dep.To.CoherentParentDependencyName})");
        }
        return stringBuilder.ToString();
    }

    private static string BuildCorrectPRDescriptionWhenNonCoherencyUpdate(Dictionary<UnixPath, List<DependencyUpdate>> updatedDependenciesPerPath, int startingId)
    {
        var builder = new StringBuilder();
        List<string> urls = [];
        int currentId = startingId;

        bool shouldAddDirectory = true;
        string padding = "  ";
        if (updatedDependenciesPerPath.Keys.Count == 1 && updatedDependenciesPerPath.Keys.First() == UnixPath.Empty)
        {
            shouldAddDirectory = false;
            padding = string.Empty;
        }
        foreach (var (relativePath, updatedDependencies) in updatedDependenciesPerPath)
        {
            if (shouldAddDirectory)
            {
                builder.AppendLine($"  - 📂 `{(UnixPath.IsEmptyPath(relativePath) ? "root" : relativePath)}`");
            }
            var dependencyGroups = updatedDependencies
                .GroupBy(dep => new
                {
                    FromVersion = dep.From.Version,
                    ToVersion = dep.To.Version,
                    FromCommit = dep.From.Commit,
                    ToCommit = dep.To.Commit,
                    RepoUri = dep.To.RepoUri
                })
                .ToList();

            foreach (var group in dependencyGroups)
            {
                var representative = group.First();

                urls.Add(PullRequestBuilder.GetChangesURI(representative.To.RepoUri, representative.From.Commit, representative.To.Commit));

                builder.AppendLine($"  {padding}- From [{representative.From.Version} to {representative.To.Version}][{currentId}]");
                foreach (var dep in group)
                {
                    builder.AppendLine($"     {padding}- {dep.To.Name}");
                }
                currentId++;
            }
        }
        builder.AppendLine();
        for (var i = 0; i < urls.Count; i++)
        {
            builder.AppendLine($"[{i + startingId}]: {urls[i]}");
        }

        return builder.ToString();
    }

    [Test]
    public void ShouldGroupAllTypesOfDependencyChanges()
    {
        // New Dependencies - multiple packages with same version
        DependencyUpdateSummary newDep1 = new()
        {
            DependencyName = "New.Package.Alpha",
            FromVersion = null,
            ToVersion = "3.0.0",
            FromCommitSha = null,
            ToCommitSha = "new123"
        };

        DependencyUpdateSummary newDep2 = new()
        {
            DependencyName = "New.Package.Beta",
            FromVersion = null,
            ToVersion = "3.0.0",
            FromCommitSha = null,
            ToCommitSha = "new123"
        };

        DependencyUpdateSummary newDep3 = new()
        {
            DependencyName = "New.Package.Gamma",
            FromVersion = null,
            ToVersion = "3.5.0",
            FromCommitSha = null,
            ToCommitSha = "new456"
        };

        // Removed Dependencies - multiple packages with same version
        DependencyUpdateSummary removedDep1 = new()
        {
            DependencyName = "Removed.Package.Zeta",
            FromVersion = "2.0.0",
            ToVersion = null,
            FromCommitSha = "old123",
            ToCommitSha = null
        };

        DependencyUpdateSummary removedDep2 = new()
        {
            DependencyName = "Removed.Package.Delta",
            FromVersion = "2.0.0",
            ToVersion = null,
            FromCommitSha = "old123",
            ToCommitSha = null
        };

        DependencyUpdateSummary removedDep3 = new()
        {
            DependencyName = "Removed.Package.Epsilon",
            FromVersion = "1.5.0",
            ToVersion = null,
            FromCommitSha = "old456",
            ToCommitSha = null
        };

        // Updated Dependencies - multiple packages with same version ranges
        DependencyUpdateSummary updatedDep1 = new()
        {
            DependencyName = "Updated.Package.Charlie",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            FromCommitSha = "update123",
            ToCommitSha = "update456"
        };

        DependencyUpdateSummary updatedDep2 = new()
        {
            DependencyName = "Updated.Package.Bravo",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            FromCommitSha = "update123",
            ToCommitSha = "update456"
        };

        DependencyUpdateSummary updatedDep3 = new()
        {
            DependencyName = "Updated.Package.Alpha",
            FromVersion = "1.5.0",
            ToVersion = "2.5.0",
            FromCommitSha = "update789",
            ToCommitSha = "update012"
        };

        // Pass dependencies in non-alphabetical order to verify sorting within groups
        string dependencyBlock = PullRequestBuilder.CreateDependencyUpdateBlock([
            newDep2, newDep1, newDep3,  // new deps out of order
            removedDep1, removedDep3, removedDep2,  // removed deps out of order
            updatedDep2, updatedDep3, updatedDep1   // updated deps out of order
        ], "https://github.com/Test");

        dependencyBlock.Should().Be(
            """

            **New Dependencies**
            - Added [3.0.0](https://github.com/Test/commit/new123)
              - New.Package.Alpha
              - New.Package.Beta
            - Added [3.5.0](https://github.com/Test/commit/new456)
              - New.Package.Gamma

            **Removed Dependencies**
            - Removed 2.0.0
              - Removed.Package.Delta
              - Removed.Package.Zeta
            - Removed 1.5.0
              - Removed.Package.Epsilon

            **Updated Dependencies**
            - From [1.0.0 to 2.0.0](https://github.com/Test/compare/update123...update456)
              - Updated.Package.Bravo
              - Updated.Package.Charlie
            - From [1.5.0 to 2.5.0](https://github.com/Test/compare/update789...update012)
              - Updated.Package.Alpha

            """);
    }

    private async Task<string> GeneratePullRequestDescription(
        TargetRepoDependencyUpdates updates,
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

    [Test]
    public async Task ShouldGenerateEnhancedBuildLinksWithBarDetails()
    {
        // Given
        string commitSha = "abc1234567";
        int buildId = 12345;
        int channelId = 789;

        GivenACodeFlowSubscription(new());
        Subscription.ChannelId = channelId;

        Build build = GivenANewBuildId(buildId, commitSha);
        build.AzureDevOpsAccount = "dnceng";
        build.AzureDevOpsProject = "internal";
        build.AzureDevOpsBuildId = 2782173;
        build.AzureDevOpsBuildNumber = "20250828.10";
        build.GitHubRepository = "https://github.com/dotnet/roslyn";

        string? description = null;
        await Execute(
            async context =>
            {
                var builder = ActivatorUtilities.CreateInstance<PullRequestBuilder>(context);
                description = await builder.GenerateCodeFlowPRDescription(
                    build,
                    ToClientModelSubscription(Subscription),
                    "pr-head",
                    "previoussha123",
                    dependencyUpdates: [],
                    upstreamRepoDiffs: [],
                    currentDescription: null);
            });

        // Then - Should contain enhanced build link with BAR details
        description.Should().NotBeNull();
        description!.Should().Contain($"[{build.AzureDevOpsBuildNumber}]({build.GetBuildLink()})");
        description.Should().Contain($"([{buildId}](https://maestro.dot.net/channel/{channelId}/github:dotnet:roslyn/build/{buildId}))");

        // Verify the complete enhanced format is present
        var expectedEnhancedBuildLine = $"- **Build**: [{build.AzureDevOpsBuildNumber}]({build.GetBuildLink()}) ([{buildId}](https://maestro.dot.net/channel/{channelId}/github:dotnet:roslyn/build/{buildId}))";
        description.Should().Contain(expectedEnhancedBuildLine);
    }

    [Test]
    public async Task ShouldGenerateEnhancedBuildLinksWithAzureDevOpsRepos()
    {
        // Given - Test Azure DevOps repository URL to slug conversion
        string commitSha = "def7654321";
        int buildId = 54321;
        int channelId = 456;

        GivenACodeFlowSubscription(new());
        Subscription.ChannelId = channelId;

        Build build = GivenANewBuildId(buildId, commitSha);
        build.AzureDevOpsAccount = "dnceng";
        build.AzureDevOpsProject = "internal";
        build.AzureDevOpsBuildId = 1234567;
        build.AzureDevOpsBuildNumber = "20250829.5";
        build.AzureDevOpsRepository = "https://dev.azure.com/dnceng/internal/_git/dotnet-runtime";
        build.GitHubRepository = null; // Clear GitHub repo to use Azure DevOps repo

        string? description = null;
        await Execute(
            async context =>
            {
                var builder = ActivatorUtilities.CreateInstance<PullRequestBuilder>(context);
                description = await builder.GenerateCodeFlowPRDescription(
                    build,
                    ToClientModelSubscription(Subscription),
                    "pr-head",
                    "previoussha456",
                    dependencyUpdates: [],
                    upstreamRepoDiffs: [],
                    currentDescription: null);
            });

        // Then - Should contain enhanced build link with BAR details for Azure DevOps repo
        description.Should().NotBeNull();
        description!.Should().Contain($"[{build.AzureDevOpsBuildNumber}]({build.GetBuildLink()})");
        description.Should().Contain($"([{buildId}](https://maestro.dot.net/channel/{channelId}/azdo:dnceng:internal:dotnet-runtime/build/{buildId}))");

        // Verify the complete enhanced format is present with Azure DevOps slug
        var expectedEnhancedBuildLine = $"- **Build**: [{build.AzureDevOpsBuildNumber}]({build.GetBuildLink()}) ([{buildId}](https://maestro.dot.net/channel/{channelId}/azdo:dnceng:internal:dotnet-runtime/build/{buildId}))";
        description.Should().Contain(expectedEnhancedBuildLine);
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

    private static TargetRepoDependencyUpdates GetTargetRepoDependencyUpdates(
        SubscriptionUpdateWorkItem update,
        List<DependencyUpdate> nonCoherencyUpdates,
        List<DependencyUpdate> coherencyUpdates,
        UnixPath path) =>
        new()
        {
            SubscriptionUpdate = update,
            DirectoryUpdates = new Dictionary<UnixPath, TargetRepoDirectoryDependencyUpdates>
            {
                {
                    path,
                    new TargetRepoDirectoryDependencyUpdates
                    {
                        NonCoherencyUpdates = nonCoherencyUpdates,
                        CoherencyUpdates = coherencyUpdates
                    }
                }
            }
        };

    private static Dictionary<UnixPath, List<DependencyUpdate>> GetUpdatedDependenciesPerPath(UnixPath path, List<DependencyUpdate> nonCoherencyUpdates) =>
        new()
        {
            { path, nonCoherencyUpdates }
        };

    private static Subscription ToClientModelSubscription(Maestro.Data.Models.Subscription other)
    {
        return new Subscription(
            other.Id,
            other.Enabled,
            other.SourceEnabled,
            other.SourceRepository,
            other.TargetRepository,
            other.TargetBranch,
            other.PullRequestFailureNotificationTags,
            other.SourceDirectory,
            other.TargetDirectory,
            other.ExcludedAssets?.Select(a => a.Filter).ToList());
    }
}
