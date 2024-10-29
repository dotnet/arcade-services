// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.WorkItems;
using Asset = Maestro.Contracts.Asset;
using AssetData = Microsoft.DotNet.Maestro.Client.Models.AssetData;

namespace ProductConstructionService.DependencyFlow.Tests;

internal abstract class PullRequestUpdaterTests : SubscriptionOrPullRequestUpdaterTests
{
    private const long InstallationId = 1174;
    protected const string InProgressPrUrl = "https://github.com/owner/repo/pull/10";
    protected string? InProgressPrHeadBranch { get; private set; } = "pr.head.branch";

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

        _forwardFlower.SetReturnsDefault(Task.FromResult(true));
        _backFlower.SetReturnsDefault(Task.FromResult((true, new NativePath(TargetRepo))));
        _gitClient.SetReturnsDefault(Task.CompletedTask);

        services.AddGitHubTokenProvider();
        services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        services.AddScoped<IBasicBarClient, SqlBarClient>();
        services.AddTransient<IPullRequestBuilder, PullRequestBuilder>();
        services.AddVmrManagers("git", VmrPath, TmpPath, null, null);
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

    protected void ThenGetRequiredUpdatesShouldHaveBeenCalled(Build withBuild, bool prExists)
    {
        var assets = new List<IEnumerable<AssetData>>();
        var dependencies = new List<IEnumerable<DependencyDetail>>();

        UpdateResolver
            .Verify(r => r.GetRequiredNonCoherencyUpdates(SourceRepo, NewCommit, Capture.In(assets), Capture.In(dependencies)));

        DarcRemotes[TargetRepo]
            .Verify(r => r.GetDependenciesAsync(TargetRepo, prExists ? InProgressPrHeadBranch : TargetBranch, null));

        UpdateResolver
            .Verify(r => r.GetRequiredCoherencyUpdatesAsync(Capture.In(dependencies), RemoteFactory.Object));

        assets.Should()
            .BeEquivalentTo(
                new List<List<AssetData>>
                {
                    withBuild.Assets
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

    protected void AndCommitUpdatesShouldHaveBeenCalled(Build withUpdatesFromBuild)
    {
        var updatedDependencies = new List<List<DependencyDetail>>();
        DarcRemotes[TargetRepo]
            .Verify(
                r => r.CommitUpdatesAsync(
                    TargetRepo,
                    InProgressPrHeadBranch,
                    RemoteFactory.Object,
                    It.IsAny<IBasicBarClient>(),
                    Capture.In(updatedDependencies),
                    It.IsAny<string>()));

        updatedDependencies.Should()
            .BeEquivalentTo(
                new List<List<DependencyDetail>>
                {
                    withUpdatesFromBuild.Assets
                        .Select(a => new DependencyDetail
                        {
                            Name = a.Name,
                            Version = a.Version,
                            RepoUri = withUpdatesFromBuild.GitHubRepository,
                            Commit = "sha3"
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
                Subscription.SourceDirectory,
                It.Is<Microsoft.DotNet.Maestro.Client.Models.Build>(b => b.Id == build.Id && b.Commit == build.Commit),
                TargetBranch,
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
                Subscription.TargetDirectory,
                It.Is<Microsoft.DotNet.Maestro.Client.Models.Build>(b => b.Id == build.Id && b.Commit == build.Commit),
                TargetBranch,
                It.IsAny<string>(),
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
                It.IsAny<IEnumerable<AssetData>>(),
                It.IsAny<IEnumerable<DependencyDetail>>()))
            .Returns(
                (string sourceRepo, string sourceSha, IEnumerable<AssetData> assets, IEnumerable<DependencyDetail> dependencies) =>
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
                                Commit = "sha3"
                            },
                        })
                        .ToList());
    }

    protected void WithNoRequiredCoherencyUpdates()
    {
        UpdateResolver
            .Setup(r => r.GetRequiredCoherencyUpdatesAsync(
                It.IsAny<IEnumerable<DependencyDetail>>(),
                It.IsAny<IRemoteFactory>()))
            .ReturnsAsync([]);
    }

    protected void WithFailsStrictCheckForCoherencyUpdates()
    {
        UpdateResolver
            .Setup(r => r.GetRequiredCoherencyUpdatesAsync(
                It.IsAny<IEnumerable<DependencyDetail>>(),
                It.IsAny<IRemoteFactory>()))
            .ReturnsAsync(
                (IEnumerable<DependencyDetail> dependencies, IRemoteFactory factory) =>
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

    protected IDisposable WithExistingPullRequest(Build forBuild, bool canUpdate)
        => canUpdate
            ? WithExistingPullRequest(forBuild, PrStatus.Open, null)
            : WithExistingPullRequest(forBuild, PrStatus.Open, MergePolicyEvaluationStatus.Pending);

    protected IDisposable WithExistingPullRequest(Build forBuild, PrStatus prStatus, MergePolicyEvaluationStatus? policyEvaluationStatus)
    {
        var prUrl = Subscription.TargetDirectory != null
            ? VmrPullRequestUrl
            : InProgressPrUrl;

        AfterDbUpdateActions.Add(() =>
        {
            var pr = CreatePullRequestState(forBuild, prUrl);
            SetState(Subscription, pr);
            SetExpectedState(Subscription, pr);
        });

        var targetRepo = Subscription.TargetDirectory != null ? VmrUri : TargetRepo;

        var remote = DarcRemotes.GetOrAddValue(targetRepo, () => CreateMock<IRemote>());
        remote
            .Setup(x => x.GetPullRequestStatusAsync(prUrl))
            .ReturnsAsync(PrStatus.Open);

        var results = policyEvaluationStatus.HasValue
            ? new MergePolicyEvaluationResults(
            [
                new MergePolicyEvaluationResult(
                    policyEvaluationStatus.Value,
                    "Check",
                    "Fake one",
                    Mock.Of<IMergePolicyInfo>(x => x.Name == "Policy" && x.DisplayName == "Some policy"))
            ])
            : new MergePolicyEvaluationResults([]);

        if (prStatus == PrStatus.Open && !policyEvaluationStatus.HasValue)
        {
            remote
                .Setup(r => r.GetPullRequestAsync(prUrl))
                .ReturnsAsync(
                    new PullRequest
                    {
                        HeadBranch = InProgressPrHeadBranch,
                        BaseBranch = TargetBranch
                    });
        }

        remote
            .Setup(r => r.CreateOrUpdatePullRequestMergeStatusInfoAsync(prUrl, results.Results))
            .Returns(Task.CompletedTask);

        MergePolicyEvaluator
            .Setup(x => x.EvaluateAsync(
                It.Is<IPullRequest>(pr => pr.Url == prUrl),
                It.IsAny<IRemote>(),
                It.IsAny<IReadOnlyList<MergePolicyDefinition>>()))
            .ReturnsAsync(results);

        return Disposable.Create(remote.VerifyAll);
    }

    protected IDisposable WithExistingCodeFlowPullRequest(Build forBuild, bool canUpdate)
        => canUpdate
            ? WithExistingCodeFlowPullRequest(forBuild, PrStatus.Open, null)
            : WithExistingCodeFlowPullRequest(forBuild, PrStatus.Open, MergePolicyEvaluationStatus.Pending);

    protected IDisposable WithExistingCodeFlowPullRequest(Build forBuild, PrStatus prStatus, MergePolicyEvaluationStatus? policyEvaluationStatus)
    {
        var prUrl = Subscription.TargetDirectory != null
            ? VmrPullRequestUrl
            : InProgressPrUrl;

        var targetRepo = Subscription.TargetDirectory != null
            ? VmrUri
            : TargetRepo;

        AfterDbUpdateActions.Add(() =>
        {
            var pr = CreatePullRequestState(forBuild, prUrl);
            SetState(Subscription, pr);
            SetExpectedState(Subscription, pr);
        });

        var results = policyEvaluationStatus.HasValue
            ? new MergePolicyEvaluationResults(
            [
                new MergePolicyEvaluationResult(
                    policyEvaluationStatus.Value,
                    "Check",
                    "Fake one",
                    Mock.Of<IMergePolicyInfo>(x => x.Name == "Policy" && x.DisplayName == "Some policy"))
            ])
            : new MergePolicyEvaluationResults([]);

        MergePolicyEvaluator
            .Setup(x => x.EvaluateAsync(
                It.Is<IPullRequest>(pr => pr.Url == prUrl),
                It.IsAny<IRemote>(),
                It.IsAny<IReadOnlyList<MergePolicyDefinition>>()))
            .ReturnsAsync(results);

        var remote = DarcRemotes.GetOrAddValue(targetRepo, () => CreateMock<IRemote>());
        remote
            .Setup(x => x.GetPullRequestStatusAsync(prUrl))
            .ReturnsAsync(prStatus);

        if (prStatus == PrStatus.Open)
        {
            remote
                .Setup(x => x.CreateOrUpdatePullRequestMergeStatusInfoAsync(prUrl, results.Results))
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

    protected void AndShouldHaveInProgressPullRequestState(
        Build forBuild,
        bool? coherencyCheckSuccessful = true,
        List<CoherencyErrorDetails>? coherencyErrors = null,
        InProgressPullRequest? expectedState = null)
    {
        var prUrl = Subscription.SourceEnabled
            ? VmrPullRequestUrl
            : InProgressPrUrl;

        SetExpectedState(Subscription, expectedState ?? CreatePullRequestState(forBuild, prUrl, coherencyCheckSuccessful, coherencyErrors));
    }

    protected void ThenShouldHaveInProgressPullRequestState(Build forBuild, InProgressPullRequest? expectedState = null)
        => AndShouldHaveInProgressPullRequestState(forBuild, expectedState: expectedState);

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
            Assets = forBuild.Assets
                .Select(a => new Asset
                {
                    Name = a.Name,
                    Version = a.Version
                })
                .ToList(),
            IsCoherencyUpdate = false,
        };

    protected InProgressPullRequest CreatePullRequestState(
            Build forBuild,
            string prUrl,
            bool? coherencyCheckSuccessful = true,
            List<CoherencyErrorDetails>? coherencyErrors = null)
        => new()
        {
            UpdaterId = GetPullRequestUpdaterId().ToString(),
            HeadBranch = InProgressPrHeadBranch,
            SourceSha = forBuild.Commit,
            ContainedSubscriptions =
            [
                new SubscriptionPullRequestUpdate
                {
                    BuildId = forBuild.Id,
                    SubscriptionId = Subscription.Id
                }
            ],
            RequiredUpdates = forBuild.Assets
                .Select(d => new DependencyUpdateSummary
                {
                    DependencyName = d.Name,
                    FromVersion = d.Version,
                    ToVersion = d.Version
                })
                .ToList(),
            CoherencyCheckSuccessful = coherencyCheckSuccessful,
            CoherencyErrors = coherencyErrors,
            Url = prUrl,
        };
}
