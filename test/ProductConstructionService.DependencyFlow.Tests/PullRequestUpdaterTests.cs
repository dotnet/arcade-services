// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using FluentAssertions;
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
using ProductConstructionService.DependencyFlow.WorkItems;
using AssetData = Microsoft.DotNet.ProductConstructionService.Client.Models.AssetData;

namespace ProductConstructionService.DependencyFlow.Tests;

internal abstract class PullRequestUpdaterTests : SubscriptionOrPullRequestUpdaterTests
{
    private const long InstallationId = 1174;
    protected const string InProgressPrUrl = "https://github.com/owner/repo/pull/10";
    protected string? InProgressPrHeadBranch { get; private set; } = "pr.head.branch";
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

        CodeFlowResult codeFlowRes = new CodeFlowResult(true, [], new NativePath(VmrPath), []);
        _forwardFlower.SetReturnsDefault(Task.FromResult(codeFlowRes));
        _backFlower.SetReturnsDefault(Task.FromResult(codeFlowRes));
        _gitClient.SetReturnsDefault(Task.CompletedTask);

        services.AddGitHubTokenProvider();
        services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        services.AddScoped<IBasicBarClient, SqlBarClient>();
        services.AddTransient<IPullRequestBuilder, PullRequestBuilder>();
        services.AddSingleVmrSupport("git", VmrPath, TmpPath, null, null);
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

        UpdateResolver
            .Verify(r => r.GetRequiredNonCoherencyUpdates(SourceRepo, NewCommit, Capture.In(assets), Capture.In(dependencies)));

