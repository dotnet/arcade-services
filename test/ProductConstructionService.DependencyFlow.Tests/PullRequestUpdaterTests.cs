// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using AwesomeAssertions;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.Tests.Mocks;
using ProductConstructionService.DependencyFlow.WorkItems;

using Asset = Maestro.Data.Models.Asset;
using AssetData = Microsoft.DotNet.ProductConstructionService.Client.Models.AssetData;

namespace ProductConstructionService.DependencyFlow.Tests;

internal abstract class PullRequestUpdaterTests : SubscriptionOrPullRequestUpdaterTests
{
    private const long InstallationId = 1174;
    protected const string InProgressPrUrl = "https://github.com/owner/repo/pull/10";
    protected string InProgressPrHeadBranch { get; private set; } = "pr.head.branch";
    protected const string InProgressPrHeadBranchSha = "pr.head.branch.sha";
    protected const string ConflictPRRemoteSha = "sha3333";

    private Mock<IPcsVmrBackFlower> _backFlower = null!;
    private Mock<IPcsVmrForwardFlower> _forwardFlower = null!;
    private Mock<ILocalLibGit2Client> _gitClient = null!;

    [SetUp]
    public void PullRequestUpdaterTests_SetUp()
    {
        _backFlower = new();
        _forwardFlower = new();
        _gitClient = new();
    }

    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);

        services.AddSingleton(_backFlower.Object);
        services.AddSingleton(_forwardFlower.Object);
        services.AddSingleton(_gitClient.Object);

        CodeFlowResult codeFlowRes = new(true, [], new NativePath(VmrPath), []);
        _forwardFlower.SetReturnsDefault(Task.FromResult(codeFlowRes));
        _backFlower.SetReturnsDefault(Task.FromResult(codeFlowRes));
        _gitClient.SetReturnsDefault(Task.CompletedTask);

        services.AddGitHubTokenProvider();
        services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        services.AddScoped<IBasicBarClient, SqlBarClient>();
        services.AddTransient<IPullRequestBuilder, PullRequestBuilder>();
        services.AddCodeflow(TmpPath, VmrPath);
    }

    protected override Task BeforeExecute(IServiceProvider context)
    {
        var dbContext = context.GetRequiredService<BuildAssetRegistryContext>();
        dbContext.Repositories.Add(
            new Repository
            {
                RepositoryName = TargetRepo,
                InstallationId = InstallationId
            });

        context.GetRequiredService<IVmrInfo>().VmrUri = VmrUri;

        return base.BeforeExecute(context);
    }

    protected void ExpectPrMetadataToBeUpdated()
    {
        DarcRemotes[Subscription.TargetRepository]
            .Setup(x => x.UpdatePullRequestAsync(
                Subscription.SourceEnabled ? VmrPullRequestUrl : InProgressPrUrl,
                It.IsAny<PullRequest>()));
    }

    protected void ThenGetRequiredUpdatesShouldHaveBeenCalled(Build withBuild, bool prExists, Func<Asset, bool>? assetFilter = null)
    {
        var assets = new List<IReadOnlyCollection<AssetData>>();
        var dependencies = new List<IReadOnlyCollection<DependencyDetail>>();
        var relativeBasePath = UnixPath.Empty;

        UpdateResolver
            .Verify(r => r.GetRequiredNonCoherencyUpdates(SourceRepo, NewCommit, Capture.In(assets), Capture.In(dependencies)));

        DarcRemotes[TargetRepo]
            .Verify(r => r.GetDependenciesAsync(TargetRepo, prExists ? InProgressPrHeadBranch : TargetBranch, null, relativeBasePath));

        UpdateResolver
            .Verify(r => r.GetRequiredCoherencyUpdatesAsync(Capture.In(dependencies)));

        assets.Should()
            .BeEquivalentTo(
                new List<List<AssetData>>
                {
                    withBuild.Assets
                        .Where(assetFilter ?? (_ => true))
                        .Select(a => new AssetData(false)
                        {
                            Name = a.Name,
                            Version = a.Version
                        })
                        .ToList()
                });
    }

    protected void AndCreateNewBranchShouldHaveBeenCalled()
    {
        var captureNewBranch = new CaptureMatch<string>(newBranch => InProgressPrHeadBranch = newBranch);
        DarcRemotes[TargetRepo]
            .Verify(r => r.CreateNewBranchAsync(TargetRepo, TargetBranch, Capture.With(captureNewBranch)));
    }

    protected void AndCommitUpdatesShouldHaveBeenCalled(Build withUpdatesFromBuild, Func<Asset, bool>? assetFilter = null)
    {
        var updatedDependencies = new List<List<DependencyDetail>>();
        DarcRemotes[TargetRepo]
            .Verify(
                r => r.GetUpdatedDependencyFiles(
                    TargetRepo,
                    InProgressPrHeadBranch,
                    Capture.In(updatedDependencies),
                    It.IsAny<UnixPath>()));

        updatedDependencies.Should()
            .BeEquivalentTo(
                new List<List<DependencyDetail>>
                {
                    withUpdatesFromBuild.Assets
                        .Where(assetFilter ?? (_ => true))
                        .Select(a => new DependencyDetail
                        {
                            Name = a.Name,
                            Version = a.Version,
                            RepoUri = withUpdatesFromBuild.GitHubRepository,
                            Commit = "sha3333"
                        })
                        .ToList()
                });
    }

    protected void AndCreatePullRequestShouldHaveBeenCalled()
    {
        var pullRequests = new List<PullRequest>();
        DarcRemotes[TargetRepo]
            .Verify(r => r.CreatePullRequestAsync(TargetRepo, Capture.In(pullRequests)));

        pullRequests.Should()
            .BeEquivalentTo(
                new List<PullRequest>
                {
                    new()
                    {
                        BaseBranch = TargetBranch,
                        HeadBranch = InProgressPrHeadBranch,
                    }
                },
                options => options.Excluding(pr => pr.Title).Excluding(pr => pr.Description));

        ValidatePRDescriptionContainsLinks(pullRequests[0]);
    }

    protected void AndCodeFlowPullRequestShouldHaveBeenCreated()
    {
        var pullRequests = new List<PullRequest>();
        DarcRemotes[VmrUri]
            .Verify(r => r.CreatePullRequestAsync(VmrUri, Capture.In(pullRequests)));

        pullRequests.Should()
            .BeEquivalentTo(
                new List<PullRequest>
                {
                    new()
                    {
                        BaseBranch = TargetBranch,
                        HeadBranch = pullRequests.First().HeadBranch,
                    }
                },
                options => options
                    .Excluding(pr => pr.Title)
                    .Excluding(pr => pr.Description));
    }

    protected void ThenCodeShouldHaveBeenBackflown(Build build)
    {
        _backFlower
            .Verify(b => b.FlowBackAsync(
                It.Is<Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription>(s => s.Id == Subscription.Id),
                It.Is<Microsoft.DotNet.ProductConstructionService.Client.Models.Build>(b => b.Id == build.Id && b.Commit == build.Commit),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _gitClient.Verify(
            g => g.Push(TargetRepo, It.IsAny<string>(), TargetRepo, It.IsAny<LibGit2Sharp.Identity>()),
            Times.Once);
    }

    protected void ThenCodeShouldHaveBeenFlownForward(Build build)
    {
        _forwardFlower
            .Verify(b => b.FlowForwardAsync(
                It.Is<Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription>(s => s.Id == Subscription.Id),
                It.Is<Microsoft.DotNet.ProductConstructionService.Client.Models.Build>(b => b.Id == build.Id && b.Commit == build.Commit),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _gitClient.Verify(
            g => g.Push(VmrPath, It.IsAny<string>(), VmrUri, It.IsAny<LibGit2Sharp.Identity>(), It.IsAny<bool>()),
            Times.Once);
    }

    protected void AndCodeShouldHaveBeenFlownForward(Build build)
        => ThenCodeShouldHaveBeenFlownForward(build);

    protected static void ValidatePRDescriptionContainsLinks(PullRequest pr)
    {
        pr.Description.Should().Contain("][1]");
        pr.Description.Should().Contain("[1]:");
    }

    protected void CreatePullRequestShouldReturnAValidValue(string? targetRepo = null, string? prUrl = null)
    {
        var repo = string.IsNullOrEmpty(targetRepo)
            ? Subscription.TargetDirectory != null ? VmrUri : TargetRepo
            : targetRepo;
        var url = string.IsNullOrEmpty(prUrl)
            ? Subscription.TargetDirectory != null ? VmrPullRequestUrl : InProgressPrUrl
            : prUrl;

        DarcRemotes.GetOrAddValue(repo, () => new Mock<IRemote>())
            .Setup(s => s.CreatePullRequestAsync(It.IsAny<string>(), It.IsAny<PullRequest>()))
            .Callback<string, PullRequest>((repo, pr) =>
            {
                if (repo == VmrUri)
                {
                    InProgressPrHeadBranch = pr.HeadBranch;
                }
            })
            .ReturnsAsync(new PullRequest
            {
                Url = url,
                HeadBranch = InProgressPrHeadBranch,
                HeadBranchSha = InProgressPrHeadBranchSha,
                BaseBranch = TargetBranch,
                Status = PrStatus.Open,
            });
    }

    protected void AndUpdatePullRequestShouldHaveBeenCalled()
    {
        var pullRequests = new List<PullRequest>();
        DarcRemotes[TargetRepo]
            .Verify(r => r.UpdatePullRequestAsync(InProgressPrUrl, Capture.In(pullRequests)));
        pullRequests.Should()
            .BeEquivalentTo(
                new List<PullRequest>
                {
                    new()
                    {
                        BaseBranch = TargetBranch,
                        HeadBranch = InProgressPrHeadBranch,
                        HeadBranchSha = InProgressPrHeadBranchSha,
                        Status = PrStatus.Open,
                    }
                },
                options => options.Excluding(pr => pr.Title).Excluding(pr => pr.Description));
    }

    protected void AndSubscriptionShouldBeUpdatedForMergedPullRequest(Build withBuild)
    {
        Subscription.LastAppliedBuildId.Should().Be(withBuild.Id);
    }

    protected void WithRequireNonCoherencyUpdates()
    {
        UpdateResolver
            .Setup(r => r.GetRequiredNonCoherencyUpdates(
                SourceRepo,
                NewCommit,
                It.IsAny<IReadOnlyCollection<AssetData>>(),
                It.IsAny<IReadOnlyCollection<DependencyDetail>>()))
            .Returns(
                (string sourceRepo, string sourceSha, IReadOnlyCollection<AssetData> assets, IReadOnlyCollection<DependencyDetail> dependencies) =>
                    // Just make from->to identical.
                    assets
                        .Select(d => new DependencyUpdate
                        {
                            From = new DependencyDetail
                            {
                                Name = d.Name,
                                Version = d.Version,
                                RepoUri = sourceRepo,
                                Commit = sourceSha
                            },
                            To = new DependencyDetail
                            {
                                Name = d.Name,
                                Version = d.Version,
                                RepoUri = sourceRepo,
                                Commit = "sha3333"
                            },
                        })
                        .ToList());
    }

    protected void WithNoRequiredCoherencyUpdates()
    {
        UpdateResolver
            .Setup(r => r.GetRequiredCoherencyUpdatesAsync(It.IsAny<IReadOnlyCollection<DependencyDetail>>()))
            .ReturnsAsync([]);
    }

    protected void WithFailsStrictCheckForCoherencyUpdates()
    {
        UpdateResolver
            .Setup(r => r.GetRequiredCoherencyUpdatesAsync(It.IsAny<IReadOnlyCollection<DependencyDetail>>()))
            .ReturnsAsync(
                (IEnumerable<DependencyDetail> dependencies) =>
                {
                    var fakeCoherencyError = new CoherencyError()
                    {
                        Dependency = new DependencyDetail() { Name = "fakeDependency" },
                        Error = "Repo @ commit does not contain dependency fakeDependency",
                        PotentialSolutions = new List<string>()
                    };
                    throw new DarcCoherencyException(fakeCoherencyError);
                });
    }

    protected IDisposable WithExistingPullRequest(Build forBuild, bool canUpdate, int nextBuildToProcess = 0, bool setupRemoteMock = true, Func<Asset, bool>? assetFilter = null)
        => canUpdate
            ? WithExistingPullRequest(forBuild, PrStatus.Open, null, nextBuildToProcess, setupRemoteMock, assetFilter)
            : WithExistingPullRequest(forBuild, PrStatus.Open, MergePolicyEvaluationStatus.Pending, nextBuildToProcess, setupRemoteMock, assetFilter);

    protected IDisposable WithExistingPullRequest(
        Build forBuild,
        PrStatus prStatus,
        MergePolicyEvaluationStatus? policyEvaluationStatus,
        int nextBuildToProcess = 0,
        bool setupRemoteMock = true,
        Func<Asset, bool>? assetFilter = null)
    {
        var prUrl = Subscription.TargetDirectory != null
            ? VmrPullRequestUrl
            : InProgressPrUrl;

        AfterDbUpdateActions.Add(() =>
        {
            var pr = CreatePullRequestState(forBuild, prUrl, nextBuildToProcess, assetFilter: assetFilter);
            SetState(Subscription, pr);
            SetExpectedPullRequestState(Subscription, pr);
        });

        var targetRepo = Subscription.TargetDirectory != null ? VmrUri : TargetRepo;

        var remote = DarcRemotes.GetOrAddValue(targetRepo, () => CreateMock<IRemote>());

        if (!setupRemoteMock)
        {
            return Disposable.Create(remote.VerifyAll);
        }

        var results = policyEvaluationStatus.HasValue
            ? new MergePolicyEvaluationResults(
                [
                    new MergePolicyEvaluationResult(
                        policyEvaluationStatus.Value,
                        "Check",
                        "Fake one",
                        "Policy",
                        "Some policy")
                ],
                string.Empty)
            : new MergePolicyEvaluationResults([], string.Empty);

        remote
            .Setup(r => r.GetPullRequestAsync(prUrl))
            .ReturnsAsync(
                new PullRequest
                {
                    HeadBranch = InProgressPrHeadBranch,
                    HeadBranchSha = InProgressPrHeadBranchSha,
                    BaseBranch = TargetBranch,
                    Status = prStatus,
                });

        remote
            .Setup(r => r.CreateOrUpdatePullRequestMergeStatusInfoAsync(prUrl, results.Results.ToImmutableList()))
            .Returns(Task.CompletedTask);

        MergePolicyEvaluator
            .Setup(x => x.EvaluateAsync(
                It.Is<PullRequestUpdateSummary>(pr => pr.Url == prUrl),
                It.IsAny<IRemote>(),
                It.IsAny<IReadOnlyList<MergePolicyDefinition>>(),
                It.IsAny<MergePolicyEvaluationResults?>(),
                It.IsAny<string>()))
            .ReturnsAsync(results.Results);

        return Disposable.Create(remote.VerifyAll);
    }

    protected IDisposable WithExistingCodeFlowPullRequest(
        Build forBuild,
        bool canUpdate,
        int nextBuildToProcess = 0,
        bool newChangeWillConflict = false,
        string? headBranchSha = null,
        bool willFlowNewBuild = false,
        bool mockMergePolicyEvaluator = true,
        bool? sourceRepoNotified = null)
        => WithExistingCodeFlowPullRequest(
            forBuild,
            PrStatus.Open,
            canUpdate ? null : MergePolicyEvaluationStatus.Pending,
            nextBuildToProcess,
            newChangeWillConflict,
            headBranchSha,
            willFlowNewBuild: willFlowNewBuild,
            mockMergePolicyEvaluator: mockMergePolicyEvaluator,
            sourceRepoNotified: sourceRepoNotified);

    protected IDisposable WithExistingCodeFlowPullRequest(
        Build forBuild,
        PrStatus prStatus,
        MergePolicyEvaluationStatus? policyEvaluationStatus,
        int nextBuildToProcess = 0,
        bool flowerWillHaveConflict = false,
        string? headBranchSha = null,
        bool willFlowNewBuild = false,
        bool mockMergePolicyEvaluator = true,
        bool? sourceRepoNotified = null)
    {
        var prUrl = Subscription.TargetDirectory != null
            ? VmrPullRequestUrl
            : InProgressPrUrl;

        var targetRepo = Subscription.TargetDirectory != null
            ? VmrUri
            : TargetRepo;

        AfterDbUpdateActions.Add(() =>
        {
            var pr = CreatePullRequestState(
                forBuild,
                prUrl,
                nextBuildToProcess,
                headBranchSha: InProgressPrHeadBranchSha,
                sourceRepoNotified: sourceRepoNotified);
            SetState(Subscription, pr);
            SetExpectedPullRequestState(Subscription, pr);
        });

        headBranchSha ??= flowerWillHaveConflict
            ? ConflictPRRemoteSha
            : InProgressPrHeadBranchSha;

        _gitClient
            .Setup(x => x.GetShaForRefAsync(It.IsAny<string>(), InProgressPrHeadBranch))
            .ReturnsAsync(headBranchSha);

        var remote = DarcRemotes.GetOrAddValue(targetRepo, () => CreateMock<IRemote>());
        remote
            .Setup(x => x.GetPullRequestAsync(prUrl))
            .ReturnsAsync(new PullRequest()
            {
                Status = prStatus,
                HeadBranch = InProgressPrHeadBranch,
                HeadBranchSha = headBranchSha,
                BaseBranch = TargetBranch,
            });

        if (willFlowNewBuild && !string.IsNullOrEmpty(Subscription.TargetDirectory))
        {
            // Source manifest is used only for forward flow PRs
            remote
                .Setup(x => x.GetSourceManifestAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((SourceManifest?)null);
        }

        if (flowerWillHaveConflict)
        {
            WithForwardFlowConflict(remote, [new UnixPath("src/conflict.txt")]);
        }

        if (mockMergePolicyEvaluator)
        {
            var results = policyEvaluationStatus.HasValue
                ? new MergePolicyEvaluationResults(
                [
                    new MergePolicyEvaluationResult(
                    policyEvaluationStatus.Value,
                    "Check",
                    "Fake one",
                    "Policy",
                    "Some policy")
                ],
                string.Empty)
                : new MergePolicyEvaluationResults([], string.Empty);
            MergePolicyEvaluator
                .Setup(x => x.EvaluateAsync(
                    It.Is<PullRequestUpdateSummary>(pr => pr.Url == prUrl),
                    It.IsAny<IRemote>(),
                    It.IsAny<IReadOnlyList<MergePolicyDefinition>>(),
                    It.IsAny<MergePolicyEvaluationResults?>(),
                    It.IsAny<string>()))
                .ReturnsAsync(results.Results);

            if (prStatus == PrStatus.Open)
            {
                remote
                    .Setup(r => r.CreateOrUpdatePullRequestMergeStatusInfoAsync(prUrl, results.Results.ToImmutableList()))
                    .Returns(Task.CompletedTask);
            }
        }
        else
        {
            remote
                .Setup(r => r.CreateOrUpdatePullRequestMergeStatusInfoAsync(prUrl, It.IsAny<IReadOnlyCollection<MergePolicyEvaluationResult>>()))
                .Returns(Task.CompletedTask);
        }

        return Disposable.Create(remote.VerifyAll);
    }

    protected void WithForwardFlowConflict(Mock<IRemote> remote, IReadOnlyCollection<UnixPath> conflictedFiles)
    {
        remote
            .Setup(x => x.CommentPullRequestAsync(
                It.Is<string>(uri => uri.StartsWith(Subscription.TargetDirectory != null ? VmrUri + "/pulls/" : InProgressPrUrl)),
                It.Is<string>(content => content.Contains("need to be manually resolved"))))
            .Returns(Task.CompletedTask);

        // We re-evaulate checks after we push changes
        remote
            .Setup(r => r.CreateOrUpdatePullRequestMergeStatusInfoAsync(
                It.Is<string>(uri => uri.StartsWith(Subscription.TargetDirectory != null ? VmrUri + "/pulls/" : InProgressPrUrl)),
                It.IsAny<IReadOnlyCollection<MergePolicyEvaluationResult>>()))
            .Returns(Task.CompletedTask);

        var setup = _forwardFlower.Setup(x => x.FlowForwardAsync(
            It.IsAny<Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription>(),
            It.IsAny<Microsoft.DotNet.ProductConstructionService.Client.Models.Build>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()));

        setup.ReturnsAsync(new CodeFlowResult(true, conflictedFiles, new NativePath(VmrPath), []));
    }

    protected void AndShouldHavePullRequestCheckReminder(string? url = null)
    {
        var prUrl = string.IsNullOrEmpty(url)
            ? Subscription.SourceEnabled
                ? VmrPullRequestUrl
                : InProgressPrUrl
            : url;

        SetExpectedReminder(Subscription, new PullRequestCheck()
        {
            UpdaterId = GetPullRequestUpdaterId().ToString(),
            Url = prUrl,
            IsCodeFlow = Subscription.SourceEnabled
        });
    }

    protected void AndShouldNotHavePullRequestCheckReminder()
    {
        RemoveExpectedReminder<PullRequestCheck>(Subscription);
    }

    protected void AndShouldHaveInProgressPullRequestState(
        Build forBuild,
        int nextBuildToProcess = 0,
        bool? coherencyCheckSuccessful = true,
        List<CoherencyErrorDetails>? coherencyErrors = null,
        InProgressPullRequest? expectedState = null,
        string? headBranchSha = null,
        Func<Asset, bool>? assetFilter = null,
        bool? sourceRepoNotified = null,
        UnixPath? relativeBasePath = null,
        string? url = null,
        List<DependencyUpdateSummary>? dependencyUpdates = null)
    {
        var prUrl = string.IsNullOrEmpty(url)
            ? Subscription.SourceEnabled
                ? VmrPullRequestUrl
                : InProgressPrUrl
            : url;

        SetExpectedPullRequestState(
            Subscription,
            expectedState
                ?? CreatePullRequestState(
                    forBuild,
                    prUrl,
                    nextBuildToProcess,
                    coherencyCheckSuccessful,
                    coherencyErrors,
                    headBranchSha,
                    assetFilter,
                    sourceRepoNotified: sourceRepoNotified,
                    relativeBasePath,
                    dependencyUpdates: dependencyUpdates));
    }

    protected void ThenShouldHaveInProgressPullRequestState(Build forBuild, int nextBuildToProcess = 0, InProgressPullRequest? expectedState = null, bool? sourceRepoNotified = null, UnixPath? relativeBasePath = null)
        => AndShouldHaveInProgressPullRequestState(forBuild, nextBuildToProcess, expectedState: expectedState, sourceRepoNotified: sourceRepoNotified, relativeBasePath: relativeBasePath);

    protected void ThenShouldHaveCachedMergePolicyResults(MergePolicyEvaluationResults results)
    {
        SetExpectedEvaluationResultsState(Subscription, results);
    }
    protected void AndShouldHaveNoPendingUpdateState()
    {
        RemoveExpectedReminder<SubscriptionUpdateWorkItem>(Subscription);
    }

    protected virtual void ThenShouldHavePendingUpdateState(Build forBuild, bool isCodeFlow = false)
    {
        SetExpectedReminder(Subscription, CreateSubscriptionUpdate(forBuild, isCodeFlow));
    }

    protected void AndPendingUpdateIsRemoved()
    {
        RemoveExpectedReminder<SubscriptionUpdateWorkItem>(Subscription);
    }

    protected void ThenUpdateReminderIsRemoved()
    {
        RemoveExpectedReminder<SubscriptionUpdateWorkItem>(Subscription);
    }

    protected IPullRequestUpdater CreatePullRequestActor(IServiceProvider context)
    {
        var updaterFactory = context.GetRequiredService<IPullRequestUpdaterFactory>();
        return updaterFactory.CreatePullRequestUpdater(GetPullRequestUpdaterId());
    }

    protected SubscriptionUpdateWorkItem CreateSubscriptionUpdate(Build forBuild, bool isCodeFlow = false)
        => new()
        {
            UpdaterId = GetPullRequestUpdaterId().ToString(),
            SubscriptionId = Subscription.Id,
            SubscriptionType = isCodeFlow ? SubscriptionType.DependenciesAndSources : SubscriptionType.Dependencies,
            BuildId = forBuild.Id,
            SourceSha = forBuild.Commit,
            SourceRepo = forBuild.GitHubRepository ?? forBuild.AzureDevOpsRepository,
            IsCoherencyUpdate = false,
        };

    protected InProgressPullRequest CreatePullRequestState(
            Build forBuild,
            string prUrl,
            int nextBuildToProcess = 0,
            bool? coherencyCheckSuccessful = true,
            List<CoherencyErrorDetails>? coherencyErrors = null,
            string? headBranchSha = null,
            Func<Asset, bool>? assetFilter = null,
            bool? sourceRepoNotified = null,
            UnixPath? relativeBasePath = null,
            List<DependencyUpdateSummary>? dependencyUpdates = null)
        => new()
        {
            UpdaterId = GetPullRequestUpdaterId().ToString(),
            HeadBranch = InProgressPrHeadBranch,
            HeadBranchSha = headBranchSha ?? InProgressPrHeadBranchSha,
            SourceSha = forBuild.Commit,
            ContainedSubscriptions =
            [
                new SubscriptionPullRequestUpdate
                {
                    BuildId = forBuild.Id,
                    SubscriptionId = Subscription.Id,
                    SourceRepo = forBuild.GetRepository(),
                    CommitSha = forBuild.Commit
                }
            ],
            RequiredUpdates = dependencyUpdates
                ?? forBuild.Assets
                    .Where(assetFilter ?? (_ => true))
                    .Select(d => new DependencyUpdateSummary
                    {
                        DependencyName = d.Name,
                        FromVersion = d.Version,
                        ToVersion = d.Version,
                        FromCommitSha = NewCommit,
                        ToCommitSha = "sha3333",
                        RelativeBasePath = relativeBasePath
                    })
                    .ToList(),
            CoherencyCheckSuccessful = coherencyCheckSuccessful,
            CoherencyErrors = coherencyErrors,
            Url = prUrl,
            SourceRepoNotified = sourceRepoNotified,
            NextBuildsToProcess = nextBuildToProcess != 0 ?
                new Dictionary<Guid, int>
                {
                    [Subscription.Id] = nextBuildToProcess
                } :
                []
        };

    protected void ThenGetRequiredUpdatesForMultipleDirectoriesShouldHaveBeenCalled(Build withBuild, bool prExists, params UnixPath[] expectedDirectories)
    {
        var assets = new List<IReadOnlyCollection<AssetData>>();
        var dependencies = new List<IReadOnlyCollection<DependencyDetail>>();

        // Verify the coherency update resolver is called once per directory
        UpdateResolver
            .Verify(r => r.GetRequiredNonCoherencyUpdates(SourceRepo, NewCommit, Capture.In(assets), Capture.In(dependencies)), 
                Times.Exactly(expectedDirectories.Length));

        // Verify GetDependenciesAsync is called once for each target directory
        foreach (var directory in expectedDirectories)
        {
            DarcRemotes[TargetRepo]
                .Verify(r => r.GetDependenciesAsync(TargetRepo, prExists ? InProgressPrHeadBranch : TargetBranch, null, directory), 
                    Times.Once);
        }

        UpdateResolver
            .Verify(r => r.GetRequiredCoherencyUpdatesAsync(Capture.In(dependencies)), 
                Times.Exactly(expectedDirectories.Length));

        // Verify that assets were processed for each directory
        assets.Count.Should().Be(expectedDirectories.Length);
        foreach (var assetCollection in assets)
        {
            assetCollection.Should()
                .BeEquivalentTo(
                    withBuild.Assets
                        .Select(a => new AssetData(false)
                        {
                            Name = a.Name,
                            Version = a.Version
                        })
                        .ToList());
        }
    }

    protected void AndCommitUpdatesForMultipleDirectoriesShouldHaveBeenCalled(Build withUpdatesFromBuild, int expectedDirectoryCount, Func<Asset, bool>? assetFilter = null)
    {
        var updatedDependencies = new List<List<DependencyDetail>>();
        DarcRemotes[TargetRepo]
            .Verify(
                r => r.GetUpdatedDependencyFiles(
                    TargetRepo,
                    InProgressPrHeadBranch,
                    Capture.In(updatedDependencies),
                    It.IsAny<UnixPath>()),
                Times.Exactly(expectedDirectoryCount));

        // Each directory should have processed the same assets
        var expectedDependencyList = withUpdatesFromBuild.Assets
            .Where(assetFilter ?? (_ => true))
            .Select(a => new DependencyDetail
            {
                Name = a.Name,
                Version = a.Version,
                RepoUri = withUpdatesFromBuild.GitHubRepository,
                Commit = "sha3333"
            })
            .ToList();

        updatedDependencies.Should().HaveCount(expectedDirectoryCount);
        foreach (var dependencies in updatedDependencies)
        {
            dependencies.Should().BeEquivalentTo(expectedDependencyList);
        }
    }
}
