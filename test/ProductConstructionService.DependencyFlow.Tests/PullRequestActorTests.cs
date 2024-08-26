// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.WorkItems;
using Asset = Maestro.Contracts.Asset;
using AssetData = Microsoft.DotNet.Maestro.Client.Models.AssetData;

namespace ProductConstructionService.DependencyFlow.Tests;

internal abstract class PullRequestActorTests : SubscriptionOrPullRequestActorTests
{
    private const long InstallationId = 1174;
    protected const string InProgressPrUrl = "https://github.com/owner/repo/pull/10";
    protected const string InProgressPrHeadBranch = "pr.head.branch";
    protected const string PrUrl = "https://git.com/pr/123";

    private Dictionary<string, Mock<IRemote>> _darcRemotes = null!;

    private Mock<IRemoteFactory> _remoteFactory = null!;
    private Mock<ICoherencyUpdateResolver> _updateResolver = null!;
    private Mock<IMergePolicyEvaluator> _mergePolicyEvaluator = null!;

    private string _newBranch = null!;

    [SetUp]
    public void PullRequestActorTests_SetUp()
    {
        _darcRemotes = new()
        {
            [TargetRepo] = new Mock<IRemote>()
        };

        _mergePolicyEvaluator = CreateMock<IMergePolicyEvaluator>();
        _remoteFactory = new(MockBehavior.Strict);
        _updateResolver = new(MockBehavior.Strict);
    }

    protected override void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(_mergePolicyEvaluator.Object);
        services.AddGitHubTokenProvider();
        services.AddSingleton<ExponentialRetry>();
        services.AddSingleton(Mock.Of<IPullRequestPolicyFailureNotifier>());
        services.AddSingleton(Mock.Of<IKustoClientProvider>());
        services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        services.AddScoped<IBasicBarClient, SqlBarClient>();
        services.AddTransient<IPullRequestBuilder, PullRequestBuilder>();
        services.AddSingleton(_updateResolver.Object);

