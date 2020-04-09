// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FluentAssertions;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Actors;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using Xunit;
using Asset = Maestro.Contracts.Asset;
using AssetData = Microsoft.DotNet.Maestro.Client.Models.AssetData;

namespace SubscriptionActorService.Tests
{
    public class PullRequestActorTests : SubscriptionOrPullRequestActorTests
    {
        private const long InstallationId = 1174;
        private const string InProgressPrUrl = "https://github.com/owner/repo/pull/10";
        private const string InProgressPrHeadBranch = "pr.head.branch";
        private const string PrUrl = "https://git.com/pr/123";

        private readonly Dictionary<string, Mock<IRemote>> DarcRemotes =
            new Dictionary<string, Mock<IRemote>>();

        private readonly Mock<IRemoteFactory> RemoteFactory;

        private readonly Mock<IMergePolicyEvaluator> MergePolicyEvaluator;

        private readonly Dictionary<ActorId, Mock<ISubscriptionActor>> SubscriptionActors =
            new Dictionary<ActorId, Mock<ISubscriptionActor>>();

        private string NewBranch;

        public PullRequestActorTests()
        {
            var lookup = new Mock<IActorLookup<ISubscriptionActor>>();
            lookup.Setup(l => l.Lookup(It.IsAny<ActorId>()))
                .Returns((ActorId actorId) =>
                {
                    Mock<ISubscriptionActor> mock = SubscriptionActors.GetOrAddValue(
                        actorId,
                        CreateMock<ISubscriptionActor>);
                    return mock.Object;
                });

            Builder.AddSingleton(lookup.Object);

            MergePolicyEvaluator = CreateMock<IMergePolicyEvaluator>();
            Builder.AddSingleton(MergePolicyEvaluator.Object);

            RemoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
            RemoteFactory.Setup(f => f.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>()))
                .ReturnsAsync(
                    (string repo, ILogger logger) =>
                        DarcRemotes.GetOrAddValue(repo, CreateMock<IRemote>).Object);
            Builder.AddSingleton(RemoteFactory.Object);
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

        private void ThenGetRequiredUpdatesShouldHaveBeenCalled(Build withBuild)
        {
            var assets = new List<IEnumerable<AssetData>>();
            var dependencies = new List<IEnumerable<DependencyDetail>>();
            DarcRemotes[TargetRepo]
                .Verify(r => r.GetRequiredNonCoherencyUpdatesAsync(SourceRepo, NewCommit, Capture.In(assets), Capture.In(dependencies)));
            DarcRemotes[TargetRepo]
                .Verify(r => r.GetDependenciesAsync(TargetRepo, TargetBranch, null, false));
            DarcRemotes[TargetRepo]
                .Verify(r => r.GetRequiredCoherencyUpdatesAsync(Capture.In(dependencies), RemoteFactory.Object));
            assets.Should()
                .BeEquivalentTo(
                    new List<List<AssetData>>
                    {
                        withBuild.Assets.Select(
                                a => new AssetData(false)
                                {
                                    Name = a.Name,
                                    Version = a.Version
                                })
                            .ToList()
                    });
        }

        private void AndCreateNewBranchShouldHaveBeenCalled()
        {
            var captureNewBranch = new CaptureMatch<string>(newBranch => NewBranch = newBranch);
            DarcRemotes[TargetRepo]
                .Verify(r => r.CreateNewBranchAsync(TargetRepo, TargetBranch, Capture.With(captureNewBranch)));
        }

        private void AndCommitUpdatesShouldHaveBeenCalled(Build withUpdatesFromBuild)
        {
            var updatedDependencies = new List<List<DependencyDetail>>();
            DarcRemotes[TargetRepo]
                .Verify(
                    r => r.CommitUpdatesAsync(
                        TargetRepo,
                        NewBranch ?? InProgressPrHeadBranch,
                        Capture.In(updatedDependencies),
                        It.IsAny<string>()));
            updatedDependencies.Should()
                .BeEquivalentTo(
                    new List<List<DependencyDetail>>
                    {
                        withUpdatesFromBuild.Assets.Select(
                                a => new DependencyDetail
                                {
                                    Name = a.Name,
                                    Version = a.Version,
                                })
                            .ToList()
                    });
        }

