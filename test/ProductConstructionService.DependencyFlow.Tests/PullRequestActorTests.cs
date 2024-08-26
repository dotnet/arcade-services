// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Services.Common;
using Moq;
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

    private string _newBranch = null!;

    protected override void RegisterServices(IServiceCollection services)
    {
        services.AddGitHubTokenProvider();
        services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        services.AddScoped<IBasicBarClient, SqlBarClient>();
        services.AddTransient<IPullRequestBuilder, PullRequestBuilder>();
        services.AddSingleton(MergePolicyEvaluator.Object);
        services.AddSingleton(UpdateResolver.Object);

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
        var captureNewBranch = new CaptureMatch<string>(newBranch => _newBranch = newBranch);
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
                    _newBranch ?? InProgressPrHeadBranch,
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
                        HeadBranch = _newBranch
                    }
                },
                options => options.Excluding(pr => pr.Title).Excluding(pr => pr.Description));

        ValidatePRDescriptionContainsLinks(pullRequests[0]);
    }

    protected void AndCodeFlowPullRequestShouldHaveBeenCreated()
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
                options => options
                    .Excluding(pr => pr.Title)
                    .Excluding(pr => pr.Description));
    }

    protected void ThenPcsShouldNotHaveBeenCalled(Build build, string? prUrl = null)
    {
        CodeFlowWorkItemsProduced
            .Should()
            .NotContain(request => request.BuildId == build.Id && (prUrl == null || request.PrUrl == prUrl));
    }

    protected void ThenPcsShouldHaveBeenCalled(Build build, string? prUrl, out string prBranch)
        => AndPcsShouldHaveBeenCalled(build, prUrl, out prBranch);

    protected void AndPcsShouldHaveBeenCalled(Build build, string? prUrl, out string prBranch)
    {
        var workItem = CodeFlowWorkItemsProduced
            .FirstOrDefault(request => request.SubscriptionId == Subscription.Id && request.BuildId == build.Id && (prUrl == null || request.PrUrl == prUrl));

        workItem.Should().NotBeNull();
        prBranch = workItem!.PrBranch;
    }

    protected static void ValidatePRDescriptionContainsLinks(PullRequest pr)
    {
        pr.Description.Should().Contain("][1]");
        pr.Description.Should().Contain("[1]:");
    }

    protected void CreatePullRequestShouldReturnAValidValue()
    {
        DarcRemotes[TargetRepo]
            .Setup(s => s.CreatePullRequestAsync(It.IsAny<string>(), It.IsAny<PullRequest>()))
            .ReturnsAsync(PrUrl);
    }

    protected void WithExistingPrBranch()
    {
        DarcRemotes[TargetRepo]
            .Setup(s => s.BranchExistsAsync(TargetRepo, It.IsAny<string>()))
            .ReturnsAsync(true);
    }

    protected void WithoutExistingPrBranch()
    {
        DarcRemotes[TargetRepo]
            .Setup(s => s.BranchExistsAsync(TargetRepo, It.IsAny<string>()))
            .ReturnsAsync(false);
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
            .ReturnsAsync((IEnumerable<DependencyDetail> dependencies, IRemoteFactory factory) => []);
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
            DarcRemotes.GetOrAddValue(TargetRepo, () => CreateMock<IRemote>())
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
                    DarcRemotes[TargetRepo].Verify(r => r.GetPullRequestAsync(InProgressPrUrl));
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