        DarcRemotes[TargetRepo]
            .Verify(r => r.GetDependenciesAsync(TargetRepo, prExists ? InProgressPrHeadBranch : TargetBranch, null));

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
                r => r.CommitUpdatesAsync(
                    TargetRepo,
                    InProgressPrHeadBranch,
                    Capture.In(updatedDependencies),
                    It.IsAny<string>()));

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
                        HeadBranch = InProgressPrHeadBranch
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
            g => g.Push(VmrPath, It.IsAny<string>(), VmrUri, It.IsAny<LibGit2Sharp.Identity>()),
            Times.Once);
    }

    protected void AndCodeShouldHaveBeenFlownForward(Build build)
        => ThenCodeShouldHaveBeenFlownForward(build);

    protected static void ValidatePRDescriptionContainsLinks(PullRequest pr)
    {
        pr.Description.Should().Contain("][1]");
        pr.Description.Should().Contain("[1]:");
    }

    protected void CreatePullRequestShouldReturnAValidValue()
    {
        var targetRepo = Subscription.TargetDirectory != null ? VmrUri : TargetRepo;
        var prUrl = Subscription.TargetDirectory != null ? VmrPullRequestUrl : InProgressPrUrl;

        DarcRemotes.GetOrAddValue(targetRepo, () => new Mock<IRemote>())
            .Setup(s => s.CreatePullRequestAsync(It.IsAny<string>(), It.IsAny<PullRequest>()))
            .Callback<string, PullRequest>((repo, pr) =>
            {
                if (targetRepo == VmrUri)
                {
                    InProgressPrHeadBranch = pr.HeadBranch;
                }
            })
            .ReturnsAsync(prUrl);
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
        bool prAlreadyHasConflict = false,
        string latestCommitToReturn = ConflictPRRemoteSha,
        bool willFlowNewBuild = false,
        bool mockMergePolicyEvaluator = true)
        => WithExistingCodeFlowPullRequest(
                forBuild,
                PrStatus.Open,
                canUpdate ? null : MergePolicyEvaluationStatus.Pending,
                nextBuildToProcess,
                newChangeWillConflict,
                prAlreadyHasConflict,
                latestCommitToReturn,
                willFlowNewBuild: willFlowNewBuild,
                mockMergePolicyEvaluator: mockMergePolicyEvaluator);

    protected IDisposable WithExistingCodeFlowPullRequest(
        Build forBuild,
        PrStatus prStatus,
        MergePolicyEvaluationStatus? policyEvaluationStatus,
        int nextBuildToProcess = 0,
        bool flowerWillHaveConflict = false,
        bool prAlreadyHasConflict = false,
        string latestCommitToReturn = ConflictPRRemoteSha,
        bool willFlowNewBuild = false,
        bool mockMergePolicyEvaluator = true)
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
                overwriteBuildCommit:
                    prAlreadyHasConflict
                        ? ConflictPRRemoteSha
                        : forBuild.Commit,
                prState:
                    prAlreadyHasConflict
                        ? InProgressPullRequestState.Conflict
                        : InProgressPullRequestState.Mergeable);
            SetState(Subscription, pr);
            SetExpectedPullRequestState(Subscription, pr);
        });

        var remote = DarcRemotes.GetOrAddValue(targetRepo, () => CreateMock<IRemote>());
        remote
            .Setup(x => x.GetPullRequestAsync(prUrl))
            .ReturnsAsync(new PullRequest()
            {
                Status = prStatus,
                HeadBranch = InProgressPrHeadBranch,
                BaseBranch = TargetBranch,
                HeadBranchCommitSha = "sha123456",
            });

        if (willFlowNewBuild
            && !string.IsNullOrEmpty(Subscription.TargetDirectory))
        {
            //Source manifest is used only for forward flow PRs
            remote
            .Setup(x => x.GetSourceManifestAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((SourceManifest?)null);
        }

        if (flowerWillHaveConflict)
        {
            remote
                .Setup(x => x.CommentPullRequestAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            ProcessExecutionResult gitMergeResult = new();
            _forwardFlower.Setup(x => x.FlowForwardAsync(
                    It.IsAny<Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription>(),
                    It.IsAny<Microsoft.DotNet.ProductConstructionService.Client.Models.Build>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Throws(() => new ConflictInPrBranchException(
                    "error: patch failed: eng/common/build.ps1",
                    "branch",
                    "repo",
                    isForwardFlow: true));

        }

        if (flowerWillHaveConflict || prAlreadyHasConflict)
        {
            remote
                .Setup(x => x.GetLatestCommitAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(latestCommitToReturn);
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
                    It.IsAny<string>()
                    ))
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

    protected void AndShouldHavePullRequestCheckReminder()
    {
        var prUrl = Subscription.SourceEnabled
            ? VmrPullRequestUrl
            : InProgressPrUrl;

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
        string? overwriteBuildCommit = null,
        InProgressPullRequestState prState = InProgressPullRequestState.Mergeable,
        Func<Asset, bool>? assetFilter = null)
    {
        var prUrl = Subscription.SourceEnabled
            ? VmrPullRequestUrl
            : InProgressPrUrl;

        SetExpectedPullRequestState(
            Subscription,
            expectedState
                ?? CreatePullRequestState(
                    forBuild,
                    prUrl,
                    nextBuildToProcess,
                    coherencyCheckSuccessful,
                    coherencyErrors,
                    overwriteBuildCommit,
                    prState,
                    assetFilter));
    }

    protected void ThenShouldHaveInProgressPullRequestState(Build forBuild, int nextBuildToProcess = 0, InProgressPullRequest? expectedState = null)
        => AndShouldHaveInProgressPullRequestState(forBuild, nextBuildToProcess, expectedState: expectedState);

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
            string? overwriteBuildCommit = null,
            InProgressPullRequestState prState = InProgressPullRequestState.Mergeable,
            Func<Asset, bool>? assetFilter = null)
        => new()
        {
            UpdaterId = GetPullRequestUpdaterId().ToString(),
            HeadBranch = InProgressPrHeadBranch,
            SourceSha = overwriteBuildCommit ?? forBuild.Commit,
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
            RequiredUpdates = forBuild.Assets
                .Where(assetFilter ?? (_ => true))
                .Select(d => new DependencyUpdateSummary
                {
                    DependencyName = d.Name,
                    FromVersion = d.Version,
                    ToVersion = d.Version,
                    FromCommitSha = NewCommit,
                    ToCommitSha = "sha3333"
                })
                .ToList(),
            CoherencyCheckSuccessful = coherencyCheckSuccessful,
            CoherencyErrors = coherencyErrors,
            Url = prUrl,
            MergeState = prState,
            NextBuildsToProcess = nextBuildToProcess != 0 ?
                new Dictionary<Guid, int>
                {
                    [Subscription.Id] = nextBuildToProcess
                } :
                []
        };
}