        private void AndCreatePullRequestShouldHaveBeenCalled()
        {
            var pullRequests = new List<PullRequest>();
            DarcRemotes[TargetRepo]
                .Verify(r => r.CreatePullRequestAsync(TargetRepo, Capture.In(pullRequests)));
            pullRequests.Should()
            .BeEquivalentTo(
                new List<PullRequest>
                {
                    new PullRequest
                    {
                        BaseBranch = TargetBranch,
                        HeadBranch = NewBranch
                    }
                },
                options => options.Excluding(pr => pr.Title).Excluding(pr => pr.Description));
        }

        private void CreatePullRequestShouldReturnAValidValue()
        {
            DarcRemotes[TargetRepo]
                .Setup(s => s.CreatePullRequestAsync(It.IsAny<string>(), It.IsAny<PullRequest>()))
                .ReturnsAsync(() => PrUrl);
        }

        private void AndUpdatePullRequestShouldHaveBeenCalled()
        {
            var pullRequests = new List<PullRequest>();
            DarcRemotes[TargetRepo]
                .Verify(r => r.UpdatePullRequestAsync(InProgressPrUrl, Capture.In(pullRequests)));
            pullRequests.Should()
            .BeEquivalentTo(
                new List<PullRequest>
                {
                    new PullRequest
                    {
                        BaseBranch = TargetBranch,
                        HeadBranch = NewBranch ?? InProgressPrHeadBranch
                    }
                },
                options => options.Excluding(pr => pr.Title).Excluding(pr => pr.Description));
        }

        private void AndSubscriptionShouldBeUpdatedForMergedPullRequest(Build withBuild)
        {
            SubscriptionActors[new ActorId(Subscription.Id)]
                .Verify(s => s.UpdateForMergedPullRequestAsync(withBuild.Id));
        }

        private void AndDependencyFlowEventsShouldBeAdded()
        {
            SubscriptionActors[new ActorId(Subscription.Id)]
                .Verify(s => s.AddDependencyFlowEventAsync(
                    It.IsAny<int>(), 
                    It.IsAny<DependencyFlowEventType>(), 
                    It.IsAny<DependencyFlowEventReason>(), 
                    It.IsAny<MergePolicyCheckResult>(), 
                    "PR",
                    It.IsAny<string>()));
        }

        private void WithRequireNonCoherencyUpdates(Build fromBuild)
        {
            DarcRemotes.GetOrAddValue(TargetRepo, CreateMock<IRemote>)
                .Setup(
                    r => r.GetRequiredNonCoherencyUpdatesAsync(
                        SourceRepo,
                        NewCommit,
                        It.IsAny<IEnumerable<AssetData>>(),
                        It.IsAny<IEnumerable<DependencyDetail>>()))
                .ReturnsAsync(
                    (string sourceRepo, string sourceSha, IEnumerable<AssetData> assets, IEnumerable<DependencyDetail> dependencies) =>
                    {
                        // Just make from->to identical.
                        return assets.Select(
                                d => new DependencyUpdate
                                {
                                    From = new DependencyDetail
                                    {
                                        Name = d.Name,
                                        Version = d.Version
                                    },
                                    To = new DependencyDetail
                                    {
                                        Name = d.Name,
                                        Version = d.Version
                                    },
                                })
                            .ToList();
                    });
        }

        private void WithNoRequiredCoherencyUpdates()
        {
            DarcRemotes.GetOrAddValue(TargetRepo, CreateMock<IRemote>)
                .Setup(
                    r => r.GetRequiredCoherencyUpdatesAsync(
                        It.IsAny<IEnumerable<DependencyDetail>>(),
                        It.IsAny<IRemoteFactory>()))
                .ReturnsAsync(
                    (IEnumerable<DependencyDetail> dependencies, IRemoteFactory factory) =>
                    {
                        return new List<DependencyUpdate>();
                    });
        }

