// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FluentAssertions;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
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
using SubscriptionActorService.StateModel;

using Asset = Maestro.Contracts.Asset;
using AssetData = Microsoft.DotNet.Maestro.Client.Models.AssetData;
using SynchronizePullRequestAction = System.Linq.Expressions.Expression<System.Func<System.Threading.Tasks.Task<SubscriptionActorService.ActionResult<SubscriptionActorService.StateModel.SynchronizePullRequestResult>>>>;

namespace SubscriptionActorService.Tests;

[TestFixture]
internal abstract class PullRequestActorTests : SubscriptionOrPullRequestActorTests
{
    private const long InstallationId = 1174;
    private const string InProgressPrUrl = "https://github.com/owner/repo/pull/10";
    private const string InProgressPrHeadBranch = "pr.head.branch";
    private const string PrUrl = "https://git.com/pr/123";

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
        _remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        _updateResolver = new Mock<ICoherencyUpdateResolver>(MockBehavior.Strict);
    }

    protected override void RegisterServices(IServiceCollection services)
    {
        var proxyFactory = new Mock<IActorProxyFactory<ISubscriptionActor>>();
        proxyFactory.Setup(l => l.Lookup(It.IsAny<ActorId>()))
            .Returns((ActorId actorId) =>
            {
                Mock<ISubscriptionActor> mock = _subscriptionActors.GetOrAddValue(
                    actorId,
                    CreateMock<ISubscriptionActor>);
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

        _remoteFactory.Setup(f => f.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>()))
            .ReturnsAsync(
                (string repo, ILogger logger) =>
                    _darcRemotes.GetOrAddValue(repo, CreateMock<IRemote>).Object);
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

    protected void ThenGetRequiredUpdatesShouldHaveBeenCalled(Build withBuild)
    {
        var assets = new List<IEnumerable<AssetData>>();
        var dependencies = new List<IEnumerable<DependencyDetail>>();
        _updateResolver
            .Verify(r => r.GetRequiredNonCoherencyUpdates(SourceRepo, NewCommit, Capture.In(assets), Capture.In(dependencies)));
        _darcRemotes[TargetRepo]
            .Verify(r => r.GetDependenciesAsync(TargetRepo, TargetBranch, null));
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

    private static void ValidatePRDescriptionContainsLinks(PullRequest pr)
    {
        pr.Description.Should().Contain("][1]");
        pr.Description.Should().Contain("[1]:");
    }

    protected void CreatePullRequestShouldReturnAValidValue()
    {
        _darcRemotes[TargetRepo]
            .Setup(s => s.CreatePullRequestAsync(It.IsAny<string>(), It.IsAny<PullRequest>()))
            .ReturnsAsync(() => PrUrl);
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
            
        ActionRunner.Setup(r => r.ExecuteAction(It.IsAny<Expression<Func<Task<ActionResult<SynchronizePullRequestResult>>>>>()))
            .ReturnsAsync(checkResult);

        if (checkResult == SynchronizePullRequestResult.InProgressCanUpdate)
        {
            _darcRemotes.GetOrAddValue(TargetRepo, CreateMock<IRemote>)
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

    protected void ThenShouldHavePullRequestUpdateReminder()
    {
        ExpectedReminders.Add(
            PullRequestActorImplementation.PullRequestUpdateKey,
            new MockReminderManager.Reminder(
                PullRequestActorImplementation.PullRequestUpdateKey,
                [],
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5)));
    }

    protected void AndShouldHaveInProgressPullRequestState(Build forBuild, bool coherencyCheckSuccessful = true, List<CoherencyErrorDetails>? coherencyErrors = null)
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

    protected void AndShouldHavePendingUpdateState(Build forBuild)
    {
        ExpectedActorState.Add(
            PullRequestActorImplementation.PullRequestUpdateKey,
            new List<UpdateAssetsParameters>
            {
                new()
                {
                    SubscriptionId = Subscription.Id,
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
                    IsCoherencyUpdate = false
                }
            });
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