        _remoteFactory.Setup(f => f.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>()))
            .ReturnsAsync(
                (string repo, ILogger logger) =>
                    _darcRemotes.GetOrAddValue(repo, () => CreateMock<IRemote>()).Object);
        services.AddSingleton(_remoteFactory.Object);

        base.RegisterServices(services);
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
        return base.BeforeExecute(context);
    }

    protected void GivenAPullRequestCheckReminder()
    {
        SetReminder(Subscription, new InProgressPullRequest()
        {
            ActorId = GetPullRequestActorId(Subscription).ToString(),
            // TODO
        });
    }

    protected void ThenGetRequiredUpdatesShouldHaveBeenCalled(Build withBuild, bool prExists)
    {
        var assets = new List<IEnumerable<AssetData>>();
        var dependencies = new List<IEnumerable<DependencyDetail>>();
        _updateResolver
            .Verify(r => r.GetRequiredNonCoherencyUpdates(SourceRepo, NewCommit, Capture.In(assets), Capture.In(dependencies)));
        _darcRemotes[TargetRepo]
            .Verify(r => r.GetDependenciesAsync(TargetRepo, prExists ? InProgressPrHeadBranch : TargetBranch, null));
        _updateResolver
            .Verify(r => r.GetRequiredCoherencyUpdatesAsync(Capture.In(dependencies), _remoteFactory.Object));
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
        var captureNewBranch = new CaptureMatch<string>(newBranch => _newBranch = newBranch);
        _darcRemotes[TargetRepo]
            .Verify(r => r.CreateNewBranchAsync(TargetRepo, TargetBranch, Capture.With(captureNewBranch)));
    }

    protected void AndCommitUpdatesShouldHaveBeenCalled(Build withUpdatesFromBuild)
    {
        var updatedDependencies = new List<List<DependencyDetail>>();
        _darcRemotes[TargetRepo]
            .Verify(
                r => r.CommitUpdatesAsync(
                    TargetRepo,
                    _newBranch ?? InProgressPrHeadBranch,
                    _remoteFactory.Object,
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
        _darcRemotes[TargetRepo]
            .Verify(r => r.CreatePullRequestAsync(TargetRepo, Capture.In(pullRequests)));
        pullRequests.Should()
            .BeEquivalentTo(
                new List<PullRequest>
                {
                    new()
                    {
                        BaseBranch = TargetBranch,
                        HeadBranch = _newBranch
                    }
                },
                options => options.Excluding(pr => pr.Title).Excluding(pr => pr.Description));

        ValidatePRDescriptionContainsLinks(pullRequests[0]);
    }

    protected void AndCodeFlowPullRequestShouldHaveBeenCreated()
    {
        var pullRequests = new List<PullRequest>();
        _darcRemotes[TargetRepo]
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
                options => options
                    .Excluding(pr => pr.Title)
                    .Excluding(pr => pr.Description));
    }

    protected void ThenPcsShouldNotHaveBeenCalled(Build build, string? prUrl = null)
    {
        "TODO".Should().NotBe("TODO");
        //_pcsClientCodeFlow
        //    .Verify(
        //        r => r.FlowAsync(
        //            It.Is<CodeFlowRequest>(request => request.BuildId == build.Id && (prUrl == null || request.PrUrl == prUrl)),
        //            It.IsAny<CancellationToken>()),
        //        Times.Never);
    }

    protected void ThenPcsShouldHaveBeenCalled(Build build, string? prUrl, out string prBranch)
        => AndPcsShouldHaveBeenCalled(build, prUrl, out prBranch);

    protected void AndPcsShouldHaveBeenCalled(Build build, string? prUrl, out string prBranch)
    {
        prBranch = "TODO";
        "TODO".Should().NotBe("TODO");
        //var pcsRequests = new List<CodeFlowRequest>();
        //_pcsClientCodeFlow
        //    .Verify(r => r.FlowAsync(Capture.In(pcsRequests), It.IsAny<CancellationToken>()));

        //pcsRequests.Should()
        //    .BeEquivalentTo(
        //        new List<CodeFlowRequest>
        //        {
        //            new()
        //            {
        //                SubscriptionId = Subscription.Id,
        //                BuildId = build.Id,
        //                PrUrl = prUrl,
        //            }
        //        },
        //        options => options.Excluding(r => r.PrBranch));

        //prBranch = pcsRequests[0].PrBranch;
    }

    protected void ExpectPcsToGetCalled(Build build, string? prUrl = null)
    {
        "TODO".Should().NotBe("TODO");
        //_pcsClientCodeFlow
        //    .Setup(r => r.FlowAsync(
        //        It.Is<CodeFlowRequest>(r => r.BuildId == build.Id && r.SubscriptionId == Subscription.Id && r.PrUrl == prUrl),
        //        It.IsAny<CancellationToken>()))
        //    .Returns(Task.CompletedTask)
        //    .Verifiable();
    }

    protected static void ValidatePRDescriptionContainsLinks(PullRequest pr)
    {
        pr.Description.Should().Contain("][1]");
        pr.Description.Should().Contain("[1]:");
    }

    protected void CreatePullRequestShouldReturnAValidValue()
    {
        _darcRemotes[TargetRepo]
            .Setup(s => s.CreatePullRequestAsync(It.IsAny<string>(), It.IsAny<PullRequest>()))
            .ReturnsAsync(PrUrl);
    }

    protected void WithExistingPrBranch()
    {
        _darcRemotes[TargetRepo]
            .Setup(s => s.BranchExistsAsync(TargetRepo, It.IsAny<string>()))
            .ReturnsAsync(true);
    }

    protected void WithoutExistingPrBranch()
    {
        _darcRemotes[TargetRepo]
            .Setup(s => s.BranchExistsAsync(TargetRepo, It.IsAny<string>()))
            .ReturnsAsync(false);
    }

    protected void AndUpdatePullRequestShouldHaveBeenCalled()
    {
        var pullRequests = new List<PullRequest>();
        _darcRemotes[TargetRepo]
            .Verify(r => r.UpdatePullRequestAsync(InProgressPrUrl, Capture.In(pullRequests)));
        pullRequests.Should()
            .BeEquivalentTo(
                new List<PullRequest>
                {
                    new()
                    {
                        BaseBranch = TargetBranch,
                        HeadBranch = _newBranch ?? InProgressPrHeadBranch
                    }
                },
                options => options.Excluding(pr => pr.Title).Excluding(pr => pr.Description));
    }

    protected void AndSubscriptionShouldBeUpdatedForMergedPullRequest(Build withBuild)
    {
        // TODO
        //SubscriptionActors[Subscription.Id]
        //    .Verify(s => s.UpdateForMergedPullRequestAsync(withBuild.Id));
    }

    protected void WithRequireNonCoherencyUpdates()
    {
        _updateResolver
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
        _updateResolver
            .Setup(r => r.GetRequiredCoherencyUpdatesAsync(
                It.IsAny<IEnumerable<DependencyDetail>>(),
                It.IsAny<IRemoteFactory>()))
            .ReturnsAsync((IEnumerable<DependencyDetail> dependencies, IRemoteFactory factory) => []);
    }

    protected void WithFailsStrictCheckForCoherencyUpdates()
    {
        _updateResolver
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

    protected IDisposable WithExistingPullRequest(PullRequestStatus checkResult)
    {
        AfterDbUpdateActions.Add(() =>
        {
            var pr = new InProgressPullRequest
            {
                Url = InProgressPrUrl,
                CoherencyCheckSuccessful = true,
                ContainedSubscriptions =
                [
                    new SubscriptionPullRequestUpdate
                    {
                        BuildId = -1,
                        SubscriptionId = Subscription.Id
                    }
                ],
                RequiredUpdates =
                [
                    new DependencyUpdateSummary
                    {
                        DependencyName = "Ham",
                        FromVersion = "1.0.0-beta.1",
                        ToVersion = "1.0.1-beta.1"
                    },
                    new DependencyUpdateSummary
                    {
                        DependencyName = "Ham",
                        FromVersion = "1.0.0-beta.1",
                        ToVersion = "1.0.1-beta.1"
                    },
                ]
            };

            SetState(Subscription, pr);
        });

        // TODO: Call SynchronizePullRequest
        //ActionRunner.Setup(r => r.ExecuteAction(It.IsAny<SynchronizePullRequestAction>()))
        //    .ReturnsAsync(checkResult);

        if (checkResult == PullRequestStatus.InProgressCanUpdate)
        {
            _darcRemotes.GetOrAddValue(TargetRepo, () => CreateMock<IRemote>())
                .Setup(r => r.GetPullRequestAsync(InProgressPrUrl))
                .ReturnsAsync(
                    new PullRequest
                    {
                        HeadBranch = InProgressPrHeadBranch,
                        BaseBranch = TargetBranch
                    });
        }

        return Disposable.Create(
            () =>
            {
                if (checkResult == PullRequestStatus.InProgressCanUpdate)
                {
                    _darcRemotes[TargetRepo].Verify(r => r.GetPullRequestAsync(InProgressPrUrl));
                }
            });
    }

    protected IDisposable WithExistingCodeFlowPullRequest(PullRequestStatus checkResult)
    {
        AfterDbUpdateActions.Add(() =>
        {
            SetState(Subscription, new InProgressPullRequest
            {
                Url = InProgressPrUrl,
                // TODO - Check what this was doing before and replicate it
            });
        });

        return Disposable.Create(() => { });

        // TODO
        //ActionRunner.Setup(r => r.ExecuteAction(It.IsAny<SynchronizePullRequestAction>()))
        //    .ReturnsAsync(checkResult);

        //return Disposable.Create(
        //    () => ActionRunner.Verify(r => r.ExecuteAction(It.IsAny<SynchronizePullRequestAction>())));
    }

    protected void WithExistingCodeFlowStatus(Build build)
    {
        AfterDbUpdateActions.Add(() =>
        {
            SetState(Subscription, new CodeFlowStatus
            {
                PrBranch = InProgressPrHeadBranch,
                SourceSha = build.Commit,
            });
        });
    }

    protected void AndShouldHavePullRequestCheckReminder()
    {
        SetExpectedReminder(Subscription, new SubscriptionUpdateWorkItem()
        {
            ActorId = GetPullRequestActorId(Subscription).ToString(),
            SubscriptionId = Subscription.Id,
            IsCoherencyUpdate = false,
            // TODO - Check what this was doing before and replicate it
        });
    }

    protected void AndShouldHaveInProgressPullRequestState(
        Build forBuild,
        bool coherencyCheckSuccessful = true,
        List<CoherencyErrorDetails>? coherencyErrors = null)
    {
        SetExpectedState(Subscription, new InProgressPullRequest
        {
            ActorId = GetPullRequestActorId(Subscription).ToString(),
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
            Url = PrUrl
        });
    }

    protected void AndShouldHaveInProgressCodeFlowPullRequestState(Build forBuild)
    {
        SetExpectedState(Subscription, new InProgressPullRequest
        {
            ActorId = GetPullRequestActorId(Subscription).ToString(),
            Url = PrUrl,
            ContainedSubscriptions =
            [
                new SubscriptionPullRequestUpdate
                {
                    BuildId = forBuild.Id,
                    SubscriptionId = Subscription.Id
                }
            ]
        });
    }

    protected void AndShouldHaveCodeFlowState(Build forBuild, string? prBranch = null)
    {
        SetExpectedState(Subscription, new CodeFlowStatus
        {
            SourceSha = forBuild.Commit,
            PrBranch = prBranch,
        });
    }

    protected void AndShouldHaveNoPendingUpdateState()
    {
        // TODO: This? ExpectedActorState.Remove(PullRequestActorImplementation.PullRequestUpdateKey);
        RemoveExpectedReminder<SubscriptionUpdateWorkItem>(Subscription);
    }

    protected virtual void ThenShouldHavePendingUpdateState(Build forBuild, bool isCodeFlow = false)
    {
        SetExpectedReminder(Subscription, new SubscriptionUpdateWorkItem()
        {
            ActorId = GetPullRequestActorId(Subscription).ToString(),
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
        });
    }

    protected void AndShouldHaveFollowingState(
        bool pullRequestUpdateState = false,
        bool pullRequestUpdateReminder = false,
        bool pullRequestState = false,
        bool pullRequestCheckReminder = false,
        bool codeFlowState = false)
    {
        Dictionary<string, (bool HasState, bool HasReminder)> keys = new()
        {
            //{ PullRequestActorImplementation.PullRequestUpdateKey, (pullRequestUpdateState, pullRequestUpdateReminder) },
            //{ PullRequestActorImplementation.PullRequestCheckKey, (false /* no pr check state allowed */, pullRequestCheckReminder) },
            //{ PullRequestActorImplementation.PullRequestKey, (pullRequestState, false /* no codeflow reminders allowed */) },
            //{ PullRequestActorImplementation.CodeFlowKey, (codeFlowState, false /* no codeflow reminders allowed */) },
        };

        foreach (var (key, (hasState, hasReminder)) in keys)
        {
            if (hasState)
            {
                RedisCache.Data.Keys.Should().Contain(key);
            }
            else
            {
                RedisCache.Data.Keys.Should().NotContain(key);
            }

            if (hasReminder)
            {
                Reminders.Reminders.Keys.Should().Contain(key);
            }
            else
            {
                Reminders.Reminders.Keys.Should().NotContain(key);
            }
        }
    }

    protected void AndPendingUpdateIsRemoved()
    {
        RemoveExpectedReminder<SubscriptionUpdateWorkItem>(Subscription);
    }

    protected void ThenUpdateReminderIsRemoved()
    {
        RemoveExpectedReminder<SubscriptionUpdateWorkItem>(Subscription);
    }

    protected IPullRequestActor CreateActor(IServiceProvider context)
    {
        var actorFactory = ActivatorUtilities.CreateInstance<IActorFactory>(context);
        return actorFactory.CreatePullRequestActor(GetPullRequestActorId(Subscription));
    }
}