        private IDisposable WithExistingPullRequest(SynchronizePullRequestResult checkResult)
        {
            AfterDbUpdateActions.Add(() =>
            {
                var pr = new InProgressPullRequest
                {
                    Url = InProgressPrUrl,
                    ContainedSubscriptions = new List<SubscriptionPullRequestUpdate>
                {
                    new SubscriptionPullRequestUpdate
                    {
                        BuildId = -1,
                        SubscriptionId = Subscription.Id
                    }
                }
                };
                StateManager.SetStateAsync(PullRequestActorImplementation.PullRequest, pr);
                ExpectedActorState.Add(PullRequestActorImplementation.PullRequest, pr);
            });
            
            ActionRunner.Setup(r => r.ExecuteAction(It.IsAny<Expression<Func<Task<ActionResult<SynchronizePullRequestResult>>>>>()))
                .ReturnsAsync(checkResult);

            if (checkResult == SynchronizePullRequestResult.InProgressCanUpdate)
            {
                DarcRemotes.GetOrAddValue(TargetRepo, CreateMock<IRemote>)
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
                    ActionRunner.Verify(r => r.ExecuteAction(It.IsAny<Expression<Func<Task<ActionResult<SynchronizePullRequestResult>>>>>()));
                    if (checkResult == SynchronizePullRequestResult.InProgressCanUpdate)
                    {
                        DarcRemotes[TargetRepo].Verify(r => r.GetPullRequestAsync(InProgressPrUrl));
                    }
                });
        }

        private void AndShouldHavePullRequestCheckReminder()
        {
            ExpectedReminders.Add(
                PullRequestActorImplementation.PullRequestCheck,
                new MockReminderManager.Reminder(
                    PullRequestActorImplementation.PullRequestCheck,
                    null,
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(5)));
        }

        private void ThenShouldHavePullRequestUpdateReminder()
        {
            ExpectedReminders.Add(
                PullRequestActorImplementation.PullRequestUpdate,
                new MockReminderManager.Reminder(
                    PullRequestActorImplementation.PullRequestUpdate,
                    Array.Empty<byte>(),
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(5)));
        }

        private void AndShouldHaveInProgressPullRequestState(Build forBuild)
        {
            ExpectedActorState.Add(
                PullRequestActorImplementation.PullRequest,
                new InProgressPullRequest
                {
                    ContainedSubscriptions = new List<SubscriptionPullRequestUpdate>
                    {
                        new SubscriptionPullRequestUpdate
                        {
                            BuildId = forBuild.Id,
                            SubscriptionId = Subscription.Id
                        }
                    },
                    RequiredUpdates = forBuild.Assets.Select(
                                d => new DependencyUpdateSummary
                                {
                                    DependencyName = d.Name,
                                    FromVersion = d.Version,
                                    ToVersion = d.Version
                                })
                    .ToList(),
                    Url = PrUrl
                });
        }

        private void AndShouldHavePendingUpdateState(Build forBuild)
        {
            ExpectedActorState.Add(
                PullRequestActorImplementation.PullRequestUpdate,
                new List<PullRequestActorImplementation.UpdateAssetsParameters>
                {
                    new PullRequestActorImplementation.UpdateAssetsParameters
                    {
                        SubscriptionId = Subscription.Id,
                        BuildId = forBuild.Id,
                        SourceSha = forBuild.Commit,
                        SourceRepo = forBuild.GitHubRepository ?? forBuild.AzureDevOpsRepository,
                        Assets = forBuild.Assets.Select(
                                a => new Asset
                                {
                                    Name = a.Name,
                                    Version = a.Version
                                })
                            .ToList(),
                        IsCoherencyUpdate = false
                    }
                });
        }

        private PullRequestActor CreateActor(IServiceProvider context)
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

