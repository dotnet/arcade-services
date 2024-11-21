// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Actors;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using NUnit.Framework;
using ProductConstructionService.Client;
using SubscriptionActorService.StateModel;

using Asset = Maestro.Contracts.Asset;
using AssetData = Microsoft.DotNet.Maestro.Client.Models.AssetData;
using SynchronizePullRequestAction = System.Linq.Expressions.Expression<System.Func<System.Threading.Tasks.Task<SubscriptionActorService.ActionResult<SubscriptionActorService.StateModel.SynchronizePullRequestResult>>>>;

namespace SubscriptionActorService.Tests;

internal abstract class PullRequestActorTests : SubscriptionOrPullRequestActorTests
{
    private const long InstallationId = 1174;
    protected const string InProgressPrUrl = "https://github.com/owner/repo/pull/10";
    protected const string InProgressPrHeadBranch = "pr.head.branch";
    protected const string PrUrl = "https://git.com/pr/123";

    private Dictionary<string, Mock<IRemote>> _darcRemotes = null!;
    private Dictionary<ActorId, Mock<ISubscriptionActor>> _subscriptionActors = null!;

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
        _subscriptionActors = [];
        _mergePolicyEvaluator = CreateMock<IMergePolicyEvaluator>();
        _remoteFactory = new(MockBehavior.Strict);
        _updateResolver = new(MockBehavior.Strict);
    }

    protected override void RegisterServices(IServiceCollection services)
    {
        var proxyFactory = new Mock<IActorProxyFactory<ISubscriptionActor>>();
        proxyFactory.Setup(l => l.Lookup(It.IsAny<ActorId>()))
            .Returns((ActorId actorId) =>
            {
                Mock<ISubscriptionActor> mock = _subscriptionActors.GetOrAddValue(
                    actorId,
                    () => CreateMock<ISubscriptionActor>());
                return mock.Object;
            });

        services.AddSingleton(proxyFactory.Object);
        services.AddSingleton(_mergePolicyEvaluator.Object);
        services.AddGitHubTokenProvider();
        services.AddSingleton<ExponentialRetry>();
        services.AddSingleton(Mock.Of<IPullRequestPolicyFailureNotifier>());
        services.AddSingleton(Mock.Of<IKustoClientProvider>());
        services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        services.AddScoped<IBasicBarClient, SqlBarClient>();
        services.AddTransient<IPullRequestBuilder, PullRequestBuilder>();
        services.AddSingleton(_updateResolver.Object);
        services.AddSingleton(Mock.Of<IProductConstructionServiceApi>());

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
        var reminder = new MockReminderManager.Reminder(
            PullRequestActorImplementation.PullRequestCheckKey,
            null,
            TimeSpan.FromMinutes(3),
            TimeSpan.FromMinutes(3));
        Reminders.Data[PullRequestActorImplementation.PullRequestCheckKey] = reminder;
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
        _subscriptionActors[new ActorId(Subscription.Id)]
            .Verify(s => s.UpdateForMergedPullRequestAsync(withBuild.Id));
    }

    protected void AndDependencyFlowEventsShouldBeAdded()
    {
        _subscriptionActors[new ActorId(Subscription.Id)]
            .Verify(s => s.AddDependencyFlowEventAsync(
                It.IsAny<int>(),
                It.IsAny<DependencyFlowEventType>(),
                It.IsAny<DependencyFlowEventReason>(),
                It.IsAny<MergePolicyCheckResult>(),
                "PR",
                It.IsAny<string>()));
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

    protected IDisposable WithExistingPullRequest(SynchronizePullRequestResult checkResult)
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
            StateManager.SetStateAsync(PullRequestActorImplementation.PullRequestKey, pr);
            ExpectedActorState.Add(PullRequestActorImplementation.PullRequestKey, pr);
        });

        ActionRunner.Setup(r => r.ExecuteAction(It.IsAny<SynchronizePullRequestAction>()))
            .ReturnsAsync(checkResult);

        if (checkResult == SynchronizePullRequestResult.InProgressCanUpdate)
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
                ActionRunner.Verify(r => r.ExecuteAction(It.IsAny<SynchronizePullRequestAction>()));
                if (checkResult == SynchronizePullRequestResult.InProgressCanUpdate)
                {
                    _darcRemotes[TargetRepo].Verify(r => r.GetPullRequestAsync(InProgressPrUrl));
                }
            });
    }

    protected IDisposable WithExistingCodeFlowPullRequest(SynchronizePullRequestResult checkResult)
    {
        AfterDbUpdateActions.Add(() =>
        {
            var pr = new InProgressPullRequest
            {
                Url = InProgressPrUrl,
            };
            StateManager.SetStateAsync(PullRequestActorImplementation.PullRequestKey, pr);
            ExpectedActorState.Add(PullRequestActorImplementation.PullRequestKey, pr);
        });

        ActionRunner.Setup(r => r.ExecuteAction(It.IsAny<SynchronizePullRequestAction>()))
            .ReturnsAsync(checkResult);

        return Disposable.Create(
            () => ActionRunner.Verify(r => r.ExecuteAction(It.IsAny<SynchronizePullRequestAction>())));
    }

    protected void WithExistingCodeFlowStatus(Build build)
    {
        AfterDbUpdateActions.Add(() =>
        {
            var status = new CodeFlowStatus
            {
                PrBranch = InProgressPrHeadBranch,
                SourceSha = build.Commit,
            };
            StateManager.SetStateAsync(PullRequestActorImplementation.CodeFlowKey, status);
        });
    }

    protected void AndShouldHavePullRequestCheckReminder()
    {
        ExpectedReminders.Add(
            PullRequestActorImplementation.PullRequestCheckKey,
            new MockReminderManager.Reminder(
                PullRequestActorImplementation.PullRequestCheckKey,
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5)));
    }

    protected void AndShouldHaveInProgressPullRequestState(
        Build forBuild,
        bool coherencyCheckSuccessful = true,
        List<CoherencyErrorDetails>? coherencyErrors = null)
    {
        ExpectedActorState.Add(
            PullRequestActorImplementation.PullRequestKey,
            new InProgressPullRequest
            {
                ContainedSubscriptions =
                [
                    new SubscriptionPullRequestUpdate
                    {
                        BuildId = forBuild.Id,
                        SubscriptionId = Subscription.Id
                    }
                ],
                RequiredUpdates = forBuild.Assets.Select(
                        d => new DependencyUpdateSummary
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
        ExpectedActorState.Add(
            PullRequestActorImplementation.PullRequestKey,
            new InProgressPullRequest
            {
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
        ExpectedActorState.Add(
            PullRequestActorImplementation.CodeFlowKey,
            new CodeFlowStatus
            {
                SourceSha = forBuild.Commit,
                PrBranch = prBranch,
            });
    }

    protected void AndShouldHaveNoPendingUpdateState()
    {
        ExpectedActorState.Remove(PullRequestActorImplementation.PullRequestUpdateKey);
        ExpectedReminders.Remove(PullRequestActorImplementation.PullRequestUpdateKey);
    }

    protected virtual void ThenShouldHavePendingUpdateState(Build forBuild, bool isCodeFlow = false)
    {
        ExpectedActorState.Add(
            PullRequestActorImplementation.PullRequestUpdateKey,
            new List<UpdateAssetsParameters>
            {
                new()
                {
                    SubscriptionId = Subscription.Id,
                    Type = isCodeFlow ? SubscriptionType.DependenciesAndSources : SubscriptionType.Dependencies,
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
                }
            });

        ExpectedReminders.Add(
            PullRequestActorImplementation.PullRequestUpdateKey,
            new MockReminderManager.Reminder(
                PullRequestActorImplementation.PullRequestUpdateKey,
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5)));
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
            { PullRequestActorImplementation.PullRequestUpdateKey, (pullRequestUpdateState, pullRequestUpdateReminder) },
            { PullRequestActorImplementation.PullRequestCheckKey, (false /* no pr check state allowed */, pullRequestCheckReminder) },
            { PullRequestActorImplementation.PullRequestKey, (pullRequestState, false /* no codeflow reminders allowed */) },
            { PullRequestActorImplementation.CodeFlowKey, (codeFlowState, false /* no codeflow reminders allowed */) },
        };

        foreach (var (key, (hasState, hasReminder)) in keys)
        {
            if (hasState)
            {
                StateManager.Data.Keys.Should().Contain(key);
            }
            else
            {
                StateManager.Data.Keys.Should().NotContain(key);
            }

            if (hasReminder)
            {
                Reminders.Data.Keys.Should().Contain(key);
            }
            else
            {
                Reminders.Data.Keys.Should().NotContain(key);
            }
        }
    }

    protected void AndPendingUpdateIsRemoved()
    {
        ExpectedActorState.Remove(PullRequestActorImplementation.PullRequestUpdateKey);
    }

    protected void ThenUpdateReminderIsRemoved()
    {
        ExpectedReminders.Remove(PullRequestActorImplementation.PullRequestUpdateKey);
    }

    protected PullRequestActor CreateActor(IServiceProvider context)
    {
        ActorId actorId;
        if (Subscription.PolicyObject.Batchable)
        {
            actorId = PullRequestActorId.Create(Subscription.TargetRepository, Subscription.TargetBranch);
        }
        else
        {
            actorId = new ActorId(Subscription.Id);
        }

        var actor = ActivatorUtilities.CreateInstance<PullRequestActor>(context);
        actor.Initialize(actorId, StateManager, Reminders);
        return actor;
    }
}