            return ActivatorUtilities.CreateInstance<PullRequestActor>(context, actorId);
        }

        public class ProcessPendingUpdatesAsync : PullRequestActorTests
        {
            private async Task WhenProcessPendingUpdatesAsyncIsCalled()
            {
                await Execute(
                    async context =>
                    {
                        PullRequestActor actor = CreateActor(context);
                        await actor.Implementation.ProcessPendingUpdatesAsync();
                    });
            }

            private void GivenAPendingUpdateReminder()
            {
                var reminder = new MockReminderManager.Reminder(
                    PullRequestActorImplementation.PullRequestUpdate,
                    Array.Empty<byte>(),
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(5));
                Reminders.Data[PullRequestActorImplementation.PullRequestUpdate] = reminder;
                ExpectedReminders[PullRequestActorImplementation.PullRequestUpdate] = reminder;
            }

            private void AndNoPendingUpdates()
            {
                var updates = new List<PullRequestActorImplementation.UpdateAssetsParameters>();
                StateManager.Data[PullRequestActorImplementation.PullRequestUpdate] = updates;
                ExpectedActorState[PullRequestActorImplementation.PullRequestUpdate] = updates;
            }

            private void AndPendingUpdates(Build forBuild)
            {
                AfterDbUpdateActions.Add(
                    () =>
                    {
                        var updates = new List<PullRequestActorImplementation.UpdateAssetsParameters>
                        {
                            new PullRequestActorImplementation.UpdateAssetsParameters
                            {
                                SubscriptionId = Subscription.Id,
                                BuildId = forBuild.Id,
                                SourceRepo = forBuild.GitHubRepository ?? forBuild.AzureDevOpsRepository,
                                SourceSha = forBuild.Commit,
                                Assets = forBuild.Assets
                                    .Select(a => new Asset {Name = a.Name, Version = a.Version})
                                    .ToList(),
                                IsCoherencyUpdate = false
                            }
                        };
                        StateManager.Data[PullRequestActorImplementation.PullRequestUpdate] = updates;
                        ExpectedActorState[PullRequestActorImplementation.PullRequestUpdate] = updates;
                    });
            }

            private void ThenUpdateReminderIsRemoved()
            {
                ExpectedReminders.Remove(PullRequestActorImplementation.PullRequestUpdate);
            }

            private void AndPendingUpdateIsRemoved()
            {
                ExpectedActorState.Remove(PullRequestActorImplementation.PullRequestUpdate);
            }

            [Fact]
            public async Task NoPendingUpdates()
            {
                GivenATestChannel();
                GivenASubscription(
                    new SubscriptionPolicy
                    {
                        Batchable = true,
                        UpdateFrequency = UpdateFrequency.EveryBuild
                    });
                Build b = GivenANewBuild(true);

                GivenAPendingUpdateReminder();
                AndNoPendingUpdates();
                await WhenProcessPendingUpdatesAsyncIsCalled();
                ThenUpdateReminderIsRemoved();
            }

            [Fact]
            public async Task PendingUpdatesNotUpdatablePr()
            {
                GivenATestChannel();
                GivenASubscription(
                    new SubscriptionPolicy
                    {
                        Batchable = true,
                        UpdateFrequency = UpdateFrequency.EveryBuild
                    });
                Build b = GivenANewBuild(true);

                GivenAPendingUpdateReminder();
                AndPendingUpdates(b);
                using (WithExistingPullRequest(SynchronizePullRequestResult.InProgressCannotUpdate))
                {
                    await WhenProcessPendingUpdatesAsyncIsCalled();
                    // Nothing happens
                }
            }

            [Fact]
            public async Task PendingUpdatesUpdatablePr()
            {
                GivenATestChannel();
                GivenASubscription(
                    new SubscriptionPolicy
                    {
                        Batchable = true,
                        UpdateFrequency = UpdateFrequency.EveryBuild
                    });
                Build b = GivenANewBuild(true);

                GivenAPendingUpdateReminder();
                AndPendingUpdates(b);
                WithRequireNonCoherencyUpdates(b);
                WithNoRequiredCoherencyUpdates();
                using (WithExistingPullRequest(SynchronizePullRequestResult.InProgressCanUpdate))
                {
                    await WhenProcessPendingUpdatesAsyncIsCalled();
                    ThenUpdateReminderIsRemoved();
                    AndPendingUpdateIsRemoved();
                    ThenGetRequiredUpdatesShouldHaveBeenCalled(b);
                    AndCommitUpdatesShouldHaveBeenCalled(b);
                    AndUpdatePullRequestShouldHaveBeenCalled();
                    AndShouldHavePullRequestCheckReminder();
                    AndDependencyFlowEventsShouldBeAdded();
                }
            }
        }

        public class UpdateAssetsAsync : PullRequestActorTests
        {
            private async Task WhenUpdateAssetsAsyncIsCalled(Build forBuild)
            {
                await Execute(
                    async context =>
                    {
                        PullRequestActor actor = CreateActor(context);
                        await actor.Implementation.UpdateAssetsAsync(
                            Subscription.Id,
                            forBuild.Id,
                            SourceRepo,
                            NewCommit,
                            forBuild.Assets.Select(
                                    a => new Asset
                                    {
                                        Name = a.Name,
                                        Version = a.Version
                                    })
                                .ToList());
                    });
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public async Task UpdateWithAssetsNoExistingPR(bool batchable)
            {
                GivenATestChannel();
                GivenASubscription(
                    new SubscriptionPolicy
                    {
                        Batchable = batchable,
                        UpdateFrequency = UpdateFrequency.EveryBuild
                    });
                Build b = GivenANewBuild(true);

                WithRequireNonCoherencyUpdates(b);
                WithNoRequiredCoherencyUpdates();

                CreatePullRequestShouldReturnAValidValue();

                await WhenUpdateAssetsAsyncIsCalled(b);

                ThenGetRequiredUpdatesShouldHaveBeenCalled(b);
                AndCreateNewBranchShouldHaveBeenCalled();
                AndCommitUpdatesShouldHaveBeenCalled(b);
                AndCreatePullRequestShouldHaveBeenCalled();
                AndShouldHavePullRequestCheckReminder();
                AndShouldHaveInProgressPullRequestState(b);
                AndDependencyFlowEventsShouldBeAdded();
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public async Task UpdateWithAssetsExistingPR(bool batchable)
            {
                GivenATestChannel();
                GivenASubscription(
                    new SubscriptionPolicy
                    {
                        Batchable = batchable,
                        UpdateFrequency = UpdateFrequency.EveryBuild
                    });
                Build b = GivenANewBuild(true);

                WithRequireNonCoherencyUpdates(b);
                WithNoRequiredCoherencyUpdates();
          
                using (WithExistingPullRequest(SynchronizePullRequestResult.InProgressCanUpdate))
                {
                    await WhenUpdateAssetsAsyncIsCalled(b);
                    ThenGetRequiredUpdatesShouldHaveBeenCalled(b);
                    AndCommitUpdatesShouldHaveBeenCalled(b);
                    AndUpdatePullRequestShouldHaveBeenCalled();
                    AndShouldHavePullRequestCheckReminder();
                    AndDependencyFlowEventsShouldBeAdded();
                }
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public async Task UpdateWithAssetsExistingPRNotUpdatable(bool batchable)
            {
                GivenATestChannel();
                GivenASubscription(
                    new SubscriptionPolicy
                    {
                        Batchable = batchable,
                        UpdateFrequency = UpdateFrequency.EveryBuild
                    });
                Build b = GivenANewBuild(true);

                WithRequireNonCoherencyUpdates(b);
                WithNoRequiredCoherencyUpdates();
                using (WithExistingPullRequest(SynchronizePullRequestResult.InProgressCannotUpdate))
                {
                    await WhenUpdateAssetsAsyncIsCalled(b);

                    ThenShouldHavePullRequestUpdateReminder();
                    AndShouldHavePendingUpdateState(b);
                }
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public async Task UpdateWithNoAssets(bool batchable)
            {
                GivenATestChannel();
                GivenASubscription(
                    new SubscriptionPolicy
                    {
                        Batchable = batchable,
                        UpdateFrequency = UpdateFrequency.EveryBuild
                    });
                Build b = GivenANewBuild(true, Array.Empty<(string, string, bool)>());

                WithRequireNonCoherencyUpdates(b);
                WithNoRequiredCoherencyUpdates();

                await WhenUpdateAssetsAsyncIsCalled(b);

                ThenGetRequiredUpdatesShouldHaveBeenCalled(b);
                AndSubscriptionShouldBeUpdatedForMergedPullRequest(b);
            }
        }
    }
}
